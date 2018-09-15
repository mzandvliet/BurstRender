using UnityEngine;
using Unity.Collections;
using Unity.Burst;
using Unity.Jobs;
using Unity.Mathematics;
using RamjetMath;

using System.Runtime.CompilerServices;

/* 
Todo:

- Material configurable per object. Diffuse and metal.
- Make it easier to configure render properties. num rays pp, resolution, etc.

- Fix "Internal: JobTempAlloc has allocations that are more than 4 frames old - this is not allowed and likely a leak"
- Find a nicer way to stop jobs in progress in case you want to abort a render. It's messes up the editor now.
- Fix albedo colors messing with light it shouldn't. Ray flying up into sky should just return sky color.
- Show incremental results
- Experiment with different ways to generate rays
- This way getting from a Burst-style screenbuffer to a cpu-texture to a gpu render is dumb

- Ray termination time is wildly divergent. You need to make that constant-ish. There are multiple ways.

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
- Render iteratively, adaptively

For multiple object types and lists per scene, we can make a type system that matches primitive type to
trace function.
*/

public class WeekendTracer : MonoBehaviour {
    private NativeArray<float3> _screen;
    private Scene _scene;
    private NativeArray<float3> _fibs;

    private ClearJob _clear;
    private TraceJob _trace;

    private CameraInfo _camInfo = new CameraInfo(
        new int2(1024, 512) * 2,
        new float3(-2.0f, -1.0f, 1.0f),
        new float3(4f, 0f, 0f),
        new float3(0f, 2f, 0f));

    private Color[] _colors;
    private Texture2D _tex;

