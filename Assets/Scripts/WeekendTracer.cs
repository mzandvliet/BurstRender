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
*/

public class WeekendTracer : MonoBehaviour {
    private NativeArray<float3> _screen;

    private NativeArray<Sphere> _spheres;

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
        
        _spheres = new NativeArray<Sphere>(16, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        System.Random rand = new System.Random(1234);
        for (int i = 0; i < _spheres.Length; i++) {
            _spheres[i] = new Sphere(new float3(-5f + 10f * Random.value, -5f + 10f * Random.value, 3f + Random.value * 10f), 0.1f + Random.value * 0.9f);
        }

        Plane plane = new Plane(new float3(0,-10,0), new float3(0,1,0));

        _trace = new TraceJob();
        _trace.Screen = _screen;
        _trace.Cam = Cam;
        _trace.Spheres = _spheres;
        _trace.Plane = plane;

        _colors = new Color[Cam.resolution.x * Cam.resolution.y];
        _tex = new Texture2D(Cam.resolution.x, Cam.resolution.y, TextureFormat.ARGB32, false, true);
        _tex.filterMode = FilterMode.Point;
    }

    private void Start() {
        Render();
    }

    private void Update() {
        if (Input.GetKeyDown(KeyCode.Space)) {
            Render();
        }
    }

    private void OnDestroy() {
        _screen.Dispose();
        _spheres.Dispose();
    }

    private void Render() {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var h = new JobHandle();
        h = _clear.Schedule(_screen.Length, 64, h);
        h = _trace.Schedule(_screen.Length, 64, h);
        h.Complete();

        sw.Stop();
        Debug.Log("Elapsed milis: " + sw.ElapsedMilliseconds);

        ToTexture2D(_screen, _colors, _tex);
    }

    private void OnGUI() {
        GUI.DrawTexture(new Rect(0f, 0f, _tex.width, _tex.height), _tex);
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
        [ReadOnly] public NativeArray<Sphere> Spheres;
        [ReadOnly] public Plane Plane;
        public CameraInfo Cam;

        public void Execute(int i) {
            const float tMin = 0f;
            const float tMax = 100f;
            const int raysPP = 8;

            var screenPos = ToXY(i, Cam);

            float3 c = new float3(0f);
            
            for (int r = 0; r < raysPP; r++) {
                float2 jitter = InterleavedGradientNoise(screenPos + new float2(r, r));
                float2 p = (screenPos + jitter) / (float2)Cam.resolution;
                var ray = MakeRay(p, Cam);

                bool hitAnything = false;
                HitRecord closestHit = new HitRecord();
                float closestT = tMax;

                // Hit plane
                HitRecord hit;
                if (Plane.Hit(ray, tMin, tMax, out hit)) {
                    if (hit.t < closestT) {
                        hitAnything = true;
                        closestHit = hit;
                        closestT = hit.t;
                    }
                }

                // Hit sphere list
                int closestSphereIdx = -1;
                for (int s = 0; s < Spheres.Length; s++) {
                    if (Spheres[s].Hit(ray, tMin, tMax, out hit)) {
                        if (hit.t < closestT) {
                            hitAnything = true;
                            closestSphereIdx = s;
                            closestHit = hit;
                            closestT = hit.t;
                        }
                    }
                }

                if (hitAnything) {
                    float3 matcol;
                    if (closestHit.material == 0) {
                        matcol = new float3(1f, 0f, 0.1f);
                    } else {
                        matcol = new float3(0f, 0f, 1f);
                    }
                    c += matcol * math.dot(closestHit.normal, new float3(0, 1, 0));
                } else {
                    c += new float3(0.8f, 0.9f, 1f);
                }
            }

            Screen[i] = c / (float)raysPP;
        }
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

    private interface IHittable {
        bool Hit(Ray3f r, float tMin, float tMax, out HitRecord hit);
    }

    private struct Sphere : IHittable {
        public float3 Center;
        public float Radius;

        public Sphere(float3 center, float radius) {
            Center = center;
            Radius = radius;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Hit(Ray3f r, float tMin, float tMax, out HitRecord hit) {
            // Todo: a bunch of 2s cancel each other out here, good algebra excercise

            hit = new HitRecord();
            hit.material = 0;

            float3 oc = r.origin - Center;
            float a = math.dot(r.direction, r.direction);
            float b = 2.0f * math.dot(oc, r.direction);
            float c = math.dot(oc, oc) - Radius * Radius;
            float discriminant = b * b - 4.0f * a * c;

            if (discriminant > 0f) {
                float t = (-b - math.sqrt(discriminant)) / (2.0f * a);
                if (t < tMax && t > tMin) {
                    hit.t = t;
                    hit.p = PointOnRay(r, t);
                    hit.normal = (hit.p - Center) / Radius;
                    return true;
                }

                t = (-b + math.sqrt(discriminant)) / (2.0f * a);
                if (t < tMax && t > tMin) {
                    hit.t = t;
                    hit.p = PointOnRay(r, t);
                    hit.normal = (hit.p - Center) / Radius;
                    return true;
                }
            }
            return false;
        }
    }

    private struct Plane : IHittable {
        public float3 Center;
        public float3 Normal;

        public Plane(float3 center, float3 normal) {
            Center = center;
            Normal = normal;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Hit(Ray3f r, float tMin, float tMax, out HitRecord hit) {
            hit = new HitRecord();
            hit.material = 1;

            const float eps = 0.0001f;

            if (math.abs(math.dot(r.direction, Normal)) > eps) {
                float t = math.dot((Center - r.origin), Normal) / math.dot(r.direction, Normal);
                if (t > eps) {
                    hit.t = t;
                    hit.p = PointOnRay(r, hit.t);
                    hit.normal = Normal;
                    return true;
                }
            }

            return false;
        }
    }
}