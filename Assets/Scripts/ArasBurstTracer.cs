//#define DO_ANIMATE
#define DO_LIGHT_SAMPLING
#define DO_THREADED

using Unity.Mathematics;
using static Unity.Mathematics.math;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using UnityEngine;

namespace Aras {
    public static class MathUtil {
        public const float Pi = (float)System.Math.PI;
        public const float Tau = (float)(System.Math.PI * 2.0);

        public static bool Refract(float3 v, float3 n, float nint, out float3 outRefracted) {
            float dt = dot(v, n);
            float discr = 1.0f - nint * nint * (1 - dt * dt);
            if (discr > 0) {
                outRefracted = nint * (v - n * dt) - n * sqrt(discr);
                return true;
            }
            outRefracted = new float3(0, 0, 0);
            return false;
        }

        public static float Schlick(float cosine, float ri) {
            float r0 = (1 - ri) / (1 + ri);
            r0 = r0 * r0;
            return r0 + (1 - r0) * pow(1 - cosine, 5);
        }

        static uint XorShift32(ref uint state) {
            uint x = state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 15;
            state = x;
            return x;
        }

        public static float RandomFloat01(ref uint state) {
            return (XorShift32(ref state) & 0xFFFFFF) / 16777216.0f;
        }

        public static float3 RandomInUnitDisk(ref uint state) {
            float3 p;
            do {
                p = 2.0f * new float3(RandomFloat01(ref state), RandomFloat01(ref state), 0) - new float3(1, 1, 0);
            } while (math.lengthsq(p) >= 1.0);
            return p;
        }

        public static float3 RandomInUnitSphere(ref uint state) {
            float3 p;
            do {
                p = 2.0f * new float3(RandomFloat01(ref state), RandomFloat01(ref state), RandomFloat01(ref state)) - new float3(1, 1, 1);
            } while (math.lengthsq(p) >= 1.0);
            return p;
        }
    }

    public struct Ray {
        public float3 orig;
        public float3 dir;

        public Ray(float3 orig_, float3 dir_) {
            orig = orig_;
            dir = dir_;
        }

        public float3 PointAt(float t) { return orig + dir * t; }
    }

    public struct Hit {
        public float3 pos;
        public float3 normal;
        public float t;
    }

    public struct Sphere {
        public float3 center;
        public float radius;
        public float invRadius;

        public Sphere(float3 center_, float radius_) { center = center_; radius = radius_; invRadius = 1.0f / radius_; }
        public void UpdateDerivedData() { invRadius = 1.0f / radius; }

        public bool HitSphere(Ray r, float tMin, float tMax, ref Hit outHit) {
            float3 oc = r.orig - center;
            float b = dot(oc, r.dir);
            float c = dot(oc, oc) - radius * radius;
            float discr = b * b - c;
            if (discr > 0) {
                float discrSq = sqrt(discr);

                float t = (-b - discrSq);
                if (t < tMax && t > tMin) {
                    outHit.pos = r.PointAt(t);
                    outHit.normal = (outHit.pos - center) * invRadius;
                    outHit.t = t;
                    return true;
                }
                t = (-b + discrSq);
                if (t < tMax && t > tMin) {
                    outHit.pos = r.PointAt(t);
                    outHit.normal = (outHit.pos - center) * invRadius;
                    outHit.t = t;
                    return true;
                }
            }
            return false;
        }
    }

    struct Camera {
        // vfov is top to bottom in degrees
        public Camera(float3 lookFrom, float3 lookAt, float3 vup, float vfov, float aspect, float aperture, float focusDist) {
            lensRadius = aperture / 2;
            float theta = vfov * MathUtil.Pi / 180f;
            float halfHeight = tan(theta / 2);
            float halfWidth = aspect * halfHeight;
            origin = lookFrom;
            w = normalize(lookFrom - lookAt);
            u = normalize(cross(vup, w));
            v = cross(w, u);
            lowerLeftCorner = origin - halfWidth * focusDist * u - halfHeight * focusDist * v - focusDist * w;
            horizontal = 2 * halfWidth * focusDist * u;
            vertical = 2 * halfHeight * focusDist * v;
        }

        public Ray GetRay(float s, float t, ref uint state) {
            float3 rd = lensRadius * MathUtil.RandomInUnitDisk(ref state);
            float3 offset = u * rd.x + v * rd.y;
            return new Ray(origin + offset, normalize(lowerLeftCorner + s * horizontal + t * vertical - origin - offset));
        }

        float3 origin;
        float3 lowerLeftCorner;
        float3 horizontal;
        float3 vertical;
        float3 u, v, w;
        float lensRadius;
    }

