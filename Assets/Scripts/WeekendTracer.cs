using UnityEngine;
using Unity.Collections;
using Unity.Burst;
using Unity.Jobs;
using Unity.Mathematics;
using Ramjet;
using Random = Unity.Mathematics.Random;

/* 
Todo:

Performance varies wildly for small variations in scene. Sometimes 70MRay/sec, sometimes 10. Egalize.

- Find a nicer way to stop jobs in progress in case you want to abort a render. It's messes up the editor now.
    - Splitting a render into a sequence of smaller jobs would work well
    - We want an iterative renderer anyway
    - It will also lessen the appearance of the 4 frame TempAllocator warning
    - Would probably also stop major cpu/os stalls from happening
- Show incremental results
- This way getting from a Burst-style screenbuffer to a cpu-texture to a gpu render is dumb
    - Compute buffer could be faster

- Ray termination time is wildly divergent. You need to make that constant-ish. There are many ways.

Now that we've done this we can compare some aspects of this style of tracing to distance fields
- Without any culling or sorting, each ray here has to be prepared to intersect with all possible
objects in the scene. So each step you loop over all objects. This gets expensive crazy fast, it
goes up as n objects and r bounces happen.
- With a single distance field to sample from that encodes multiple parts of the scene, for many
objects and many rays, it starts winning.

So, to optimize there's a couple of things we should try to do:
- Don't use ad-hoc functional recursion
- Cull objects, or otherwise limit the amount of objects a ray has to computationally interact with
- Limit super-sampling for parts of the image that need it.
- When generating random numbers, think carefully how many bits of entropy you need
- Can you at all use cached irradiance? When hitting a surface, if there has been prior local light information, use it?
- Render iteratively, adaptively

For multiple object types and lists per scene, we can make a type system that matches primitive type to
trace function.
*/

namespace Weekend {
    public class WeekendTracer : MonoBehaviour {
        [SerializeField] private bool _drawDebugRays;
        [SerializeField] private bool _saveImage;
        [SerializeField] private string _saveFolder = "C:\\Users\\Martijn\\Desktop\\weekendtracer\\";

        private NativeArray<float3> _screen;
        private NativeArray<float3> _fibs;

        private Scene _scene;
        private Camera _camera;

        private ClearJob _clear;
        private TraceJob _trace;


        private TraceJobQuality _debugQuality = new TraceJobQuality() {
            tMin = 0,
            tMax = 1000,
            RaysPerPixel = 4,
            MaxDepth = 8,
        };

        private TraceJobQuality _fullQuality = new TraceJobQuality() {
            tMin = 0,
            tMax = 1000,
            RaysPerPixel = 512,
            MaxDepth = 64,
        };

        private Color[] _colors;
        private Texture2D _tex;

