using UnityEngine;
using Unity.Collections;
using Unity.Burst;
using Unity.Jobs;
using Unity.Mathematics;
using Ramjet;
using Tracing;
using System.Runtime.CompilerServices;

using Random = Unity.Mathematics.Random;

/* 
Todo:

Distance field has different properties than classic intersection
- After a bounce, ray starts at normal*eps distance from surface,
and will leave it very slowly even though it will never hit it.
- Scatter function should know that inside sphere means negative dist
- viewpoints and rays parallel to surfaces wil ruin performance
*/

namespace Tracing {
    public class Tracer : MonoBehaviour {
        [SerializeField] private bool _drawDebugRays;
        [SerializeField] private bool _saveImage;
        [SerializeField] private string _saveFolder = "C:\\Users\\Martijn\\Desktop\\weekendtracer\\";

        private NativeArray<float3> _screen;
        private NativeArray<float3> _fibs;

        private Scene _scene;
        private Camera _camera;

        private ClearJob _clear;
        private TraceJob _trace;

        private TraceJobQuality _debugQuality = new TraceJobQuality()
        {
            tMin = 0,
            tMax = 1000,
            RaysPerPixel = 1,
            MaxDepth = 2,
        };

        private TraceJobQuality _fullQuality = new TraceJobQuality()
        {
            tMin = 0,
            tMax = 1000,
            RaysPerPixel = 128,
            MaxDepth = 32,
        };

        private Color[] _colors;
        private Texture2D _tex;

        private void Awake() {
            const uint vertResolution = 512;
            const float aspect = 16f / 9f;
            const float vfov = 50f;
            const float aperture = 0.002f;

            uint horiResolution = (uint)math.round(vertResolution * aspect);
            _fullQuality.Resolution = new uint2(horiResolution, vertResolution);
            _debugQuality.Resolution = _fullQuality.Resolution;

            var position = new float3(0f, 1f, -2f);
            var lookDir = new float3(0, 0, 0) - position;
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
            _trace.Camera = _camera;
            _trace.Scene = _scene;
            _trace.RayCounter = new NativeArray<ulong>(1, Allocator.Persistent, NativeArrayOptions.ClearMemory);

            _colors = new Color[totalPixels];
            _tex = new Texture2D((int)_fullQuality.Resolution.x, (int)_fullQuality.Resolution.y, TextureFormat.ARGB32, false, true);
            _tex.filterMode = FilterMode.Point;
        }

        private void OnDestroy() {
            _screen.Dispose();
            _fibs.Dispose();
            _scene.Dispose();
            _trace.RayCounter.Dispose();
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
            scene.Spheres[3] = new Sphere(new float3(0, 0, 1), 0.5f, new Material(MaterialType.Lambertian, new float3(1f, 1f, 1f)));

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
        private struct TraceJob : IJobParallelFor {
            [ReadOnly] public Scene Scene;
            [ReadOnly] public Camera Camera;
            [ReadOnly] public TraceJobQuality Quality;
            [ReadOnly] public NativeArray<float3> Fibs;

            [WriteOnly] public NativeArray<float3> Screen;
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
                    pixel += TraceRecursive(ray, Scene, ref rng, Fibs, 0, Quality.MaxDepth, ref rayCount);
                }

                Screen[i] = math.sqrt(pixel / (float)(Quality.RaysPerPixel));

                RayCounter[0] += rayCount; // Todo: atomics, or rather, fix the amount of rays per job run
            }
        }

        private static float3 TraceRecursive(Ray3f ray, Scene scene, ref Random rng, NativeArray<float3> fibs, int depth, int maxDepth, ref ushort rayCount) {
            HitRecord hit;

            if (depth >= maxDepth) {
                hit = new HitRecord();
                return new float3(0);
            }

            bool hitSomething = IntersectField(ray, scene, 256, 100f, out hit);
            ++rayCount;

            float3 light = new float3(0);

            if (hitSomething) {
                // We see a thing through another thing, find that other thing, see what it sees, it might be light, but might end void
                // Filter it through its material model

                Ray3f nextRay;
                bool scattered = Trace.Scatter(ray, hit, ref rng, fibs, out nextRay, 0.002f);
                if (scattered) {
                    light = TraceRecursive(nextRay, scene, ref rng, fibs, depth + 1, maxDepth, ref rayCount);
                }
                light = Trace.BRDF(hit, light);
            } else {
                // We see sunlight, just send that back through the path traversed

                float t = 0.5f * (ray.direction.y + 1f);
                light = (1f - t) * new float3(1f) + t * new float3(0.15f, 0.2f, 4f);
            }

            return light;
        }

        // Todo: wow this is pretty ugly and ineffecient, be smarter :P
        private static bool IntersectField(Ray3f r, Scene scene, short maxStep, float maxDist, out HitRecord hit) {
            hit = new HitRecord();

            const float EPS = 0.001f;

            float3 p = r.origin;

            float totalDist = 0;
            for (int d = 0; d < maxStep; d++) {
                float closestDist = float.MaxValue;
                int closestIdx = -1;
                for (int s = 0; s < scene.Spheres.Length; s++) {
                    var sph = scene.Spheres[s];
                    float dist = SDF.Sphere(p - sph.Center, sph.Radius);
                    if (dist < closestDist) {
                        closestDist = dist;
                        closestIdx = s;
                    }
                }

                totalDist += closestDist;

                if (closestDist < EPS) {
                    var sph = scene.Spheres[closestIdx];
                    hit.distance = totalDist;
                    hit.point = p;
                    hit.normal = (p-sph.Center) / sph.Radius;
                    hit.material = sph.Material;
                    return true;
                }

                if (totalDist > maxDist) {
                    return false;
                }

                p += r.direction * closestDist;
            }

            return false;
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