    struct Material {
        public enum Type { Lambert, Metal, Dielectric };
        public Type type;
        public float3 albedo;
        public float3 emissive;
        public float roughness;
        public float ri;
        public Material(Type t, float3 a, float3 e, float r, float i) {
            type = t; albedo = a; emissive = e; roughness = r; ri = i;
        }
        public bool HasEmission => emissive.x > 0 || emissive.y > 0 || emissive.z > 0;
    };

    class Test {
        const int DO_SAMPLES_PER_PIXEL = 8;
        const float DO_ANIMATE_SMOOTHING = 0.5f;

        static Sphere[] s_SpheresData = {
        new Sphere(new float3(0,-100.5f,-1), 100),
        new Sphere(new float3(2,0,-1), 0.5f),
        new Sphere(new float3(0,0,-1), 0.5f),
        new Sphere(new float3(-2,0,-1), 0.5f),
        new Sphere(new float3(2,0,1), 0.5f),
        new Sphere(new float3(0,0,1), 0.5f),
        new Sphere(new float3(-2,0,1), 0.5f),
        new Sphere(new float3(0.5f,1,0.5f), 0.5f),
        new Sphere(new float3(-1.5f,1.5f,0f), 0.3f),
    };

        static Material[] s_SphereMatsData = {
        new Material(Material.Type.Lambert,     new float3(0.8f, 0.8f, 0.8f), new float3(0,0,0), 0, 0),
        new Material(Material.Type.Lambert,     new float3(0.8f, 0.4f, 0.4f), new float3(0,0,0), 0, 0),
        new Material(Material.Type.Lambert,     new float3(0.4f, 0.8f, 0.4f), new float3(0,0,0), 0, 0),
        new Material(Material.Type.Metal,       new float3(0.4f, 0.4f, 0.8f), new float3(0,0,0), 0, 0),
        new Material(Material.Type.Metal,       new float3(0.4f, 0.8f, 0.4f), new float3(0,0,0), 0, 0),
        new Material(Material.Type.Metal,       new float3(0.4f, 0.8f, 0.4f), new float3(0,0,0), 0.2f, 0),
        new Material(Material.Type.Metal,       new float3(0.4f, 0.8f, 0.4f), new float3(0,0,0), 0.6f, 0),
        new Material(Material.Type.Dielectric,  new float3(0.4f, 0.4f, 0.4f), new float3(0,0,0), 0, 1.5f),
        new Material(Material.Type.Lambert,     new float3(0.8f, 0.6f, 0.2f), new float3(30,25,15), 0, 0),
    };

        const float kMinT = 0.001f;
        const float kMaxT = 1.0e7f;
        const int kMaxDepth = 10;


        static bool HitWorld(Ray r, float tMin, float tMax, ref Hit outHit, ref int outID, NativeArray<Sphere> spheres) {
            Hit tmpHit = default(Hit);
            bool anything = false;
            float closest = tMax;
            for (int i = 0; i < spheres.Length; ++i) {
                if (spheres[i].HitSphere(r, tMin, closest, ref tmpHit)) {
                    anything = true;
                    closest = tmpHit.t;
                    outHit = tmpHit;
                    outID = i;
                }
            }
            return anything;
        }

