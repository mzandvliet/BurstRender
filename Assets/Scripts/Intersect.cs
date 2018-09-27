using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using Ramjet;
using System.Runtime.CompilerServices;

using Random = Unity.Mathematics.Random;
using Unity.Burst;
using Unity.Jobs;

namespace Tracing {
    [System.Serializable]
    public struct Ray3f : System.IEquatable<Ray3f>, System.IFormattable {
        public float3 origin;
        public float3 direction;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Ray3f(float3 origin, float3 direction) {
            this.origin = origin;
            this.direction = direction;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(Ray3f other) {
            return this.origin.Equals(other.origin) && this.direction.Equals(other.direction);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string ToString(string format, System.IFormatProvider formatProvider) {
            return string.Format("{pos: {0}, dir: {1}}", origin, direction);
        }
    }

    public struct Camera {
        public float3 Position;
        public Quaternion Rotation;

        private float3 LowerLeft;
        private float3 Horizontal;
        private float3 Vertical;

        private float LensRadius;

        public float VFov {
            get;
            private set;
        }
        public float Aspect {
            get;
            private set;
        }

        public float FocusDistance {
            get;
            private set;
        }

        public Camera(float vfov, float aspect, float aperture, float focusDistance) {
            Position = new float3(0f);
            Rotation = quaternion.identity;
            LowerLeft = new float3(0f);
            Horizontal = new float3(0f);
            Vertical = new float3(0f);

            VFov = vfov;
            Aspect = aspect;
            LensRadius = aperture / 2f;
            FocusDistance = focusDistance;

            UpdateSettings();
        }

        public void UpdateSettings() {
            float theta = VFov * Mathf.Deg2Rad;
            float halfHeight = math.tan(theta / 2f);
            float halfWidth = Aspect * halfHeight;

            LowerLeft = new float3(-halfWidth * FocusDistance, -halfHeight * FocusDistance, FocusDistance);
            Horizontal = new float3(2f * halfWidth * FocusDistance, 0f, 0f);
            Vertical = new float3(0f, 2f * halfHeight * FocusDistance, 0f);
        }

        public Ray3f GetRay(float2 uv, ref Random rng) {
            // Todo: could optimize by storing pre-rotated lowerLeft, Horizontal and Vertical vectors.
            float3 offset = Math.RandomInUnitDisk(ref rng) * LensRadius;
            float3 direction = math.normalize(LowerLeft + uv.x * Horizontal + uv.y * Vertical - offset);
            offset = Rotation * offset;
            direction = Rotation * direction;
            return new Ray3f(
                Position + offset,
                direction
            );
        }
    }

    public struct HitRecord {
        public float t;
        public float3 point;
        public float3 normal;
        public Material material;
    }

    public struct Scene : System.IDisposable {
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

    public enum MaterialType { // If I make this : byte, switching on it makes burst cry
        Lambertian = 0,
        Metal = 1,
        Dielectric = 2
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

    public struct Plane {
        public float3 Center;
        public float3 Normal;
        public Material Material; // Todo: Don't store material here

        public Plane(float3 center, float3 normal, Material material) {
            Center = center;
            Normal = normal;
            Material = material;
        }
    }

    public struct Disk {
        public Plane Plane;
        public float Radius;
        public Material Material; // Todo: Don't store material here

        public Disk(Plane plane, float radius, Material material) {
            Plane = plane;
            Radius = radius;
            Material = material;
        }
    }

    public static class Intersect {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float3 PointOnRay(Ray3f r, float t) {
            return r.origin + r.direction * t;
        }

        public static bool Scene(Scene s, Ray3f r, float tMin, float tMax, out HitRecord finalHit) {
            bool hitAnything = false;
            HitRecord closestHit = new HitRecord();
            closestHit.t = tMax;

            // Note: this is the most naive brute force scene intersection you could ever do :P

            HitRecord hit;

            // Hit planes
            for (int i = 0; i < s.Planes.Length; i++) {
                if (Intersect.Plane(s.Planes[i], r, tMin, tMax, out hit)) {
                    if (hit.t < closestHit.t) {
                        hit.material = s.Planes[i].Material;
                        hitAnything = true;
                        closestHit = hit;
                    }
                }
            }

            // Hit disks
            for (int i = 0; i < s.Disks.Length; i++) {
                if (Intersect.Disk(s.Disks[i], r, tMin, tMax, out hit)) {
                    if (hit.t < closestHit.t) {
                        hit.material = s.Disks[i].Material;
                        hitAnything = true;
                        closestHit = hit;
                    }
                }
            }

            // // Hit spheres
            for (int i = 0; i < s.Spheres.Length; i++) {
                if (Intersect.Sphere(s.Spheres[i], r, tMin, tMax, out hit)) {
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

    public static class Trace {
        public static float3 TraceRecursive(Ray3f ray, Scene scene, ref Random rng, NativeArray<float3> fibs, int depth, int maxDepth, ref ushort rayCount) {
            HitRecord hit;

            if (depth >= maxDepth) {
                hit = new HitRecord();
                return new float3(0);
            }

            const float tMin = 0f;
            const float tMax = 1000f;

            bool hitSomething = Intersect.Scene(scene, ray, tMin, tMax, out hit);
            ++rayCount;

            float3 light = new float3(0);

            if (hitSomething) {
                // We see a thing through another thing, find that other thing, see what it sees, it might be light, but might end void
                // Filter it through its material model

                Ray3f subRay;
                bool scattered = Scatter(ray, hit, ref rng, fibs, out subRay);
                if (scattered) {
                    light = TraceRecursive(subRay, scene, ref rng, fibs, depth + 1, maxDepth, ref rayCount);
                }
                light = BRDF(hit, light);
            } else {
                // We see sunlight, just send that back through the path traversed

                float t = 0.5f * (ray.direction.y + 1f);
                light = (1f - t) * new float3(1f) + t * scene.LightColor;
            }

            return light;
        }

        public static bool Scatter(Ray3f ray, HitRecord hit, ref Random rng, NativeArray<float3> fibs, out Ray3f scattered) {
            const float eps = 0.0001f;

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
                        if (Refract(ray.direction, outwardNormal, nint, out refracted)) {
                            reflectProb = Schlick(cosine, refIdx);
                        }

                        bool reflect = rng.NextFloat() < reflectProb;

                        scattered = new Ray3f(
                            reflect ? hit.point + outwardNormal * eps : hit.point - outwardNormal * eps,
                            reflect ? reflected : refracted
                        );
                        return true;
                    }
                case MaterialType.Metal: {
                        // Todo: false if dot(reflected, normal) < 0
                        float3 transmitted = math.reflect(ray.direction, hit.normal);
                        transmitted += fibs[rng.NextInt(0, fibs.Length - 1)] * hit.material.Fuzz;
                        transmitted = math.normalize(transmitted);
                        scattered = new Ray3f(hit.point + hit.normal * eps, transmitted);
                        if (math.dot(scattered.direction, hit.normal) > 0) {
                            return true;
                        }
                        return false;
                    }
                case MaterialType.Lambertian:
                default: {
                        float3 target = hit.normal + fibs[rng.NextInt(0, fibs.Length - 1)];
                        float3 transmitted = math.normalize(target - hit.point);
                        scattered = new Ray3f(hit.point + hit.normal * eps, transmitted);
                        return true;
                    }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float3 Reflect(float3 v, float3 n) {
            return v - (2f * math.dot(v, n)) * n;
        }

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

        public static float3 BRDF(HitRecord hit, float3 light) {
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
    }
}