        private void Awake() {
            const uint vertResolution = 512;
            const float aspect = 21f / 9f;
            const float vfov = 25f;
            const float aperture = 0.1f;

            uint horiResolution = (uint)math.round(vertResolution * aspect);
            _fullQuality.Resolution = new uint2(horiResolution, vertResolution);
            _debugQuality.Resolution = _fullQuality.Resolution;

            var position = new float3(-3f, 1f, -2f);
            var lookDir = new float3(0, 0, 1) - position;
            var focus = math.length(lookDir);
            var rotation = quaternion.LookRotation(lookDir / focus, new float3(0, 1, 0));
            _camera = new Camera(vfov, aspect, aperture, focus);
            _camera.Position = position;
            _camera.Rotation = rotation;

            Debug.Log("Resolution = " + _fullQuality.Resolution);

            int totalPixels = (int)(_fullQuality.Resolution.x * _fullQuality.Resolution.y);
            _screen = new NativeArray<float3>(totalPixels, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            _clear = new ClearJob();
            _clear.Buffer = _screen;

            _scene = MakeScene();

            _fibs = new NativeArray<float3>(4096, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            Math.GenerateFibonacciSphere(_fibs);

            _trace = new TraceJob();
            _trace.Screen = _screen;
            _trace.Fibs = _fibs;
            _trace.Scene = _scene;
            _trace.Camera = _camera;
            _trace.RayCounter = new NativeArray<ulong>(1, Allocator.Persistent, NativeArrayOptions.ClearMemory);

            _colors = new Color[totalPixels];
            _tex = new Texture2D((int)_fullQuality.Resolution.x, (int)_fullQuality.Resolution.y, TextureFormat.ARGB32, false, true);
            _tex.filterMode = FilterMode.Point;
        }

        private void OnDestroy() {
            _screen.Dispose();
            _scene.Dispose();
            _fibs.Dispose();
            _trace.RayCounter.Dispose();
        }

        private static Scene MakeScene() {
            var scene = new Scene();

            scene.LightDir = math.normalize(new float3(-2f, -1, -0.33f));
            scene.LightColor = new float3(0.5f, 0.7f, 1f);

            var rng = new Random(1234);

            scene.Spheres = new NativeArray<Sphere>(7, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            scene.Spheres[0] = new Sphere(new float3(0, -100.5f, 1), 100f, new Material(MaterialType.Lambertian, new float3(0.8f, 0.8f, 0f)));
            scene.Spheres[1] = new Sphere(new float3(-1, 0, 1), 0.5f, new Material(MaterialType.Lambertian, new float3(0.1f, 0.2f, 0.5f)));
            scene.Spheres[2] = new Sphere(new float3(1, 0, 1), 0.5f, new Material(MaterialType.Metal, new float3(0.8f, 0.6f, 0.2f)));
            scene.Spheres[3] = new Sphere(new float3(0, 0, 1), 0.5f, new Material(MaterialType.Dielectric, new float3(1f, 1f, 1f)));
            scene.Spheres[4] = new Sphere(new float3(0, 0, 1), -0.45f, new Material(MaterialType.Dielectric, new float3(1f, 1f, 1f)));
            scene.Spheres[5] = new Sphere(new float3(0, 0, 1), 0.4f, new Material(MaterialType.Dielectric, new float3(1f, 1f, 1f)));
            scene.Spheres[6] = new Sphere(new float3(0, 0, 1), -0.35f, new Material(MaterialType.Dielectric, new float3(1f, 1f, 1f)));

            scene.Planes = new NativeArray<Plane>(0, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            scene.Disks = new NativeArray<Disk>(0, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

            return scene;
        }

        private void Start() {
            // // Hack: do a cheap render first, editor performs it in managed code for on first run for some reason
            _trace.Quality = _debugQuality;
            StartRender();
            CompleteRender();

            // Now do a full-quality render
            _trace.Quality = _fullQuality;
            StartRender();
        }

        private void Update() {
            if (_renderHandle.HasValue && _renderHandle.Value.IsCompleted) {
                CompleteRender();
                if (_saveImage) {
                    Util.ExportImage(_tex, _saveFolder);
                }
            } else {
                if (Input.GetKeyDown(KeyCode.Space)) {
                    StartRender();
                }
            }
        }

        private void OnDrawGizmos() {
            if (!Application.isPlaying || !_drawDebugRays) {
                return;
            }

            uint i = _fullQuality.Resolution.x * (_fullQuality.Resolution.y / 2) + _fullQuality.Resolution.x / 2;
            var rng = new Unity.Mathematics.Random(14387 + ((uint)i * 7));

            var screenPos = Math.ToXY(i, _fullQuality.Resolution);
            float3 pixel = new float3(0f);

            Gizmos.color = new Color(1f, 1f, 1f, 0.5f);
            for (int s = 0; s < _scene.Spheres.Length; s++) {
                Gizmos.DrawSphere(_scene.Spheres[s].Center, _scene.Spheres[s].Radius);
            }

            for (int r = 0; r < 16; r++) {
                Gizmos.color = Color.HSVToRGB(r / (float)_trace.Quality.RaysPerPixel, 0.7f, 0.5f);
                float2 jitter = new float2(rng.NextFloat(), rng.NextFloat());
                float2 p = (screenPos + jitter) / (float2)_fullQuality.Resolution;

                var ray = _camera.GetRay(p, ref rng);

                for (int t = 0; t < _trace.Quality.MaxDepth; t++) {
                    const float tMin = 0f;
                    const float tMax = 1000f;

                    Gizmos.DrawSphere(ray.origin, 0.01f);

                    HitRecord hit;
                    bool hitSomething = Intersect.Scene(_scene, ray, tMin, tMax, out hit);
                    if (hitSomething) {
                        Gizmos.DrawLine(ray.origin, hit.point);
                        Ray3f subRay;
                        float reflectProb = 1f;
                        if (!Trace.Scatter(ray, hit, ref rng, _trace.Fibs, out subRay, out reflectProb)) {
                            break;
                        }
                        ray = subRay;
                    } else {
                        Gizmos.DrawRay(ray.origin, math.normalize(ray.direction));
                        break;
                    }
                }
            }
        }

        private JobHandle? _renderHandle;
        private System.Diagnostics.Stopwatch _watch;

        private void StartRender() {
            if (_renderHandle.HasValue) {
                Debug.LogWarning("Cannot start new render while previous one is still busy");
                return;
            }

            Debug.Log("Rendering...");

            _watch = System.Diagnostics.Stopwatch.StartNew();

            _trace.RayCounter[0] = 0;

            _renderHandle = new JobHandle();
            _renderHandle = _clear.Schedule(_screen.Length, 4, _renderHandle.Value);
            _renderHandle = _trace.Schedule(_screen.Length, 4, _renderHandle.Value);
        }

        private void CompleteRender() {
            _renderHandle.Value.Complete();

            _watch.Stop();
            Debug.Log("Done! Time taken: " + _watch.ElapsedMilliseconds + "ms, Num Rays: " + _trace.RayCounter[0]);
            Debug.Log("That's about " + (_trace.RayCounter[0] / (_watch.ElapsedMilliseconds / 1000.0d)) / 1000000.0d + " MRay/sec");
            Util.ToTexture2D(_screen, _colors, _tex, _fullQuality.Resolution);
            _renderHandle = null;
        }

        private void OnGUI() {
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), _tex, ScaleMode.ScaleToFit);
        }

        [BurstCompile]
        private struct ClearJob : IJobParallelFor {
            public NativeArray<float3> Buffer;
            public void Execute(int i) {
                Buffer[i] = 0f;
            }
        }

        [BurstCompile]
        private struct TraceJob : IJobParallelFor {
            [WriteOnly] public NativeArray<float3> Screen;
            [ReadOnly] public Scene Scene;
            [ReadOnly] public NativeArray<float3> Fibs;
            [ReadOnly] public Camera Camera;
            [ReadOnly] public TraceJobQuality Quality;

            [NativeDisableParallelForRestriction] public NativeArray<ulong> RayCounter;

            public void Execute(int i) {
                var rng = new Unity.Mathematics.Random(14387 + ((uint)i * 7));

                var screenPos = Math.ToXY((uint)i, Quality.Resolution);
                float3 pixel = new float3(0f);

                ushort rayCount = 0;

                for (int r = 0; r < Quality.RaysPerPixel; r++) {
                    float2 jitter = new float2(rng.NextFloat(), rng.NextFloat());
                    float2 p = (screenPos + jitter) / (float2)Quality.Resolution;
                    var ray = Camera.GetRay(p, ref rng);
                    pixel += Trace.TraceRecursive(ray, Scene, ref rng, Fibs, 0, Quality.MaxDepth, ref rayCount);
                }

                Screen[i] = math.sqrt(pixel / (float)(Quality.RaysPerPixel));

                RayCounter[0] += rayCount; // Todo: atomics, or rather, fix the amount of rays per job run
            }
        }

        private struct TraceJobQuality {
            public uint2 Resolution;
            public float tMin;
            public float tMax;
            public int RaysPerPixel;
            public int MaxDepth;
        }
    }
}
