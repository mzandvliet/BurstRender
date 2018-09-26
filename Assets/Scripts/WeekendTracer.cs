using UnityEngine;
using Unity.Collections;
using Unity.Burst;
using Unity.Jobs;
using Unity.Mathematics;
using Ramjet;
using Random = Unity.Mathematics.Random;

/* 
Todo:

Light, motion blur, modeled shapes and materials.
BVH-like structure for broad phase intersections
Try iterative-adaptive sampling strategies (fixed amount of ray evaluations per frame, distribute wisely)

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

namespace Tracing {
    public class WeekendTracer : MonoBehaviour {
        [SerializeField] private bool _drawDebugRays;
        [SerializeField] private bool _saveImage;
        [SerializeField] private string _saveFolder = "C:\\Users\\Martijn\\Desktop\\weekendtracer\\";

        private NativeArray<float3> _screenBuffer;
        private NativeArray<float3> _fibs;

        private Scene _scene;
        private Camera _camera;

        private ClearJob _clear;

        private TraceJobQuality _quality = new TraceJobQuality() {
            tMin = 0,
            tMax = 1000,
            RaysPerPixel = 2,
            MaxDepth = 64,
        };

        private Color[] _colors;
        private Texture2D _tex;

        private void Awake() {
            const uint vertResolution = 64;
            const float aspect = 21f / 9f;
            const float vfov = 25f;
            const float aperture = 0.1f;

            uint horiResolution = (uint)math.round(vertResolution * aspect);
            _quality.Resolution = new uint2(horiResolution, vertResolution);

            var position = new float3(-3f, 1f, -2f);
            var lookDir = new float3(0, 0, 1) - position;
            var focus = math.length(lookDir);
            var rotation = quaternion.LookRotation(lookDir / focus, new float3(0, 1, 0));
            _camera = new Camera(vfov, aspect, aperture, focus);
            _camera.Position = position;
            _camera.Rotation = rotation;

            Debug.Log("Resolution = " + _quality.Resolution);

            int totalPixels = (int)(_quality.Resolution.x * _quality.Resolution.y);
            _screenBuffer = new NativeArray<float3>(totalPixels, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            _clear = new ClearJob();
            _clear.Buffer = _screenBuffer;

            _scene = MakeScene();

            _fibs = new NativeArray<float3>(4096, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            Math.GenerateFibonacciSphere(_fibs);

            _colors = new Color[totalPixels];
            _tex = new Texture2D((int)_quality.Resolution.x, (int)_quality.Resolution.y, TextureFormat.ARGB32, false, true);
            _tex.filterMode = FilterMode.Point;

            //_handles = new NativeArray<JobHandle>(8, Allocator.);
        }

        private void OnDestroy() {
            _screenBuffer.Dispose();
            _scene.Dispose();
            _fibs.Dispose();
        }

        private static Scene MakeScene() {
            var scene = new Scene();

            scene.LightDir = math.normalize(new float3(-1f, -1, -0.33f));
            scene.LightColor = new float3(0.15f, 0.2f, 4f);

            var rng = new Random(1234);

            scene.Spheres = new NativeArray<Sphere>(7, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            scene.Spheres[0] = new Sphere(new float3(0, -100.5f, 1), 100f, new Material(MaterialType.Lambertian, new float3(0.8f, 0.8f, 0f)));
            scene.Spheres[1] = new Sphere(new float3(1, 0, 1), 0.5f, new Material(MaterialType.Lambertian, new float3(0.1f, 0.2f, 0.5f)));
            scene.Spheres[2] = new Sphere(new float3(-1, 0, 1), 0.5f, new Material(MaterialType.Metal, new float3(0.8f, 0.6f, 0.2f)));
            scene.Spheres[3] = new Sphere(new float3(0, 0, 1), 0.5f, new Material(MaterialType.Dielectric, new float3(1f, 1f, 1f)));
            scene.Spheres[4] = new Sphere(new float3(0, 0, 1), -0.45f, new Material(MaterialType.Dielectric, new float3(1f, 1f, 1f)));
            scene.Spheres[5] = new Sphere(new float3(0, 0, 1), 0.4f, new Material(MaterialType.Dielectric, new float3(1f, 1f, 1f)));
            scene.Spheres[6] = new Sphere(new float3(0, 0, 1), -0.35f, new Material(MaterialType.Dielectric, new float3(1f, 1f, 1f)));

            scene.Planes = new NativeArray<Plane>(0, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            scene.Disks = new NativeArray<Disk>(0, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

            return scene;
        }

        //private NativeArray<JobHandle> _handles;
        private bool _isRendering;
        private System.Diagnostics.Stopwatch _watch;
        private uint _pixelIdx;
        private ulong _rayCount;

        private void Start() {
            StartRender();
        }

        private void Update() {
            if (_isRendering) {
                var pixelCoord = Math.To2DCoords(_pixelIdx, _quality.Resolution);

                var j = TraceJob.Create(pixelCoord, _quality.Resolution, _fibs, _scene, _camera);
                var h = j.Schedule();

                h.Complete();

                var col = new Color(j.OutColor[0].x, j.OutColor[0].y, j.OutColor[0].z, 1f);
                _tex.SetPixel((int)pixelCoord.x, (int)pixelCoord.y, col);
                _tex.Apply();
                Debug.Log(pixelCoord + ", " + j.OutRayCount[0]);
                _rayCount += j.OutRayCount[0];
                _pixelIdx++;

                j.Dispose();

                if (_pixelIdx >= _screenBuffer.Length) {
                    CompleteRender();
                }
            }
        }

        private void StartRender() {
            _isRendering = true;

            _watch = System.Diagnostics.Stopwatch.StartNew();
            _pixelIdx = 0;
            _rayCount = 0;
        }

        private void CompleteRender() {
            _isRendering = false;

            _watch.Stop();
            Debug.Log("Done! Time taken: " + _watch.ElapsedMilliseconds + "ms, Num Rays: " + _rayCount);
            Debug.Log("That's about " + (_rayCount / (_watch.ElapsedMilliseconds / 1000.0d)) / 1000000.0d + " MRay/sec");

            //Util.ToTexture2D(_screen, _colors, _tex, _quality.Resolution);

            if (_saveImage) {
                Util.ExportImage(_tex, _saveFolder);
            }
        }

        private void OnGUI() {
            GUI.DrawTexture(new Rect(0f, 30f, Screen.width, Screen.height - 30f), _tex, ScaleMode.ScaleToFit);
            GUILayout.Label("Current pixel: " + (int)_pixelIdx + "/" + _screenBuffer.Length);
        }

        

        [BurstCompile]
        private struct TraceJob : IJob, System.IDisposable {
            [ReadOnly] public uint2 PixelCoord;
            [ReadOnly] public uint2 Resolution;
            [ReadOnly] public Scene Scene;
            [ReadOnly] public Camera Camera;
            [ReadOnly] public TraceJobQuality Quality;
            [ReadOnly] public NativeArray<float3> Fibs;

            [WriteOnly] public NativeArray<float3> OutColor;
            [WriteOnly] public NativeArray<ushort> OutRayCount;

            public static TraceJob Create(uint2 pixelCoord, uint2 resolution, NativeArray<float3> fibs, Scene scene, Camera camera) {
                var tj = new TraceJob();
                tj.PixelCoord = pixelCoord;
                tj.Resolution = resolution;
                tj.Fibs = fibs;
                tj.Scene = scene;
                tj.Camera = camera;

                tj.OutColor = new NativeArray<float3>(1, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                tj.OutRayCount = new NativeArray<ushort>(1, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                tj.OutColor[0] = new float3(0,1,0);
                tj.OutRayCount[0] = 0;
                return tj;
            }

            public void Dispose() {
                OutColor.Dispose();
                OutRayCount.Dispose();
            }

            public void Execute() {
                uint screenIdx = Math.To1Dindex(PixelCoord, Resolution);
                var rng = new Unity.Mathematics.Random(14387 + (screenIdx * 7));

                ushort rayCount = 0;
                float3 pixel = new float3(0f);
                // for (int r = 0; r < Quality.RaysPerPixel; r++) {
                //     float2 jitter = new float2(rng.NextFloat(), rng.NextFloat());
                //     float2 p = (PixelIndex + jitter) / (float2)Quality.Resolution;
                //     var ray = Camera.GetRay(p, ref rng);
                //     pixel += Trace.TraceRecursive(ray, Scene, ref rng, Fibs, 0, Quality.MaxDepth, ref rayCount);
                // }

                float2 jitter = new float2(rng.NextFloat(), rng.NextFloat());
                float2 p = (PixelCoord + jitter) / (float2)Quality.Resolution;
                var ray = Camera.GetRay(p, ref rng);
                pixel += Trace.TraceRecursive(ray, Scene, ref rng, Fibs, 0, Quality.MaxDepth, ref rayCount);

                OutColor[0] = math.sqrt(pixel / (float)Quality.RaysPerPixel);
                OutRayCount[0] = rayCount;
            }
        }

        private struct TraceJobQuality {
            public uint2 Resolution;
            public float tMin;
            public float tMax;
            public int RaysPerPixel;
            public int MaxDepth;
        }


        private void OnDrawGizmos() {
            if (!Application.isPlaying || !_drawDebugRays) {
                return;
            }

            uint i = _quality.Resolution.x * (_quality.Resolution.y / 2) + _quality.Resolution.x / 2;
            var rng = new Unity.Mathematics.Random(14387 + ((uint)i * 7));

            var screenPos = Math.ToNormalizedCoords(i, _quality.Resolution);
            float3 pixel = new float3(0f);

            Gizmos.color = new Color(1f, 1f, 1f, 0.5f);
            for (int s = 0; s < _scene.Spheres.Length; s++) {
                Gizmos.DrawSphere(_scene.Spheres[s].Center, _scene.Spheres[s].Radius);
            }

            const int numRays = 16;
            const int maxDepth = 16;

            for (int r = 0; r < numRays; r++) {
                Gizmos.color = Color.HSVToRGB(r / (float)numRays, 0.7f, 0.5f);
                float2 jitter = new float2(rng.NextFloat(), rng.NextFloat());
                float2 p = (screenPos + jitter) / (float2)_quality.Resolution;

                var ray = _camera.GetRay(p, ref rng);

                for (int t = 0; t < maxDepth; t++) {
                    const float tMin = 0f;
                    const float tMax = 1000f;
                    const float eps = 0.0001f;

                    Gizmos.DrawSphere(ray.origin, 0.01f);

                    HitRecord hit;
                    bool hitSomething = Intersect.Scene(_scene, ray, tMin, tMax, out hit);
                    if (hitSomething) {
                        Gizmos.DrawLine(ray.origin, hit.point);
                        Ray3f subRay;
                        if (!Trace.Scatter(ray, hit, ref rng, _fibs, out subRay, eps)) {
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
    }
}
