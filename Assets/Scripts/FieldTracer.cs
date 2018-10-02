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

If we have a cheap measure of the **field gradient**, we can more easily
escape the gravitational pull of nearby geometry we'll never hit.

Also: use rational trig and calc to go sqrt-less
*/

namespace Tracing {
    public class FieldTracer : MonoBehaviour {
        [SerializeField] private bool _renderHighQuality;
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
            RaysPerPixel = 1,
            MaxDepth = 2,
        };

        private TraceJobQuality _fullQuality = new TraceJobQuality()
        {
            RaysPerPixel = 64,
            MaxDepth = 32,
        };

        private Texture2D _tex;

        private void Awake() {
            const int vertResolution = 1080;
            const float aspect = 16f / 9f;
            const float vfov = 50f;
            const float aperture = 0.002f;

            int horiResolution = (int)math.round(vertResolution * aspect);
            _fullQuality.Resolution = new int2(horiResolution, vertResolution);
            _debugQuality.Resolution = _fullQuality.Resolution;

            var position = new float3(10f, 1.5f, -2f);
            var lookDir = new float3(12, 1, -10) - position;
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

            _tex = new Texture2D((int)_fullQuality.Resolution.x, (int)_fullQuality.Resolution.y, TextureFormat.ARGB32, false, true);
            _tex.filterMode = FilterMode.Point;

            Debug.Log(-2.5f % 1f);
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

            scene.Spheres = new NativeArray<Sphere>(0, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            scene.Planes = new NativeArray<Plane>(0, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            scene.Disks = new NativeArray<Disk>(0, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

            return scene;
        }

        private void Start() {
            // Hack: do a cheap render first, editor performs it in managed code for on first run for some reason
            _trace.Quality = _debugQuality;
            StartRender();
            CompleteRender();

            // Now do a full-quality render
            if (_renderHighQuality) {
                _trace.Quality = _fullQuality;
                StartRender();
            }
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
            _renderHandle = _clear.Schedule(_screen.Length, 32, _renderHandle.Value);
            _renderHandle = _trace.Schedule(_screen.Length, 8, _renderHandle.Value);
        }

        private void CompleteRender() {
            _renderHandle.Value.Complete();

            _watch.Stop();
            Debug.Log("Done! Time taken: " + _watch.ElapsedMilliseconds + "ms, Num Rays: " + _trace.RayCounter[0]);
            Debug.Log("That's about " + (_trace.RayCounter[0] / (_watch.ElapsedMilliseconds / 1000.0d)) / 1000000.0d + " MRay/sec");
            Util.ToTexture2D(_screen, _tex, _fullQuality.Resolution);
            _renderHandle = null;
        }

        private void OnGUI() {
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), _tex, ScaleMode.ScaleToFit);
        }

        [BurstCompile]
        public struct ClearJob : IJobParallelFor {
            public NativeArray<float3> Buffer;
            public void Execute(int i) {
                Buffer[i] = new float3(0f);
            }
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

                var screenPos = Math.ToXYFloat(i, Quality.Resolution);
                float3 pixel = new float3(0f);

                ushort traceCount = 0;
                ushort usefulRays = 0;

                for (int r = 0; r < Quality.RaysPerPixel; r++) {
                    float2 jitter = new float2(rng.NextFloat(), rng.NextFloat());
                    float2 p = (screenPos + jitter) / (float2)Quality.Resolution;
                    var ray = Camera.GetRay(p, ref rng);
                    float3 col = TraceRecursive(ray, Scene, ref rng, Fibs, 0, Quality.MaxDepth, ref traceCount);
                    if (math.lengthsq(col) > 0.00001f) {
                        pixel += col;
                        usefulRays++;
                    }
                }

                Screen[i] = math.sqrt(pixel / (float)usefulRays);

                RayCounter[0] += traceCount; // Todo: atomics, or rather, fix the amount of rays per job run
            }
        }

        private static float3 TraceRecursive(Ray3f ray, Scene scene, ref Random rng, NativeArray<float3> fibs, int depth, int maxDepth, ref ushort rayCount) {
            HitRecord hit;

            bool hitSomething = IntersectField(ray, scene, 512, 512f, out hit);
            ++rayCount;

            float3 light = new float3(0,0,0);

            if (hitSomething) {
                Ray3f nextRay;
                bool scattered = Scatter(ray, hit, ref rng, fibs, out nextRay);
                if (scattered && depth < maxDepth) {
                    light = TraceRecursive(nextRay, scene, ref rng, fibs, depth + 1, maxDepth, ref rayCount);
                }
                light = Trace.BRDF(hit) * light;
            } else {
                float t = 0.5f * (ray.direction.y + 1f);
                light = (1f - t) * new float3(1f) + t * new float3(0.15f, 0.2f, 4f);
            }

            return light;
        }

