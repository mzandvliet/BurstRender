using UnityEngine;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Rng = Unity.Mathematics.Random;

/*
    Todo:
    - Generate functions using Berstein Polynomial expansion
    - Use NativeSlice to better encapsulate multiple curves
    in a single Array?
    - Proper support for regular -> rational curves


    Use a code-generation script. C# doesn't have preprocessor
    or macro functionality, and yet we want to quickly write
    code that works with 1 to n dimensional control points.

    Also, since the patterns for higher order curves are 
    elegantly expressed through algebra, we could generate
    code for the basis functions quite well.

 */

// Borrowing from: https://www.youtube.com/watch?v=o9RK6O2kOKo

public static class BDCCubic4d {
    public static float4 GetCasteljau(NativeArray<float4> c, in float t) {
        float4 bc = math.lerp(c[1], c[2], t);
        return math.lerp(math.lerp(math.lerp(c[0], c[1], t), bc, t), math.lerp(bc, math.lerp(c[2], c[3], t), t), t);
    }

    public static void Split(NativeArray<float4> o, in float t, NativeArray<float4> left, NativeArray<float4> right) {
        float4 ab = math.lerp(o[0], o[1], t);
        float4 bc = math.lerp(o[1], o[2], t);
        float4 cd = math.lerp(o[2], o[3], t);

        float4 abbc = math.lerp(ab, bc, t);
        float4 bccd = math.lerp(bc, cd, t);

        float4 p = math.lerp(abbc, bccd, t);

        left[0] = o[0];
        left[1] = ab;
        left[2] = abbc;
        left[3] = p;

        right[0] = p;
        right[1] = bccd;
        right[2] = cd;
        right[3] = o[3];
    }

    public static float4 Get(NativeArray<float4> c, in float t) {
        float omt = 1f - t;
        float omt2 = omt * omt;
        float t2 = t * t;
        return
            c[0] * (omt2 * omt) +
            c[1] * (3f * omt2 * t) +
            c[2] * (3f * omt * t2) +
            c[3] * (t2 * t);
    }

    public static float4 GetAt(NativeArray<float4> c, in float t, int idx) {
        float omt = 1f - t;
        float omt2 = omt * omt;
        float t2 = t * t;
        idx *= 4;
        return
            c[idx + 0] * (omt2 * omt) +
            c[idx + 1] * (3f * omt2 * t) +
            c[idx + 2] * (3f * omt * t2) +
            c[idx + 3] * (t2 * t);
    }

    public static float4 GetTangent(NativeArray<float4> c, in float t) {
        float omt = 1f - t;
        float omt2 = omt * omt;
        float t2 = t * t;
        return math.normalize(
            c[0] * (-omt2) +
            c[1] * (3f * omt2 - 2f * omt) +
            c[2] * (-3f * t2 + 2f * t) +
            c[3] * (t2)
        );
    }

    public static float4 GetNonUnitTangent(NativeArray<float4> c, in float t) {
        float omt = 1f - t;
        float omt2 = omt * omt;
        float t2 = t * t;
        return
            c[0] * (-omt2) +
            c[1] * (3f * omt2 - 2f * omt) +
            c[2] * (-3f * t2 + 2f * t) +
            c[3] * (t2);
    }

    public static float4 GetTangentAt(NativeArray<float4> c, in float t, int idx) {
        float omt = 1f - t;
        float omt2 = omt * omt;
        float t2 = t * t;
        idx *= 4;
        return math.normalize(
            c[idx + 0] * (-omt2) +
            c[idx + 1] * (3f * omt2 - 2f * omt) +
            c[idx + 2] * (-3f * t2 + 2f * t) +
            c[idx + 3] * (t2)
        );
    }

    public static float Length(NativeArray<float4> c, in int steps) {
        float dist = 0;

        float4 pPrev = c[0];
        for (int i = 1; i <= steps; i++) {
            float t = i / (float)steps;
            float4 p = Get(c, t);
            dist += math.length(p - pPrev);
            pPrev = p;
        }

        return dist;
    }