        static bool Scatter(Material mat, Ray r_in, Hit rec, out float3 attenuation, out Ray scattered, out float3 outLightE, ref int inoutRayCount, NativeArray<Sphere> spheres, NativeArray<Material> materials, ref uint randState) {
            outLightE = new float3(0, 0, 0);
            if (mat.type == Material.Type.Lambert) {
                // random point inside unit sphere that is tangent to the hit point
                float3 target = rec.pos + rec.normal + MathUtil.RandomInUnitSphere(ref randState);
                scattered = new Ray(rec.pos, normalize(target - rec.pos));
                attenuation = mat.albedo;

                // sample lights
#if DO_LIGHT_SAMPLING
                for (int i = 0; i < spheres.Length; ++i) {
                    if (!materials[i].HasEmission)
                        continue; // skip non-emissive
                                  //@TODO if (&mat == &smat)
                                  //    continue; // skip self
                    var s = spheres[i];

                    // create a random direction towards sphere
                    // coord system for sampling: sw, su, sv
                    float3 sw = normalize(s.center - rec.pos);
                    float3 su = normalize(cross(abs(sw.x) > 0.01f ? new float3(0, 1, 0) : new float3(1, 0, 0), sw));
                    float3 sv = cross(sw, su);
                    // sample sphere by solid angle
                    float cosAMax = sqrt(max(0.0f, 1.0f - s.radius * s.radius / math.lengthsq(rec.pos - s.center)));
                    float eps1 = MathUtil.RandomFloat01(ref randState), eps2 = MathUtil.RandomFloat01(ref randState);
                    float cosA = 1.0f - eps1 + eps1 * cosAMax;
                    float sinA = sqrt(1.0f - cosA * cosA);
                    float phi = MathUtil.Tau * eps2;
                    float3 l = su * cos(phi) * sinA + sv * sin(phi) * sinA + sw * cosA;
                    l = normalize(l);

                    // shoot shadow ray
                    Hit lightHit = default(Hit);
                    int hitID = 0;
                    ++inoutRayCount;
                    if (HitWorld(new Ray(rec.pos, l), kMinT, kMaxT, ref lightHit, ref hitID, spheres) && hitID == i) {
                        float omega = MathUtil.Tau * (1 - cosAMax);

                        float3 rdir = r_in.dir;
                        float3 nl = dot(rec.normal, rdir) < 0 ? rec.normal : -rec.normal;
                        outLightE += (mat.albedo * materials[i].emissive) * (math.max(0.0f, math.dot(l, nl)) * omega / MathUtil.Pi);
                    }
                }
#endif
                return true;
            } else if (mat.type == Material.Type.Metal) {
                float3 refl = reflect(r_in.dir, rec.normal);
                // reflected ray, and random inside of sphere based on roughness
                scattered = new Ray(rec.pos, normalize(refl + mat.roughness * MathUtil.RandomInUnitSphere(ref randState)));
                attenuation = mat.albedo;
                return dot(scattered.dir, rec.normal) > 0;
            } else if (mat.type == Material.Type.Dielectric) {
                float3 outwardN;
                float3 rdir = r_in.dir;
                float3 refl = reflect(rdir, rec.normal);
                float nint;
                attenuation = new float3(1, 1, 1);
                float3 refr;
                float reflProb;
                float cosine;
                if (dot(rdir, rec.normal) > 0) {
                    outwardN = -rec.normal;
                    nint = mat.ri;
                    cosine = mat.ri * dot(rdir, rec.normal);
                } else {
                    outwardN = rec.normal;
                    nint = 1.0f / mat.ri;
                    cosine = -dot(rdir, rec.normal);
                }
                if (MathUtil.Refract(rdir, outwardN, nint, out refr)) {
                    reflProb = MathUtil.Schlick(cosine, mat.ri);
                } else {
                    reflProb = 1;
                }
                if (MathUtil.RandomFloat01(ref randState) < reflProb)
                    scattered = new Ray(rec.pos, normalize(refl));
                else
                    scattered = new Ray(rec.pos, normalize(refr));
            } else {
                attenuation = new float3(1, 0, 1);
                scattered = default(Ray);
                return false;
            }
            return true;
        }

        static float3 Trace(Ray r, int depth, ref int inoutRayCount, NativeArray<Sphere> spheres, NativeArray<Material> materials, ref uint randState) {
            Hit rec = default(Hit);
            int id = 0;
            ++inoutRayCount;
            if (HitWorld(r, kMinT, kMaxT, ref rec, ref id, spheres)) {
                Ray scattered;
                float3 attenuation;
                float3 lightE;
                var mat = materials[id];
                if (depth < kMaxDepth && Scatter(mat, r, rec, out attenuation, out scattered, out lightE, ref inoutRayCount, spheres, materials, ref randState)) {
                    return mat.emissive + lightE + attenuation * Trace(scattered, depth + 1, ref inoutRayCount, spheres, materials, ref randState);
                } else {
                    return mat.emissive;
                }
            } else {
                // sky
                float3 unitDir = r.dir;
                float t = 0.5f * (unitDir.y + 1.0f);
                return ((1.0f - t) * new float3(1.0f, 1.0f, 1.0f) + t * new float3(0.5f, 0.7f, 1.0f)) * 0.3f;
            }
        }

        [BurstCompile]
        struct TraceRowJob : IJobParallelFor {
            public int screenWidth, screenHeight, frameCount;
            [NativeDisableParallelForRestriction] public NativeArray<UnityEngine.Color> backbuffer;
            public Camera cam;

            [NativeDisableParallelForRestriction] public NativeArray<int> rayCounter;
            [NativeDisableParallelForRestriction] public NativeArray<Sphere> spheres;
            [NativeDisableParallelForRestriction] public NativeArray<Material> materials;

