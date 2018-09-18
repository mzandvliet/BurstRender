using UnityEngine;
using Unity.Collections;
using Unity.Burst;
using Unity.Jobs;
using Unity.Mathematics;
using RamjetMath;

using System.Runtime.CompilerServices;

/* 
Todo:

- Fix "Internal: JobTempAlloc has allocations that are more than 4 frames old - this is not allowed and likely a leak"
Guess: todo with recursive Trace calls
*/

namespace Weekend {
    public class WeekendTracer : MonoBehaviour {
        private NativeArray<float3> _screen;
        private NativeArray<Sphere> _spheres;

        private CameraInfo _camInfo = new CameraInfo(
            new int2(1024, 512),
            new float3(-2.0f, -1.0f, 1.0f),
            new float3(4f, 0f, 0f),
            new float3(0f, 2f, 0f));

        private Color[] _colors;
        private Texture2D _tex;

        private void Awake() {
            _screen = new NativeArray<float3>(_camInfo.resolution.x * _camInfo.resolution.y, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

            _colors = new Color[_camInfo.resolution.x * _camInfo.resolution.y];
            _tex = new Texture2D(_camInfo.resolution.x, _camInfo.resolution.y, TextureFormat.ARGB32, false, true);
            _tex.filterMode = FilterMode.Point;

            MakeScene();
        }

        private void OnDestroy() {
            _screen.Dispose();
            _spheres.Dispose();
        }

        private void MakeScene() {

            UnityEngine.Random.InitState(1234);

            _spheres = new NativeArray<Sphere>(8, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < _spheres.Length; i++) {
                var pos = new float3(
                    -3f + 6f * UnityEngine.Random.value,
                     -1f + 2f * UnityEngine.Random.value,
                     1.5f + 5f * UnityEngine.Random.value);
                var rad = 0.1f + UnityEngine.Random.value * 0.9f;
                var matType = MaterialType.Metal;
                var mat = new Material(matType, new float3(0.5f) + 0.5f * new float3(UnityEngine.Random.value, UnityEngine.Random.value, UnityEngine.Random.value));
                mat.Fuzz = math.pow(UnityEngine.Random.value * 0.6f, 2f);
                _spheres[i] = new Sphere(pos, rad, mat);
            }
        }

        private void Start() {
            // Fast dummy render for editor to get Burst to warm up, otherwise first render is suuuuper slow.
            // In fact, it seems like first exectution doesn't even use Burst?
            StartRender(1);
            CompleteRender();

            // Now trace at the quality we want.
            StartRender(512 * 32);
        }

        private void Update() {
            if (_renderHandle.HasValue && _renderHandle.Value.IsCompleted) {
                CompleteRender();
            } else {
                if (Input.GetKeyDown(KeyCode.Space)) {
                    StartRender(1);
                }
            }
        }

        private JobHandle? _renderHandle;
        private System.Diagnostics.Stopwatch _watch;

        private void StartRender(int iterations) {
            if (_renderHandle.HasValue) {
                Debug.LogWarning("Cannot start new render while previous one is still busy");
                return;
            }

            Debug.Log("Rendering...");

            var job = new TraceJob();
            job.Screen = _screen;
            job.Iterations = iterations;
            _watch = System.Diagnostics.Stopwatch.StartNew();

            var h = new JobHandle();
            h = job.Schedule(_screen.Length, 4, h);

            _renderHandle = h;
        }

        private void CompleteRender() {
            _renderHandle.Value.Complete();

            _watch.Stop();
            Debug.Log("Done! Time taken: " + _watch.ElapsedMilliseconds + "ms");
            _renderHandle = null;
        }

        private void OnGUI() {
            GUI.DrawTexture(new Rect(0f, 0f, _tex.width, _tex.height), _tex);
            // Todo: add some controls
        }

        [BurstCompile]
        private struct TraceJob : IJobParallelFor {
            [WriteOnly] public NativeArray<float3> Screen;
            [ReadOnly] public int Iterations;

            public void Execute(int i) {
                var xor = new XorshiftBurst(1234);

                float3 pixel = new float3(0);

                for (int r = 0; r < Iterations; r++) {
                    pixel = new float3(xor.NextFloat(), xor.NextFloat(), xor.NextFloat());
                }

                Screen[i] = pixel;
            }
        }
    }

    public struct CameraInfo {
        public int2 resolution;
        public float3 lowerLeft;
        public float3 hori;
        public float3 vert;

        public CameraInfo(int2 res, float3 ll, float3 ho, float3 ve) {
            resolution = res;
            lowerLeft = ll;
            hori = ho;
            vert = ve;
        }
    }

    public struct HitRecord {
        public float t;
        public float3 p;
        public float3 normal;
        public Material material;
    }