    public static float Length(NativeArray<float4> c, in int steps, in float t) {
        float dist = 0;

        float4 pPrev = c[0];
        for (int i = 1; i <= steps; i++) {
            float tNow = t * (i / (float)steps);
            float4 p = Get(c, tNow);
            dist += math.length(p - pPrev);
            pPrev = p;
        }

        return dist;
    }

    public static float Length(NativeArray<float> distances, float t) {
        t = t * (float)(distances.Length - 1);
        int ti = (int)math.floor(t);
        if (ti >= distances.Length - 1) {
            return distances[distances.Length - 1];
        }
        return math.lerp(distances[ti], distances[ti + 1], t - (float)ti);
    }

    // Instead of storing at linear t spacing, why not store with non-linear t-spacing and lerp between them
    public static void CacheDistancesAt(NativeArray<float4> c, NativeArray<float> outDistances) {
        float dist = 0;
        outDistances[0] = 0f;
        float4 pPrev = c[0];
        for (int i = 1; i < outDistances.Length; i++) {
            float t = i / (float)(outDistances.Length - 1);
            float4 p = Get(c, t);
            dist += math.length(p - pPrev);
            outDistances[i] = dist;
            pPrev = p;
        }
    }
}

public static class BDCCubic3d {
    public static float3 GetCasteljau(NativeArray<float3> c, in float t) {
        float3 bc = math.lerp(c[1], c[2], t);
        return math.lerp(math.lerp(math.lerp(c[0], c[1], t), bc, t), math.lerp(bc, math.lerp(c[2], c[3], t), t), t);
    }

    public static void Split(NativeArray<float3> o, in float t, NativeArray<float3> left, NativeArray<float3> right) {
        float3 ab = math.lerp(o[0], o[1], t);
        float3 bc = math.lerp(o[1], o[2], t);
        float3 cd = math.lerp(o[2], o[3], t);

        float3 abbc = math.lerp(ab, bc, t);
        float3 bccd = math.lerp(bc, cd, t);

        float3 p = math.lerp(abbc, bccd, t);

        left[0] = o[0];
        left[1] = ab;
        left[2] = abbc;
        left[3] = p;

        right[0] = p;
        right[1] = bccd;
        right[2] = cd;
        right[3] = o[3];
    }

    public static float3 Get(NativeArray<float3> c, in float t) {
        float omt = 1f - t;
        float omt2 = omt * omt;
        float t2 = t * t;
        return
            c[0] * (omt2 * omt) +
            c[1] * (3f * omt2 * t) +
            c[2] * (3f * omt * t2) +
            c[3] * (t2 * t);
    }

    public static float3 GetAt(NativeArray<float3> c, in float t, int idx) {
        float omt = 1f - t;
        float omt2 = omt * omt;
        float t2 = t * t;
        idx *= 4;
        return
            c[idx + 0] * (omt2 * omt) +
            c[idx + 1] * (3f * omt2 * t) +
            c[idx + 2] * (3f * omt * t2) +
            c[idx + 3] * (t2 * t);
    }

    public static float3 GetTangent(NativeArray<float3> c, in float t) {
        float omt = 1f - t;
        float omt2 = omt * omt;
        float t2 = t * t;
        return math.normalize(
            c[0] * (-omt2) +
            c[1] * (3f * omt2 - 2f * omt) +
            c[2] * (-3f * t2 + 2f * t) +
            c[3] * (t2)
        );
    }

    public static float3 GetNonUnitTangent(NativeArray<float3> c, in float t) {
        float omt = 1f - t;
        float omt2 = omt * omt;
        float t2 = t * t;
        return
            c[0] * (-omt2) +
            c[1] * (3f * omt2 - 2f * omt) +
            c[2] * (-3f * t2 + 2f * t) +
            c[3] * (t2);
    }

