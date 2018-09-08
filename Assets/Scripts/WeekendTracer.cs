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

    private JobHandle _renderHandle;

    private static readonly CameraInfo Cam = new CameraInfo(
        new int2(1024, 512),
        new float3(-2.0f, -1.0f, 1.0f),
        new float3(4f, 0f, 0f),
        new float3(0f, 2f, 0f));

    private Color[] _colors;
    private Texture2D _tex;


    private void Awake() {
        _screen = new NativeArray<float3>(Cam.resolution.x * Cam.resolution.y, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        
        _spheres = new NativeArray<Sphere>(16, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        System.Random rand = new System.Random(1234);
        for (int i = 0; i < _spheres.Length; i++) {
            _spheres[i] = new Sphere(new float3(-5f + 10f * Random.value, -5f + 10f * Random.value, 3f + Random.value * 10f), 0.1f + Random.value * 0.9f);
            //_spheres[i] = new Sphere(new float3(-1f * 8 + i * 1f,0f, 2f), 1f);
        }

        _clear = new ClearJob();
        _clear.Buffer = _screen;

        _trace = new TraceJob();
        _trace.Screen = _screen;
        _trace.Cam = Cam;
        _trace.Spheres = _spheres;

        _colors = new Color[Cam.resolution.x * Cam.resolution.y];
        _tex = new Texture2D(Cam.resolution.x, Cam.resolution.y, TextureFormat.ARGB32, false, true);
        _tex.filterMode = FilterMode.Point;
    }

    private void OnDestroy() {
        _screen.Dispose();
        _spheres.Dispose();
    }

    private void Update() {
        var h = new JobHandle();
        h = _clear.Schedule(_screen.Length, 64, h);
        h = _trace.Schedule(_screen.Length, 64, h);
        _renderHandle = h;

        _renderHandle.Complete();
    }

    private void LateUpdate() {
        ToTexture2D(_screen, _colors, _tex); // Lol 7ms
    }

    private void OnGUI() {
        GUI.DrawTexture(new Rect(0f, 0f, _tex.width * 1.5f, _tex.height * 1.5f), _tex);
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
        public CameraInfo Cam;

        public void Execute(int i) {
            // Create a camera ray
            // sample distance to sphere at origin

            var screenPos = ToXY(i, Cam) / (float2)Cam.resolution;
            var r = MakeRay(screenPos, Cam);

            const float tMin = 0f;
            const float tMax = 100f;
            
            HitRecord closestHit = new HitRecord();
            int closestIdx = -1;
            float closestT = tMax;
            for (int s = 0; s < Spheres.Length; s++) {
                HitRecord hit;
                if (Spheres[s].Hit(r, tMin, tMax, out hit)) {
                    if (hit.t < closestT) {
                        closestIdx = s;
                        closestHit = hit;
                        closestT = hit.t;
                    }
                }    
            }

            if(closestIdx > -1) {
                // Screen[i] = new float3(1f, 0f, 0.1f) * math.dot(closestHit.normal, new float3(0, 1, 0));
                Screen[i] = 0.5f + 0.5f * closestHit.normal;

            } else {
                Screen[i] = new float3(0.8f, 0.9f, 1f);
            }
            
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int2 ToXY(int screenIdx, CameraInfo cam) {
        return new int2(
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
            cam.lowerLeft + cam.hori * screenPos.x + cam.vert * screenPos.y);
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

    /* Here's the deal:
    I can't use the approach of the list of generic hittables, Burst won't let me
    use such a thing in a job.

     */
}