    private void Awake() {
        _screen = new NativeArray<float3>(_camInfo.resolution.x * _camInfo.resolution.y, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        _clear = new ClearJob();
        _clear.Buffer = _screen;

        _scene = MakeScene();

        _fibs = new NativeArray<float3>(4096, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        GenerateFibonacciSphere(_fibs);

        _trace = new TraceJob();
        _trace.Screen = _screen;
        _trace.Fibs = _fibs;
        _trace.Scene = _scene;
        _trace.Cam = _camInfo;

        _colors = new Color[_camInfo.resolution.x * _camInfo.resolution.y];
        _tex = new Texture2D(_camInfo.resolution.x, _camInfo.resolution.y, TextureFormat.ARGB32, false, true);
        _tex.filterMode = FilterMode.Point;
    }

    private void OnDestroy() {
        _screen.Dispose();
        _scene.Dispose();
        _fibs.Dispose();
    }

    private static Scene MakeScene() {
        var scene = new Scene();

        scene.LightDir = math.normalize(new float3(-2f, -1, -0.33f));
        scene.LightColor = new float3(0.5f, 0.7f, 1f);

        Random.InitState(1234);

        scene.Spheres = new NativeArray<Sphere>(16, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        System.Random rand = new System.Random(13249);
        for (int i = 0; i < scene.Spheres.Length; i++) {
            var pos = new float3(
                -3f + 6f * Random.value,
                 -1f + 2f * Random.value,
                 1.5f + 5f * Random.value);
            var rad = 0.1f + Random.value * 0.9f;
            var mat = new Material(MaterialType.Metal, new float3(0.5f) + 0.5f * new float3(Random.value, Random.value, Random.value));
            mat.Fuzz = math.pow(Random.value * 0.6f, 2f);
            scene.Spheres[i] = new Sphere(pos, rad, mat);
        }

        scene.Planes = new NativeArray<Plane>(1, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        scene.Planes[0] = new Plane(
            new float3(0f, -1, 0f),
            new float3(0f, 1f, 0f),
            new Material(MaterialType.Diffuse, new float3(Random.value, Random.value, Random.value)));

        // Todo: Putting material in shape, and using recursive shapes (disk = planeXcircle) results in redundant material information
        var diskMat = new Material(MaterialType.Metal, new float3(Random.value, Random.value, Random.value));
        scene.Disks = new NativeArray<Disk>(1, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        scene.Disks[0] = new Disk(
            new Plane(new float3(5f, 1f, 5f), math.normalize(new float3(0f, 1f, -1)), diskMat),
            5f,
            diskMat);

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
        _renderHandle = _clear.Schedule(_screen.Length, 4, _renderHandle.Value);
        _renderHandle = _trace.Schedule(_screen.Length, 4, _renderHandle.Value);
    }

    private void CompleteRender() {
        _watch.Stop();
        Debug.Log("Done! Time taken: " + _watch.ElapsedMilliseconds + "ms");
        ToTexture2D(_screen, _colors, _tex, _camInfo);
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
        const int raysPP = 1024;
        const int recursionsPR = 32;
        

        public void Execute(int i) {
            // Todo: test xorshift thoroughly. First few iterations can still be very correlated
            var xor = new XorshiftBurst(i * 3215, i * 502, i * 1090, i * 8513);
            xor.Next();
            xor.Next();
            xor.Next();
            xor.Next();

            var screenPos = ToXY(i, Cam);
            float3 pixel = new float3(0f);

            for (int r = 0; r < raysPP; r++) {
                float2 jitter = new float2(xor.NextFloat(), xor.NextFloat());
                float2 p = (screenPos + jitter) / (float2)Cam.resolution;
                var ray = MakeRay(p, Cam);
                pixel += Trace(ref ray, ref Scene, ref xor, Fibs, 0, recursionsPR);
            }

            Screen[i] = pixel / (float)(raysPP);
        }
    }

    private static float3 Trace(ref Ray3f ray, ref Scene scene, ref XorshiftBurst xor, NativeArray<float3> fibs, int depth, int maxDepth) {
        HitRecord  hit;

        if (depth >= maxDepth) {
            hit = new HitRecord();
            return new float3(0);
        }

        const float tMin = 0f;
        const float tMax = 1000f;
        const float eps = 0.0001f;

        bool hitSomething = HitTest.Scene(scene, ray, tMin, tMax, out hit);

        float3 light;

        if (hitSomething) {
            // We see a thing through another thing, find that other thing, see what it sees, it might be light, but might end void

            Ray3f subRay;
            if (hit.material.Type == MaterialType.Metal) {
                float3 reflectScatter = fibs[xor.NextInt(0, fibs.Length - 1)] * hit.material.Fuzz;
                subRay = new Ray3f(hit.p + hit.normal * eps, Reflect(ray.direction, hit.normal) + reflectScatter);
            } else {
                subRay = new Ray3f(hit.p + hit.normal * eps, hit.normal + fibs[xor.NextInt(0, fibs.Length - 1)]);
            }
            light = Trace(ref subRay, ref scene, ref xor, fibs, depth++, maxDepth);
            light = light * hit.material.Albedo;
        } else {
            // We see sunlight
            var normedDir = math.normalize(ray.direction);
            float t = 0.5f * (normedDir.y + 1f);
            light = (1f - t) * new float3(1f) + t * scene.LightColor;
        }

        // Todo: When we see, we could return terminate=true;

        return light;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float2 ToXY(int screenIdx, CameraInfo cam) {
        return new float2(
            (screenIdx % cam.resolution.x),
            (screenIdx / cam.resolution.x)
        );
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
    private static float3 Reflect(float3 v, float3 n) {
        return v - (2f * math.dot(v, n)) * n;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float InterleavedGradientNoise(float2 xy) {
        return math.frac(52.9829189f
                    * math.frac(xy.x * 0.06711056f
                            + xy.y * 0.00583715f));
    }

    private static void ToTexture2D(NativeArray<float3> screen, Color[] colors, Texture2D tex, CameraInfo cam) {
        for (int i = 0; i < screen.Length; i++) {
            var c = screen[i];
            colors[i] = new Color(c.x, c.y, c.z, 1f);
        }

        tex.SetPixels(0, 0, cam.resolution.x, cam.resolution.y, colors, 0);
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
        public Material material;
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

    private enum MaterialType : byte {
        Diffuse = 0,
        Metal = 1
    }
    private struct Material {
        public MaterialType Type;
        public float3 Albedo;
        public float Fuzz;

        public Material(MaterialType type, float3 albedo) {
            Type = type;
            Albedo = albedo;
            Fuzz = 0f;
        }
    }

    private struct Sphere {
        public float3 Center;
        public float Radius;
        public Material Material; // Todo: Don't store material here

        public Sphere(float3 center, float radius, Material material) {
            Center = center;
            Radius = radius;
            Material = material;
        }
    }

    private struct Plane {
        public float3 Center;
        public float3 Normal;
        public Material Material; // Todo: Don't store material here

        public Plane(float3 center, float3 normal, Material material) {
            Center = center;
            Normal = normal;
            Material = material;
        }
    }

    private struct Disk {
        public Plane Plane;
        public float Radius;
        public Material Material; // Todo: Don't store material here

        public Disk(Plane plane, float radius, Material material) {
            Plane = plane;
            Radius = radius;
            Material = material;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float3 PointOnRay(Ray3f r, float t) {
        return r.origin + r.direction * t;
    }

    private static class HitTest {
        public static bool Scene(Scene s, Ray3f r, float tMin, float tMax, out HitRecord finalHit) {
            bool hitAnything = false;
            float closestT = tMax;
            HitRecord closestHit = new HitRecord();

            // Note: this is the most naive brute force scene intersection you could ever do :P

            HitRecord hit;

            // Hit planes
            for (int i = 0; i < s.Planes.Length; i++) {
                if (HitTest.Plane(s.Planes[i], r, tMin, tMax, out hit)) {
                    if (hit.t < closestT) {
                        hit.material = s.Planes[i].Material;
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
                        hit.material = s.Disks[i].Material;
                        hitAnything = true;
                        closestHit = hit;
                        closestT = hit.t;
                    }
                }
            }

            // // Hit spheres
            for (int i = 0; i < s.Spheres.Length; i++) {
                if (HitTest.Sphere(s.Spheres[i], r, tMin, tMax, out hit)) {
                    if (hit.t < closestT) {
                        hit.material = s.Spheres[i].Material;
                        hitAnything = true;
                        closestHit = hit;
                        closestT = hit.t;
                    }
                }
            }

            finalHit = closestHit;

            return hitAnything;
        }

        // [MethodImpl(MethodImplOptions.AggressiveInlining)]
        // public static bool Test(ref Ray3f r, out HitRecord hit) {
        //     hit = new HitRecord();
        //     return false;
        // }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Plane(Plane p, Ray3f r, float tMin, float tMax, out HitRecord hit) {
            hit = new HitRecord();

            const float eps = 0.0001f;
            if (math.abs(math.dot(r.direction, p.Normal)) > eps) {
                float t = math.dot((p.Center - r.origin), p.Normal) / math.dot(r.direction, p.Normal);
                if (t > eps) {
                    hit.t = t;
                    hit.p = PointOnRay(r, t);
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
    }
}