    public static float3 GetTangentAt(NativeArray<float3> c, in float t, int idx) {
        float omt = 1f - t;
        float omt2 = omt * omt;
        float t2 = t * t;
        idx *= 4;
        return math.normalize(
            c[idx + 0] * (-omt2) +
            c[idx + 1] * (3f * omt2 - 2f * omt) +
            c[idx + 2] * (-3f * t2 + 2f * t) +
            c[idx + 3] * (t2)
        );
    }

    // Assumes tangent and up are already normal vectors
    public static float3 GetNormal(NativeArray<float3> c, in float t, in float3 up) {
        float3 tangent = GetTangent(c, t);
        float3 binorm = math.cross(up, tangent);
        return math.cross(tangent, binorm);
    }

    public static float3 GetNonUnitNormal(NativeArray<float3> c, in float t, in float3 up) {
        float3 tangent = GetNonUnitTangent(c, t);
        float3 binorm = math.cross(up, tangent);
        return math.cross(tangent, binorm);
    }


    public static float3 GetNormalAt(NativeArray<float3> c, in float t, in float3 up, int idx) {
        float3 tangent = GetTangentAt(c, t, idx);
        float3 binorm = math.cross(up, tangent);
        return math.normalize(math.cross(tangent, binorm));
    }

    public static float Length(NativeArray<float3> c, in int steps) {
        float dist = 0;

        float3 pPrev = c[0];
        for (int i = 1; i <= steps; i++) {
            float t = i / (float)steps;
            float3 p = Get(c, t);
            dist += math.length(p - pPrev);
            pPrev = p;
        }

        return dist;
    }

    public static float Length(NativeArray<float3> c, in int steps, in float t) {
        float dist = 0;

        float3 pPrev = c[0];
        for (int i = 1; i <= steps; i++) {
            float tNow = t * (i / (float)steps);
            float3 p = Get(c, tNow);
            dist += math.length(p - pPrev);
            pPrev = p;
        }

        return dist;
    }

    public static float Length(NativeArray<float> distances, float t) {
        t = t * (float)(distances.Length - 1);
        int ti = (int)math.floor(t);
        if (ti >= distances.Length - 1) {
            return distances[distances.Length - 1];
        }
        return math.lerp(distances[ti], distances[ti + 1], t - (float)ti);
    }

    // Instead of storing at linear t spacing, why not store with non-linear t-spacing and lerp between them
    public static void CacheDistancesAt(NativeArray<float3> c, NativeArray<float> outDistances) {
        float dist = 0;
        outDistances[0] = 0f;
        float3 pPrev = c[0];
        for (int i = 1; i < outDistances.Length; i++) {
            float t = i / (float)(outDistances.Length - 1);
            float3 p = Get(c, t);
            dist += math.length(p - pPrev);
            outDistances[i] = dist;
            pPrev = p;
        }
    }
}

public static class BDCCubic2d {
    public static float2 GetCasteljau(NativeArray<float2> c, in float t) {
        float2 bc = math.lerp(c[1], c[2], t);
        return math.lerp(math.lerp(math.lerp(c[0], c[1], t), bc, t), math.lerp(bc, math.lerp(c[2], c[3], t), t), t);
    }

    public static float2 Get(NativeArray<float2> c, in float t) {
        float omt = 1f - t;
        float omt2 = omt * omt;
        float t2 = t * t;
        return
            c[0] * (omt2 * omt) +
            c[1] * (3f * omt2 * t) +
            c[2] * (3f * omt * t2) +
            c[3] * (t2 * t);
    }

    public static float2 Get(NativeArray<float2> c, NativeArray<float> w, in float t) {
        float omt = 1f - t;
        float omt2 = omt * omt;
        float t2 = t * t;
        return
            c[0] * w[0] * (omt2 * omt) +
            c[1] * w[1] * (3f * omt2 * t) +
            c[2] * w[2] * (3f * omt * t2) +
            c[3] * w[3] * (t2 * t);
    }