    public enum MaterialType { // If I make this : byte, switching on it makes burst cry
        Metal = 0,
    }
    public struct Material {
        public MaterialType Type;
        public float3 Albedo;
        public float Fuzz;

        public Material(MaterialType type, float3 albedo) {
            Type = type;
            Albedo = albedo;
            Fuzz = 0f;
        }
    }

    public struct Sphere {
        public float3 Center;
        public float Radius;
        public Material Material; // Todo: Don't store material here

        public Sphere(float3 center, float radius, Material material) {
            Center = center;
            Radius = radius;
            Material = material;
        }
    }

    public static class Tracer {
        public static bool Sphere(Sphere s, Ray3f r, float tMin, float tMax, out HitRecord hit) {
            // Todo: a bunch of 2s cancel each other out here, good algebra excercise
            hit = new HitRecord();

            float3 oc = r.origin - s.Center;
            float a = math.dot(r.direction, r.direction);
            float b = 2.0f * math.dot(oc, r.direction);
            float c = math.dot(oc, oc) - s.Radius * s.Radius;
            float discriminant = b * b - 4.0f * a * c;

            if (discriminant > 0f) {
                float t = (-b - math.sqrt(discriminant)) / (2.0f * a);
                if (t < tMax && t > tMin) {
                    hit.t = t;
                    hit.p = PointOnRay(r, t);
                    hit.normal = (hit.p - s.Center) / s.Radius;
                    return true;
                }

                t = (-b + math.sqrt(discriminant)) / (2.0f * a);
                if (t < tMax && t > tMin) {
                    hit.t = t;
                    hit.p = PointOnRay(r, t);
                    hit.normal = (hit.p - s.Center) / s.Radius;
                    return true;
                }
            }
            return false;
        }

        public static float3 PointOnRay(Ray3f r, float t) {
            return r.origin + r.direction * t;
        }

        public static float3 Trace(Ray3f ray, NativeArray<Sphere> spheres, ref XorshiftBurst xor, int depth, int maxDepth) {
            HitRecord hit;

            if (depth >= maxDepth) {
                hit = new HitRecord();
                return new float3(0);
            }

            const float tMin = 0f;
            const float tMax = 1000f;

            bool hitSomething = false;

            HitRecord closestHit = new HitRecord();
            closestHit.t = tMax;
            for (int i = 0; i < spheres.Length; i++) {
                if (Tracer.Sphere(spheres[i], ray, tMin, tMax, out hit)) {
                    if (hit.t < closestHit.t) {
                        hit.material = spheres[i].Material;
                        hitSomething = true;
                        closestHit = hit;
                    }
                }
            }
            hit = closestHit;

            float3 light = new float3(xor.NextFloat(), xor.NextFloat(), xor.NextFloat());

            if (hitSomething) {
                light = 0.5f + 0.5f * hit.normal;
            }

            if (hitSomething) {
                // We see a thing through another thing, find that other thing, see what it sees, it might be light, but might end void
                // Filter it through its material model

                //Ray3f subRay = Scatter(ray, hit, ref xor, fibs);
                var subRay = new Ray3f(hit.p, hit.normal);
                light = Trace(subRay, spheres, ref xor, depth + 1, maxDepth);
                light = BRDF(hit, light);
            } else {
                // We see sunlight, just send that back through the path traversed

                var normedDir = math.normalize(ray.direction);
                float t = 0.5f * (normedDir.y + 1f);
                light = (1f - t) * new float3(1f) + t * new float3(0.7f);
            }

            return light;
        }

        public static Ray3f Scatter(Ray3f ray, HitRecord hit, ref XorshiftBurst xor, NativeArray<float3> fibs) {
            const float eps = 0.0001f;

            // Todo: fuzz of const radius is addded to ray.Direction, which is of arbitrary length. 
            // Small rays will thus get scattered more than large ones. Right or wrong?
            float3 reflection = Reflect(ray.direction, hit.normal);
            float3 fuzz = fibs[xor.NextInt(0, fibs.Length - 1)] * hit.material.Fuzz;
            return new Ray3f(hit.p + hit.normal * eps, reflection + fuzz);
        }

        public static float3 Reflect(float3 v, float3 n) {
            return v - (2f * math.dot(v, n)) * n;
        }

        // Note: not passing by ref here causes huge errors
        public static float3 BRDF(HitRecord hit, float3 light) {
            return light * hit.material.Albedo;
        }

        public static float2 ToXY(int screenIdx, CameraInfo cam) {
            return new float2(
                (screenIdx % cam.resolution.x),
                (screenIdx / cam.resolution.x)
            );
        }

        public static Ray3f MakeRay(float2 screenPos, CameraInfo cam) {
            return new Ray3f(
                new float3(),
                cam.lowerLeft +
                cam.hori * screenPos.x +
                cam.vert * screenPos.y);
        }
    }
}

