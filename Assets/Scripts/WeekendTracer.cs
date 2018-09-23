using UnityEngine;
using Unity.Collections;
using Unity.Burst;
using Unity.Jobs;
using Unity.Mathematics;
using RamjetMath;

using System.Runtime.CompilerServices;

/* 
Todo:

Performance varies wildly for small variations in scene. Sometimes 70MRay/sec, sometimes 10. Egalize.

- Test Unity.Mathematics.Random
- Transformable camera object for ray generation
- Make it easier to configure render properties. num rays pp, resolution, etc.
- Investigate editor performance (slow on first render, fast on next)

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

public class WeekendTracer : MonoBehaviour {
    [SerializeField] private bool _drawDebugRays;
    [SerializeField] private string _saveFolder = "C:\\Users\\Martijn\\Desktop\\weekendtracer\\";

    private NativeArray<float3> _screen;
    private Scene _scene;
    private NativeArray<float3> _fibs;

    private ClearJob _clear;
    private TraceJob _trace;

    private Camera _camera = new Camera(50f, 21f/9f);

    private TraceJobQuality _debugQuality = new TraceJobQuality()
    {
        tMin = 0,
        tMax = 1000,
        MaxDepth = 4,
        RaysPerPixel = 2
    };

    private TraceJobQuality _fullQuality = new TraceJobQuality() {
        tMin = 0,
        tMax = 1000,
        MaxDepth = 32,
        RaysPerPixel = 32
    };

    private Color[] _colors;
    private Texture2D _tex;

    private void Awake() {
        int vertResolution = 512;
        int horiResolution = (int)math.round(vertResolution * _camera.aspect);
        _fullQuality.Resolution = new int2(horiResolution, vertResolution);
        _debugQuality.Resolution = _fullQuality.Resolution;
        
        Debug.Log("Resolution = " + _fullQuality.Resolution);

        _screen = new NativeArray<float3>(_fullQuality.Resolution.x * _fullQuality.Resolution.y, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        _clear = new ClearJob();
        _clear.Buffer = _screen;

        _scene = MakeScene();

        _fibs = new NativeArray<float3>(4096, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        GenerateFibonacciSphere(_fibs);

        _trace = new TraceJob();
        _trace.Screen = _screen;
        _trace.Fibs = _fibs;
        _trace.Scene = _scene;
        _trace.Camera = _camera;
        _trace.RayCounter = new NativeArray<ulong>(1, Allocator.Persistent, NativeArrayOptions.ClearMemory);

        _colors = new Color[_fullQuality.Resolution.x * _fullQuality.Resolution.y];
        _tex = new Texture2D(_fullQuality.Resolution.x, _fullQuality.Resolution.y, TextureFormat.ARGB32, false, true);
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

        UnityEngine.Random.InitState(1234);

        scene.Spheres = new NativeArray<Sphere>(7, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        scene.Spheres[0] = new Sphere(new float3(0, -100.5f, 1), 100f, new Material(MaterialType.Lambertian, new float3(0.8f, 0.8f, 0f)));
        scene.Spheres[1] = new Sphere(new float3(0, 0, 1), 0.5f, new Material(MaterialType.Lambertian, new float3(0.1f, 0.2f, 0.5f)));
        scene.Spheres[2] = new Sphere(new float3(1, 0,1), 0.5f, new Material(MaterialType.Metal, new float3(0.8f, 0.6f, 0.2f)));
        scene.Spheres[3] = new Sphere(new float3(-1, 0,1), 0.5f, new Material(MaterialType.Dielectric, new float3(1f, 1f, 1f)));
        scene.Spheres[4] = new Sphere(new float3(-1, 0, 1), -0.45f, new Material(MaterialType.Dielectric, new float3(1f, 1f, 1f)));
        scene.Spheres[5] = new Sphere(new float3(-1, 0, 1), 0.4f, new Material(MaterialType.Dielectric, new float3(1f, 1f, 1f)));
        scene.Spheres[6] = new Sphere(new float3(-1, 0, 1), -0.35f, new Material(MaterialType.Dielectric, new float3(1f, 1f, 1f)));

        // var floorMat = new Material(MaterialType.Lambertian, new float3(0.8f, 0.8f, 0f));
        scene.Planes = new NativeArray<Plane>(0, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        // scene.Planes[0] = new Plane(
        //     new float3(0f, -0.5f, 0f),
        //     new float3(0f, 1f, 0f),
        //     floorMat);

        // var diskMat = new Material(MaterialType.Metal, new float3(UnityEngine.Random.value, UnityEngine.Random.value, UnityEngine.Random.value));
        scene.Disks = new NativeArray<Disk>(0, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        // scene.Disks[0] = new Disk(
        //     new Plane(new float3(5f, 1f, 10f), math.normalize(new float3(0f, 1f, -1)), diskMat),
        //     5f,
        //     diskMat);

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
            ExportImage(_tex, _saveFolder);
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

        int i = _fullQuality.Resolution.x * (_fullQuality.Resolution.y / 4) + _fullQuality.Resolution.x / 4;
        var xor = new XorshiftBurst(i * 2543, i * 12269, i * 19037, i * 26699);
        xor.Next();
        xor.Next();
        xor.Next();
        xor.Next();
        xor.Next();
        xor.Next();

        var screenPos = ToXY(i, _fullQuality.Resolution);
        float3 pixel = new float3(0f);

        Gizmos.color = new Color(1f, 1f, 1f, 0.5f);
        for (int s = 0; s < _scene.Spheres.Length; s++) {
            Gizmos.DrawSphere(_scene.Spheres[s].Center, _scene.Spheres[s].Radius);
        }

        for (int r = 0; r < _trace.Quality.RaysPerPixel; r++) {
            Gizmos.color = Color.HSVToRGB(r / (float)_trace.Quality.RaysPerPixel, 0.7f, 0.5f);
            float2 jitter = new float2(xor.NextFloat(), xor.NextFloat()) * 300f;
            float2 p = (screenPos + jitter) / (float2)_fullQuality.Resolution;

            var ray = _camera.GetRay(p);

            float reflectProb = 1f;

            for (int t = 0; t < _trace.Quality.MaxDepth; t++) {
                const float tMin = 0f;
                const float tMax = 1000f;

                Gizmos.DrawSphere(ray.origin, 0.01f);

                HitRecord hit;
                bool hitSomething = HitTest.Scene(_scene, ray, tMin, tMax, out hit);
                if (hitSomething) {
                    Gizmos.color = new Color(reflectProb, reflectProb, reflectProb);
                    Gizmos.DrawLine(ray.origin, hit.point);
                    Ray3f subRay;
                    if (!Scatter(ray, hit, ref xor, _trace.Fibs, out subRay, out reflectProb)) {
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
        ToTexture2D(_screen, _colors, _tex, _fullQuality.Resolution);
        _renderHandle = null;
    }

    private static void ExportImage(Texture2D texture, string folder) {
        var bytes = texture.EncodeToJPG(100);
        System.IO.File.WriteAllBytes(
            System.IO.Path.Combine(folder, string.Format("render_{0}.png", System.DateTime.Now.ToFileTimeUtc())),
            bytes);
    }

    private void OnGUI() {
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), _tex, ScaleMode.ScaleToFit);
        // Todo: add some controls
    }

    [BurstCompile]
    private struct ClearJob : IJobParallelFor {
        public NativeArray<float3> Buffer;
        public void Execute(int i) {
            Buffer[i] = 0f;
        }
    }

    private struct TraceJobQuality {
        public int2 Resolution;
        public float tMin;
        public float tMax;
        public int RaysPerPixel;
        public int MaxDepth;
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
            /* Todo: test xorshift thoroughly. First few iterations can still be very correlated
               so I warm it up n times for now. */
            var xor = new XorshiftBurst(i * 2543, i * 12269, i * 19037, i * 26699);
            xor.Next();
            xor.Next();
            xor.Next();
            xor.Next();
            xor.Next();
            xor.Next();

            var screenPos = ToXY(i, Quality.Resolution);
            float3 pixel = new float3(0f);

            ushort rayCount = 0;

            for (int r = 0; r < Quality.RaysPerPixel; r++) {
                float2 jitter = new float2(xor.NextFloat(), xor.NextFloat());
                float2 p = (screenPos + jitter) / (float2)Quality.Resolution;
                var ray = Camera.GetRay(p);
                pixel += TraceRecursive(ray, Scene, ref xor, Fibs, 0, Quality.MaxDepth, ref rayCount);
            }

            Screen[i] = math.sqrt(pixel / (float)(Quality.RaysPerPixel));

            RayCounter[0] += rayCount; // Todo: atomics, or rather, fix the amount of rays per job run
        }
    }

    private static float3 TraceRecursive(Ray3f ray, Scene scene, ref XorshiftBurst xor, NativeArray<float3> fibs, int depth, int maxDepth, ref ushort rayCount) {
        HitRecord  hit;

        if (depth >= maxDepth) {
            hit = new HitRecord();
            return new float3(0);
        }

        const float tMin = 0f;
        const float tMax = 1000f;

        bool hitSomething = HitTest.Scene(scene, ray, tMin, tMax, out hit);
        ++rayCount;

        float3 light = new float3(0);

        if (hitSomething) {
            // We see a thing through another thing, find that other thing, see what it sees, it might be light, but might end void
            // Filter it through its material model

            float refr;
            Ray3f subRay;
            bool scattered = Scatter(ray, hit, ref xor, fibs, out subRay, out refr);
            if (scattered) {
                light = TraceRecursive(subRay, scene, ref xor, fibs, depth + 1, maxDepth, ref rayCount);
            }
            light = BRDF(hit, light);
        } else {
            // We see sunlight, just send that back through the path traversed

            float t = 0.5f * (ray.direction.y + 1f);
            light = (1f - t) * new float3(1f) + t * scene.LightColor;
        }

        return light;
    }

    private static bool Scatter(Ray3f ray, HitRecord hit, ref XorshiftBurst xor, NativeArray<float3> fibs, out Ray3f scattered, out float reflectProb) {
        const float eps = 0.0001f;

        const float refIdx = 1.5f;
        reflectProb = 1f;

        switch(hit.material.Type) {
            case MaterialType.Dielectric:
            {
                float3 outwardNormal;
                float nint;
                float3 reflected = math.reflect(ray.direction, hit.normal);
                float cosine;
                
                if (math.dot(ray.direction, hit.normal) > 0f) {
                    outwardNormal = -hit.normal;
                    nint = refIdx;
                    cosine = refIdx * math.dot(ray.direction, hit.normal);
                }
                else {
                    outwardNormal = hit.normal;
                    nint = 1f / refIdx;
                    cosine = -math.dot(ray.direction, hit.normal);
                }

                float3 refracted;
                if (Refract(ray.direction, outwardNormal, nint, out refracted)) {
                    reflectProb = Schlick(cosine, refIdx);
                }

                bool reflect = xor.NextFloat() < reflectProb;

                scattered = new Ray3f(
                    reflect ? hit.point + outwardNormal * eps : hit.point - outwardNormal * eps,
                    reflect ? reflected : refracted
                );
                return true;
            }
            case MaterialType.Metal:
            {
                // Todo: false if dot(reflected, normal) < 0
                float3 transmitted = math.reflect(ray.direction, hit.normal);
                transmitted += fibs[xor.NextInt(0, fibs.Length - 1)] * hit.material.Fuzz;
                transmitted = math.normalize(transmitted);
                scattered = new Ray3f(hit.point + hit.normal * eps, transmitted);
                if (math.dot(scattered.direction, hit.normal) > 0) {
                    return true;
                }
                return false;
            }
            case MaterialType.Lambertian:
            default:
            {
                float3 target = hit.normal + fibs[xor.NextInt(0, fibs.Length - 1)];
                float3 transmitted = math.normalize(target - hit.point);
                scattered = new Ray3f(hit.point + hit.normal * eps, transmitted);
                return true;
            }
        }
    }

    // [MethodImpl(MethodImplOptions.AggressiveInlining)]
    // private static float3 Reflect(float3 v, float3 n) {
    //     return v - (2f * math.dot(v, n)) * n;
    // }

    public static bool Refract(float3 v, float3 n, float nint, out float3 outRefracted) {
        float dt = math.dot(v, n);
        float discr = 1.0f - nint * nint * (1 - dt * dt);
        if (discr > 0) {
            outRefracted = nint * (v - n * dt) - n * math.sqrt(discr);
            outRefracted = math.normalize(outRefracted);
            return true;
        }
        outRefracted = new float3(0, 0, 0);
        return false;
    }

    // private static bool Refract(float3 I, float3 N, float ior, out float3 refracted) {
    //     I = math.normalize(I);
    //     N = math.normalize(N);
    //     float cosi = math.clamp(math.dot(I, N), -1f, 1f);
    //     float etai = 1f;
    //     float etat = ior;
    //     float3 n = N;
    //     if (cosi < 0) {
    //         cosi = -cosi;
    //     } else {
    //         float temp = etai;
    //         etai = etat;
    //         etat = temp;
    //         n = -n;
    //     }
    //     float eta = etai / etat;
    //     float k = 1f - eta * eta * (1f - cosi * cosi);
    //     if (k < 0f) {
    //         refracted = 0f;
    //         return false;
    //     } else {
    //         refracted = eta * I + (eta * cosi - math.sqrt(k)) * n;
    //         return true;
    //     }
    // }

    public static float Schlick(float cosine, float ri) {
        float r0 = (1f - ri) / (1f + ri);
        r0 = r0 * r0;
        return r0 + (1f - r0) * math.pow(1f - cosine, 5f);
    }

    private static float3 BRDF(HitRecord hit, float3 light) {
        switch (hit.material.Type) {
            case MaterialType.Dielectric:
                return light;
            case MaterialType.Metal:
                return light * hit.material.Albedo;
            case MaterialType.Lambertian:
            default:
                return light * hit.material.Albedo;
        }
    }

    private static float2 ToXY(int screenIdx, int2 resolution) {
        return new float2(
            (screenIdx % resolution.x),
            (screenIdx / resolution.x)
        );
    }

    private static void ToTexture2D(NativeArray<float3> screen, Color[] colors, Texture2D tex, int2 resolution) {
        for (int i = 0; i < screen.Length; i++) {
            var c = screen[i];
            colors[i] = new Color(c.x, c.y, c.z, 1f);
        }

        tex.SetPixels(0, 0, resolution.x, resolution.y, colors, 0);
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

    private struct Camera {
        private float3 origin;
        private float3 lowerLeft;
        private float3 hori;
        private float3 vert;

        public readonly float vfov;
        public readonly float aspect;

        public Camera(float vfov, float aspect) {
            this.vfov = vfov;
            this.aspect = aspect;

            float theta = vfov * Mathf.Deg2Rad;
            float halfHeight = math.tan(theta/2f);
            float halfWidth = aspect * halfHeight;
            lowerLeft = new float3(-halfWidth, -halfHeight, 1.0f);
            hori = new float3(2f * halfWidth, 0f, 0f);
            vert = new float3(0f, 2f * halfHeight, 0f);
            origin = new float3(0f);
        }

        public Ray3f GetRay(float2 uv) {
            return new Ray3f(
                origin,
                math.normalize(lowerLeft + uv.x * hori + uv.y * vert - origin));
        }
    }

    private struct HitRecord {
        public float t;
        public float3 point;
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

    private enum MaterialType { // If I make this : byte, switching on it makes burst cry
        Lambertian = 0,
        Metal = 1,
        Dielectric = 2
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
            HitRecord closestHit = new HitRecord();
            closestHit.t = tMax;

            // Note: this is the most naive brute force scene intersection you could ever do :P

            HitRecord hit;

            // Hit planes
            for (int i = 0; i < s.Planes.Length; i++) {
                if (HitTest.Plane(s.Planes[i], r, tMin, tMax, out hit)) {
                    if (hit.t < closestHit.t) {
                        hit.material = s.Planes[i].Material;
                        hitAnything = true;
                        closestHit = hit;
                    }
                }
            }

            // Hit disks
            for (int i = 0; i < s.Disks.Length; i++) {
                if (HitTest.Disk(s.Disks[i], r, tMin, tMax, out hit)) {
                    if (hit.t < closestHit.t) {
                        hit.material = s.Disks[i].Material;
                        hitAnything = true;
                        closestHit = hit;
                    }
                }
            }

            // // Hit spheres
            for (int i = 0; i < s.Spheres.Length; i++) {
                if (HitTest.Sphere(s.Spheres[i], r, tMin, tMax, out hit)) {
                    if (hit.t < closestHit.t) {
                        hit.material = s.Spheres[i].Material;
                        hitAnything = true;
                        closestHit = hit;
                    }
                }
            }

            finalHit = closestHit;

            return hitAnything;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Plane(Plane p, Ray3f r, float tMin, float tMax, out HitRecord hit) {
            hit = new HitRecord();

            const float eps = 0.0001f;
            if (math.abs(math.dot(r.direction, p.Normal)) > eps) {
                float t = math.dot((p.Center - r.origin), p.Normal) / math.dot(r.direction, p.Normal);
                if (t > eps) {
                    hit.t = t;
                    hit.point = PointOnRay(r, t);
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
                var offset = (hit.point - d.Plane.Center);
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
                    hit.point = PointOnRay(r, t);
                    hit.normal = (hit.point - s.Center) / s.Radius;
                    return true;
                }

                t = (-b + math.sqrt(discriminant)) / (2.0f * a);
                if (t < tMax && t > tMin) {
                    hit.t = t;
                    hit.point = PointOnRay(r, t);
                    hit.normal = (hit.point - s.Center) / s.Radius;
                    return true;
                }
            }
            return false;

            // float3 oc = r.origin - s.Center;
            // float b = math.dot(oc, r.direction);
            // float c = math.dot(oc, oc) - s.Radius * s.Radius;
            // float discr = b * b - c;
            // if (discr > 0) {
            //     float discrSq = math.sqrt(discr);

            //     float t = (-b - discrSq);
            //     if (t < tMax && t > tMin) {
            //         hit.point = PointOnRay(r, t);
            //         hit.normal = (hit.point - s.Center) / s.Radius;
            //         hit.t = t;
            //         return true;
            //     }
            //     t = (-b + discrSq);
            //     if (t < tMax && t > tMin) {
            //         hit.point = PointOnRay(r, t);
            //         hit.normal = (hit.point - s.Center) / s.Radius;
            //         hit.t = t;
            //         return true;
            //     }
            // }
            // return false;
        }
    }
}