    public static float2 GetAt(NativeArray<float2> c, in float t, int idx) {
        float omt = 1f - t;
        float omt2 = omt * omt;
        float t2 = t * t;
        idx *= 4;
        return
            c[idx + 0] * (omt2 * omt) +
            c[idx + 1] * (3f * omt2 * t) +
            c[idx + 2] * (3f * omt * t2) +
            c[idx + 3] * (t2 * t);
    }

    public static float2 GetTangent(NativeArray<float2> c, in float t) {
        float omt = 1f - t;
        float omt2 = omt * omt;
        float t2 = t * t;
        return math.normalize(
            c[0] * (-omt2) +
            c[1] * (3f * omt2 - 2f * omt) +
            c[2] * (-3f * t2 + 2f * t) +
            c[3] * (t2)
        );
    }

    public static float2 GetNonUnitTangent(NativeArray<float2> c, in float t) {
        float omt = 1f - t;
        float omt2 = omt * omt;
        float t2 = t * t;
        return
            c[0] * (-omt2) +
            c[1] * (3f * omt2 - 2f * omt) +
            c[2] * (-3f * t2 + 2f * t) +
            c[3] * (t2);
    }

    public static float2 GetTangentAt(NativeArray<float2> c, in float t, int idx) {
        float omt = 1f - t;
        float omt2 = omt * omt;
        float t2 = t * t;
        idx *= 4;
        return math.normalize(
            c[idx + 0] * (-omt2) +
            c[idx + 1] * (3f * omt2 - 2f * omt) +
            c[idx + 2] * (-3f * t2 + 2f * t) +
            c[idx + 3] * (t2)
        );
    }

    public static float2 GetNormal(NativeArray<float2> c, in float t) {
        float2 tangent = math.normalize(GetTangent(c, t));
        return new float2(-tangent.y, tangent.x);
    }

    public static float2 GetNormalAt(NativeArray<float2> c, in float t, int idx) {
        float2 tangent = math.normalize(GetTangentAt(c, t, idx));
        return new float2(-tangent.y, tangent.x);
    }

    public static float GetLength(NativeArray<float2> c, in int steps) {
        float dist = 0;

        float2 pPrev = c[0];
        for (int i = 1; i <= steps; i++) {
            float t = i / (float)steps;
            float2 p = Get(c, t);
            dist += math.length(p - pPrev);
            pPrev = p;
        }

        return dist;
    }

    public static float GetLength(NativeArray<float2> c, in int steps, in float t) {
        float dist = 0;

        float2 pPrev = c[0];
        for (int i = 1; i <= steps; i++) {
            float tNow = t * (i / (float)steps);
            float2 p = Get(c, tNow);
            dist += math.length(p - pPrev);
            pPrev = p;
        }

        return dist;
    }

    public static float GetLength(NativeArray<float> distances, float t) {
        t = t * (float)(distances.Length - 1);
        int ti = (int)math.floor(t);
        if (ti >= distances.Length - 1) {
            return distances[distances.Length - 1];
        }
        return math.lerp(distances[ti], distances[ti + 1], t - (float)ti);
    }

    // Instead of storing at linear t spacing, why not store with non-linear t-spacing and lerp between them
    public static void CacheDistances(NativeArray<float2> c, NativeArray<float> outDistances) {
        float dist = 0;
        outDistances[0] = 0f;
        float2 pPrev = c[0];
        for (int i = 1; i < outDistances.Length; i++) {
            float t = i / (float)(outDistances.Length - 1);
            float2 p = Get(c, t);
            dist += math.length(p - pPrev);
            outDistances[i] = dist;
            pPrev = p;
        }
    }

    public static void CacheDistancesAt(NativeArray<float2> c, NativeArray<float> outDistances, int idx) {
        float dist = 0;
        outDistances[0] = 0f;
        float2 pPrev = c[0];
        idx *= 4;
        for (int i = 1; i < outDistances.Length; i++) {
            float t = i / (float)(outDistances.Length - 1);
            float2 p = GetAt(c, t, idx);
            dist += math.length(p - pPrev);
            outDistances[i] = dist;
            pPrev = p;
        }
    }
}