            public void Execute(int y) {
                int backbufferIdx = y * screenWidth;
                float invWidth = 1.0f / screenWidth;
                float invHeight = 1.0f / screenHeight;
                float lerpFac = (float)frameCount / (float)(frameCount + 1);
#if DO_ANIMATE
            lerpFac *= DO_ANIMATE_SMOOTHING;
#endif
                uint state = (uint)(y * 9781 + frameCount * 6271) | 1;
                int rayCount = 0;
                for (int x = 0; x < screenWidth; ++x) {
                    float3 col = new float3(0, 0, 0);
                    for (int s = 0; s < DO_SAMPLES_PER_PIXEL; s++) {
                        float u = (x + MathUtil.RandomFloat01(ref state)) * invWidth;
                        float v = (y + MathUtil.RandomFloat01(ref state)) * invHeight;
                        Ray r = cam.GetRay(u, v, ref state);
                        col += Trace(r, 0, ref rayCount, spheres, materials, ref state);
                    }
                    col *= 1.0f / (float)DO_SAMPLES_PER_PIXEL;
                    col = sqrt(col);

                    UnityEngine.Color prev = backbuffer[backbufferIdx];
                    col = new float3(prev.r, prev.g, prev.b) * lerpFac + col * (1 - lerpFac);
                    backbuffer[backbufferIdx] = new UnityEngine.Color(col.x, col.y, col.z, 1);
                    backbufferIdx++;
                }
                rayCounter[0] += rayCount; //@TODO: how to do atomics add?
            }
        }


        public void DrawTest(float time, int frameCount, int screenWidth, int screenHeight, NativeArray<UnityEngine.Color> backbuffer, out int outRayCount) {
            int rayCount = 0;
#if DO_ANIMATE
        s_SpheresData[1].center.y = cos(time)+1.0f;
        s_SpheresData[8].center.z = sin(time)*0.3f;
#endif
            float3 lookfrom = new float3(0, 2, 3);
            float3 lookat = new float3(0, 0, 0);
            float distToFocus = 3;
            float aperture = 0.1f;

            for (int i = 0; i < s_SpheresData.Length; ++i)
                s_SpheresData[i].UpdateDerivedData();

            Camera cam = new Camera(lookfrom, lookat, new float3(0, 1, 0), 60, (float)screenWidth / (float)screenHeight, aperture, distToFocus);

// #if DO_THREADED
            TraceRowJob job;
            job.screenWidth = screenWidth;
            job.screenHeight = screenHeight;
            job.frameCount = frameCount;
            job.backbuffer = backbuffer;
            job.cam = cam;
            job.rayCounter = new NativeArray<int>(1, Allocator.Temp);
            job.spheres = new NativeArray<Sphere>(s_SpheresData, Allocator.Temp);
            job.materials = new NativeArray<Material>(s_SphereMatsData, Allocator.Temp);
            var fence = job.Schedule(screenHeight, 4);
            fence.Complete();
            rayCount = job.rayCounter[0];
            job.rayCounter.Dispose();
            job.spheres.Dispose();
            job.materials.Dispose();
// #else
//         for (int y = 0; y < screenHeight; ++y)
//             rayCount += TraceRowJob(y, screenWidth, screenHeight, frameCount, backbuffer, ref cam);
// #endif
            outRayCount = rayCount;
        }
    }
}

public class ArasBurstTracer : MonoBehaviour {
    private Color[] _colors;
    private Texture2D _tex;

    public void Awake() {
        int resX = 2048;
        int resY = 512;

        var buf = new NativeArray<Color>(resX * resY, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
        var test = new Aras.Test();
        int rayCount;
        test.DrawTest(0f, 1, resX, resY, buf, out rayCount);
        UnityEngine.Debug.Log(rayCount);

        test.DrawTest(0f, 1, resX, resY, buf, out rayCount);
        UnityEngine.Debug.Log(rayCount);

        _colors = new Color[resX * resY];
        _tex = new Texture2D(resX, resY, TextureFormat.ARGB32, false, true);
        _tex.filterMode = FilterMode.Point;
        ToTexture2D(buf, _colors, _tex);

        buf.Dispose();
    }

    private void OnGUI() {
        GUI.DrawTexture(new Rect(0f, 0f, _tex.width, _tex.height), _tex);
    }

    private static void ToTexture2D(NativeArray<Color> screen, Color[] colors, Texture2D tex) {
        for (int i = 0; i < screen.Length; i++) {
            var c = screen[i];
            colors[i] = c;
        }

        tex.SetPixels(0, 0, tex.width, tex.height, colors, 0);
        tex.Apply();
    }
}