        public static bool Scatter(Ray3f ray, HitRecord hit, ref Random rng, NativeArray<float3> fibs, out Ray3f scattered) {
            const float epsEscape = 0.002f;

            const float refIdx = 1.5f;

            switch (hit.material.Type) {
                case MaterialType.Dielectric: {
                        float3 outwardNormal;
                        float nint;
                        float3 reflected = math.reflect(ray.direction, hit.normal);
                        float cosine;

                        if (math.dot(ray.direction, hit.normal) > 0f) {
                            outwardNormal = -hit.normal;
                            nint = refIdx;
                            cosine = refIdx * math.dot(ray.direction, hit.normal);
                        } else {
                            outwardNormal = hit.normal;
                            nint = 1f / refIdx;
                            cosine = -math.dot(ray.direction, hit.normal);
                        }

                        float reflectProb = 1f;
                        float3 refracted;
                        if (Trace.Refract(ray.direction, outwardNormal, nint, out refracted)) {
                            reflectProb = Trace.Schlick(cosine, refIdx);
                        }

                        bool reflect = rng.NextFloat() < reflectProb;

                        scattered = new Ray3f(
                            reflect ? hit.point + outwardNormal * epsEscape : hit.point - outwardNormal * epsEscape,
                            reflect ? reflected : refracted
                        );
                        return true;
                    }
                case MaterialType.Metal: {
                        float3 transmitted = math.reflect(ray.direction, hit.normal);
                        transmitted += fibs[rng.NextInt(0, fibs.Length - 1)] * hit.material.Fuzz;
                        transmitted = math.normalize(transmitted);
                        scattered = new Ray3f(hit.point + hit.normal * epsEscape, transmitted);
                        if (math.dot(scattered.direction, hit.normal) > 0) {
                            return true;
                        }
                        return false;
                    }
                case MaterialType.Lambertian:
                default: {
                        float3 target = hit.normal + fibs[rng.NextInt(0, fibs.Length - 1)];
                        float3 transmitted = math.normalize(target - hit.point);
                        scattered = new Ray3f(hit.point + hit.normal * epsEscape, transmitted);
                        return true;
                    }
            }
        }

        // Todo: optimize by splitting the below into boolean intersection funct, and then:
        // dist field
        // color field
        // normal field
        // material field
        // evaluating those for all the intermediate steps is useless
        // basically: https://www.shadertoy.com/view/ldl3zN IQ piano

        private static bool IntersectField(Ray3f r, Scene scene, short maxStep, float maxDist, out HitRecord hit) {
            hit = new HitRecord();

            const float eps = 0.001f;

            float3 p = r.origin;
            
            float totalDist = 0;
            for (int d = 0; d < maxStep; d++) {
                float dist = 99999f;

                float3 normal = new float3(0,0,1);
                Material material = new Material();
                HitSphere(p, ref dist, ref normal, ref material);
                HitSpheres(p, ref dist, ref normal, ref material);
                HitFloor(p, ref dist, ref normal, ref material);

                if (dist <= eps) {
                    hit.point = p;
                    hit.normal = normal;
                    hit.material = material;
                    hit.t = totalDist;
                    return true;
                }

                totalDist += dist;
                p += r.direction * dist;
            }

            return false;
        }

        private static void HitSphere(float3 p, ref float curDist, ref float3 normal, ref Material mat) {
            float3 spherePos = new float3(0f, 21f, -40f);
            float sphereRad = 20f;
            float3 pos = p - spherePos;

            float dist = SDF.Sphere(pos, sphereRad);

            if (dist < curDist) {
                curDist = dist;
                normal = pos / sphereRad;
                mat = new Material(MaterialType.Metal, new float3(0.33f));
            }
        }

        private static void HitSpheres(float3 p, ref float curDist, ref float3 normal, ref Material mat) {
            float interval = 2f;
            
            float3 pos = p + new float3(100f, 0f, 100f);

            int2 period = new int2(
                ((int)(pos.x / interval)) % 7,
                ((int)(pos.z / interval)) % 17);

            pos = SDF.ModXZ(pos, new float2(interval, interval)); // Todo: how to make consistent for negative quadrants?

            /*  Todo:
             *  doing the following makes the field discontinuous as the modular boundary is crossed
                float3 spherePos = new float3(0f, period % 2, 0f);
                need a different way of thinking about instance parameters in repeated space

                dont' calculate material/color/normal calculation until after hit is found
                do them in a separate function
             */

            float3 spherePos = new float3(0f, 0f, 0f); // location in worldmodxspace
            float sphereRad = 0.5f;
            pos = pos - spherePos;

            float dist = SDF.Sphere(pos, sphereRad);

            if (dist < curDist) {
                curDist = dist;
                normal = pos / sphereRad;
                mat = new Material(MaterialType.Metal, new float3(period.x / 7f, 0.75f, period.y / 17f));
            }
        }

        private static void HitFloor(float3 p, ref float curDist, ref float3 normal, ref Material mat) {
            float3 planeNormal = new float3(0,1,0);
            float3 planePos = new float3(0f, -0.5f, 0f);
            float dist = math.dot(p - planePos, planeNormal);
            if (dist < curDist) {
                curDist = dist;
                normal = planeNormal;
                mat = new Material(MaterialType.Lambertian, new float3(0.9f, 0.95f, 0.85f));
            }
        }

        private struct TraceJobQuality {
            public int2 Resolution;
            public int RaysPerPixel;
            public int MaxDepth;
        }
    }
}