public static class BDCQuadratic3d {
    public static float3 GetCasteljau(in float3 a, in float3 b, in float3 c, in float t) {
        return math.lerp(math.lerp(a, b, t), math.lerp(b, c, t), t);
    }

    public static float3 Get(in float3 a, in float3 b, in float3 c, in float t) {
        float3 u = 1f - t;
        return u * u * a + 2f * t * u * b + t * t * c;
    }

    public static float3 EvaluateNormalApprox(float3 a, float3 b, float3 c, float t) {
        const float EPS = 0.001f;

        float3 p0 = Get(a, b, c, t - EPS);
        float3 p1 = Get(a, b, c, t + EPS);

        return math.cross(new float3(0, 0, 1), math.normalize(p1 - p0));
    }

    public static float LengthEuclidApprox(in float3 a, in float3 b, in float3 c, in int steps) {
        float dist = 0;

        float3 pPrev = Get(a, b, c, 0f);
        for (int i = 1; i <= steps; i++) {
            float t = i / (float)steps;
            float3 p = Get(a, b, c, t);
            dist += math.length(p - pPrev);
            pPrev = p;
        }

        return dist;
    }

    public static float LengthEuclidApprox(in float3 a, in float3 b, in float3 c, in int steps, in float t) {
        float dist = 0;

        float3 pPrev = Get(a, b, c, 0f);
        for (int i = 1; i <= steps; i++) {
            float tNow = t * (i / (float)steps);
            float3 p = Get(a, b, c, tNow);
            dist += math.length(p - pPrev);
            pPrev = p;
        }

        return dist;
    }

    public static float LengthEuclidApprox(NativeArray<float> distances, float t) {
        t = t * (float)(distances.Length - 1);
        int ti = (int)math.floor(t);
        if (ti >= distances.Length - 1) {
            return distances[distances.Length - 1];
        }
        return math.lerp(distances[ti], distances[ti + 1], t - (float)ti);
    }

    // Instead of storing at linear t spacing, why not store with non-linear t-spacing and lerp between them
    public static void CacheDistances(in float3 a, in float3 b, in float3 c, NativeArray<float> outDistances) {
        float dist = 0;
        outDistances[0] = 0f;
        float3 pPrev = Get(a, b, c, 0f); // Todo: this is just point a
        for (int i = 1; i < outDistances.Length; i++) {
            float t = i / (float)(outDistances.Length - 1);
            float3 p = Get(a, b, c, t);
            dist += math.length(p - pPrev);
            outDistances[i] = dist;
            pPrev = p;
        }
    }
}


public static class BDCQuadratic2d {
    public static float2 GetCasteljau(in float2 a, in float2 b, in float2 c, in float t) {
        return math.lerp(math.lerp(a, b, t), math.lerp(b, c, t), t);
    }

    public static float2 Get(in float2 a, in float2 b, in float2 c, in float t) {
        float2 u = 1f - t;
        return u * u * a + 2f * t * u * b + t * t * c;
    }

    public static float2 Get(NativeArray<float2> c, NativeArray<float> w, in float t) {
        float u = 1f - t;
        float t2 = t * t;
        return
            c[0] * w[0] * (u * u) +
            c[1] * w[1] * (2f * t * u) +
            c[2] * w[2] * t2;
    }

    public static float LengthEuclidean(in float2 a, in float2 b, in float2 c, in int steps) {
        float dist = 0;

        float2 pPrev = BDCQuadratic2d.Get(a, b, c, 0f);
        for (int i = 1; i <= steps; i++) {
            float t = i / (float)steps;
            float2 p = BDCQuadratic2d.Get(a, b, c, t);
            dist += math.length(p - pPrev);
            pPrev = p;
        }

        return dist;
    }
}