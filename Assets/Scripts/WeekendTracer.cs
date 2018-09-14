using UnityEngine;
using Unity.Collections;
using Unity.Burst;
using Unity.Jobs;
using Unity.Mathematics;
using RamjetMath;

using System.Runtime.CompilerServices;

/* 
Todo:
- Experiment with different ways to generate rays
- This way getting from a Burst-style screenbuffer to a cpu-texture to a gpu render is dumb

After disk intersection, remove object-oriented style. Find a better way to have multiple tracable types.
First task is to separate the object data structures from the functions that trace them


Now that we've done this we can compare some aspects of this style of tracing to distance fields
- Without any culling or sorting, each ray here has to be prepared to intersect with all possible
objects in the scene. So each step you loop over all objects. This gets expensive crazy fast, it
goes up as n objects and r bounces happen.
- With a single distance field to sample from that encodes multiple parts of the scene, for many
objects and many rays, it starts winning.

So, to optimize there's a couple of things we should try to do:
- Cull objects, or otherwise limit the amount of objects a ray has to computationally interact with
- Limit super-sampling for parts of the image that need it.
- When generating random numbers, think carefully how many bits of entropy you need
- Can you at all use cached irradiance? When hitting a surface, if there has been prior local light information, use it?


For multiple object types and lists per scene, we can make a type system that matches primitive type to
trace function.
*/

public class WeekendTracer : MonoBehaviour {
    private NativeArray<float3> _screen;
    private Scene _scene;

    private ClearJob _clear;
    private TraceJob _trace;

    private static readonly CameraInfo Cam = new CameraInfo(
        new int2(1024, 512),
        new float3(-2.0f, -1.0f, 1.0f),
        new float3(4f, 0f, 0f),
        new float3(0f, 2f, 0f));

    private Color[] _colors;
    private Texture2D _tex;

    private void Awake() {
        _screen = new NativeArray<float3>(Cam.resolution.x * Cam.resolution.y, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        _clear = new ClearJob();
        _clear.Buffer = _screen;

        _scene = MakeScene();

        var fibs = new NativeArray<float3>(4096, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        GenerateFibonacciSphere(fibs);

        _trace = new TraceJob();
        _trace.Screen = _screen;
        _trace.Cam = Cam;
        _trace.Fibs = fibs;
        _trace.Scene = _scene;

        _colors = new Color[Cam.resolution.x * Cam.resolution.y];
        _tex = new Texture2D(Cam.resolution.x, Cam.resolution.y, TextureFormat.ARGB32, false, true);
        _tex.filterMode = FilterMode.Point;
    }

    private static Scene MakeScene() {
        var scene = new Scene();

        scene.LightDir = math.normalize(new float3(-0.5f, -1, 0));
        scene.LightColor = new float3(0.5f, 0.7f, 1f);

        Random.InitState(1234);

        scene.Spheres = new NativeArray<Sphere>(16, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        System.Random rand = new System.Random(13345);
        for (int i = 0; i < scene.Spheres.Length; i++) {
            scene.Spheres[i] = new Sphere(new float3(
                -2f + 4f * Random.value,
                 -1f + 2f * Random.value,
                 1.5f + 5f * Random.value),
                0.1f + Random.value * 0.9f);
        }

        scene.Planes = new NativeArray<Plane>(1, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        scene.Planes[0] = new Plane(new float3(0f, -1, 0f), new float3(0f, 1f, 0f));

        scene.Disks = new NativeArray<Disk>(1, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        scene.Disks[0] = new Disk(new Plane(new float3(5f, 1f, 5f), math.normalize(new float3(0f, 1f, -1))), 5f);

        return scene;
    }

    private void Start() {
        StartRender();
    }

    private void Update() {
        if (_renderHandle.HasValue && _renderHandle.Value.IsCompleted) {
            _renderHandle.Value.Complete();
            CompleteRender();
        } else {
            if (Input.GetKeyDown(KeyCode.Space)) {
                StartRender();
            }
        }
    }

    private void OnDestroy() {
        _screen.Dispose();
        _scene.Dispose();
        _trace.Fibs.Dispose();
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

        _renderHandle = new JobHandle();
        _renderHandle = _clear.Schedule(_screen.Length, 32, _renderHandle.Value);
        _renderHandle = _trace.Schedule(_screen.Length, 32, _renderHandle.Value);
    }

    private void CompleteRender() {
        _watch.Stop();
        Debug.Log("Done! Time taken: " + _watch.ElapsedMilliseconds + "ms");
        ToTexture2D(_screen, _colors, _tex);
        _renderHandle = null;
    }

    private void OnGUI() {
        GUI.DrawTexture(new Rect(0f, 0f, _tex.width, _tex.height), _tex);
        // Todo: add some controls
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
        public CameraInfo Cam;

        const float tMin = 0f;
        const float tMax = 100f;
        const int raysPP = 512;
        const int recursionsPR = 8;
        const float eps = 0.0001f;

        public void Execute(int i) {
            // Todo: test xorshift thoroughly
            XorshiftBurst xor = new XorshiftBurst(i * 3215, i * 502, i * 1090, i * 8513, Allocator.TempJob);

            var screenPos = ToXY(i, Cam);
            float3 pixel = new float3(0f);

            for (int r = 0; r < raysPP; r++) {
                float2 jitter = new float2(xor.NextFloat(), xor.NextFloat());
                float2 p = (screenPos + jitter) / (float2)Cam.resolution;
                var ray = MakeRay(p, Cam);
                pixel += Trace(ray, xor, 0, recursionsPR);
            }

            Screen[i] = pixel / (float)(raysPP);

            xor.Dispose();
        }

        private float3 Trace(Ray3f ray, XorshiftBurst xor, int depth, int maxDepth) {
            float3 col = new float3(0, 0, 0);

            if (depth >= maxDepth) {
                return col;
            }

            HitRecord hit;
            bool hitAnything = HitTest.Scene(Scene, ray, tMin, tMax, out hit);

            if (hitAnything) {
                ray.origin = hit.p + hit.normal * eps;
                ray.direction = hit.normal + Fibs[xor.NextInt(0, Fibs.Length - 1)];
                col = Trace(ray, xor, depth++, maxDepth);
            } else {
                var normedDir = math.normalize(ray.direction);
                float t = 0.5f * (normedDir.y + 1f);
                col = (1f - t) * new float3(1f) + t * Scene.LightColor;
            }

            col = col * GetAlbedo(hit.material);

            return col;
        }
    }

    /* 
    Note: At first I thought you can help this process along by including dot(normal, light), dot(normal, view)
    but it seems easier to just let light filter by albedo and do everything else by more rays and more bouncing
    */
    private static float3 GetAlbedo(int material) {
        float3 albedo;
        switch (material) {
            case 0:
                albedo = new float3(0.4f, 0.2f, 0.7f);
                break;
            case 1:
                albedo = new float3(0.2f, 1f, 0.55f);
                break;
            case 2:
                albedo = new float3(0.6f, 0.1f, 0.1f);
                break;
            default:
                albedo = new float3(.3f);
                break;
        }
        return albedo;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float2 ToXY(int screenIdx, CameraInfo cam) {
        return new float2(
            (screenIdx % cam.resolution.x),
            (screenIdx / cam.resolution.x)
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float3 PointOnRay(Ray3f r, float t) {
        return r.origin + r.direction * t;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Ray3f MakeRay(float2 screenPos, CameraInfo cam) {
        return new Ray3f(
            new float3(),
            cam.lowerLeft +
            cam.hori * screenPos.x +
            cam.vert * screenPos.y);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float InterleavedGradientNoise(float2 xy) {
        return math.frac(52.9829189f
                    * math.frac(xy.x * 0.06711056f
                            + xy.y * 0.00583715f));
    }

    private static void ToTexture2D(NativeArray<float3> screen, Color[] colors, Texture2D tex) {
        for (int i = 0; i < screen.Length; i++) {
            var c = screen[i];
            colors[i] = new Color(c.x, c.y, c.z, 1f);
        }

        tex.SetPixels(0, 0, Cam.resolution.x, Cam.resolution.y, colors, 0);
        tex.Apply();
    }

    private static void GenerateFibonacciSphere(NativeArray<float3> output) {
        float n = output.Length / 2.0f;
        float pi = Mathf.PI;
        float dphi = pi * (3.0f - math.sqrt(5.0f));
        float phi = 0f;
        float dz = 1.0f / n;
        float z = 1.0f - dz / 2.0f;
        int[] indices = new int[output.Length];

        for (int j = 0; j < n; j++) {
            float zj = z;
            float thetaj = math.acos(zj);
            float phij = phi % (2f * pi);
            z = z - dz;
            phi = phi + dphi;

            // spherical -> cartesian, with r = 1
            output[j] = new Vector3((float)(math.cos(phij) * math.sin(thetaj)),
                                    (float)(zj),
                                    (float)(math.sin(thetaj) * math.sin(phij)));
            indices[j] = j;
        }

        // The code above only covers a hemisphere, this mirrors it into a sphere.
        for (int i = 0; i < n; i++) {
            var vz = output[i];
            vz.y *= -1;
            output[output.Length - i - 1] = vz;
            indices[i + output.Length / 2] = i + output.Length / 2;
        }
    }

    private struct CameraInfo {
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

    private struct HitRecord {
        public float t;
        public float3 p;
        public float3 normal;
        public int material;
    }

    private struct Scene : System.IDisposable {
        public NativeArray<Sphere> Spheres;
        public NativeArray<Plane> Planes;
        public NativeArray<Disk> Disks;
        public float3 LightDir;
        public float3 LightColor;

        public void Dispose() {
            Spheres.Dispose();
            Planes.Dispose();
            Disks.Dispose();
        }
    }

    private struct Sphere {
        public float3 Center;
        public float Radius;

        public Sphere(float3 center, float radius) {
            Center = center;
            Radius = radius;
        }
    }

    private struct Plane {
        public float3 Center;
        public float3 Normal;

        public Plane(float3 center, float3 normal) {
            Center = center;
            Normal = normal;
        }
    }

    private struct Disk {
        public Plane Plane;
        public float Radius;

        public Disk(Plane plane, float radius) {
            Plane = plane;
            Radius = radius;
        }
    }

    private static class HitTest {
        public static bool Scene(Scene s, Ray3f r, float tMin, float tMax, out HitRecord hit) {
            bool hitAnything = false;
            HitRecord closestHit = new HitRecord();
            float closestT = tMax;

            // Note: this is the most naive brute force scene intersection you could ever do :P

            // Hit planes
            for (int i = 0; i < s.Planes.Length; i++) {
                if (HitTest.Plane(s.Planes[i], r, tMin, tMax, out hit)) {
                    if (hit.t < closestT) {
                        hit.material = 0;
                        hitAnything = true;
                        closestHit = hit;
                        closestT = hit.t;
                    }
                }
            }

            // Hit disks
            for (int i = 0; i < s.Disks.Length; i++) {
                if (HitTest.Disk(s.Disks[i], r, tMin, tMax, out hit)) {
                    if (hit.t < closestT) {
                        hit.material = 1;
                        hitAnything = true;
                        closestHit = hit;
                        closestT = hit.t;
                    }
                }
            }

            // Hit spheres
            for (int i = 0; i < s.Spheres.Length; i++) {
                if (HitTest.Sphere(s.Spheres[i], r, tMin, tMax, out hit)) {
                    if (hit.t < closestT) {
                        hit.material = 2;
                        hitAnything = true;
                        closestHit = hit;
                        closestT = hit.t;
                    }
                }
            }

            hit = closestHit;
            return hitAnything;
        }

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Plane(Plane p, Ray3f r, float tMin, float tMax, out HitRecord hit) {
            hit = new HitRecord();

            const float eps = 0.0001f;
            if (math.abs(math.dot(r.direction, p.Normal)) > eps) {
                float t = math.dot((p.Center - r.origin), p.Normal) / math.dot(r.direction, p.Normal);
                if (t > eps) {
                    hit.t = t;
                    hit.p = PointOnRay(r, hit.t);
                    hit.normal = p.Normal;
                    return true;
                }
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Disk(Disk d, Ray3f r, float tMin, float tMax, out HitRecord hit) {
            hit = new HitRecord();

            if (Plane(d.Plane, r, tMin, tMax, out hit)) {
                var offset = (hit.p - d.Plane.Center);
                if (math.dot(offset, offset) <= d.Radius * d.Radius) {
                    return true;
                }
            }

            return false;
        }
    }
}