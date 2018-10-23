using UnityEngine;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Rng = Unity.Mathematics.Random;

// Borrowing from: https://www.youtube.com/watch?v=o9RK6O2kOKo
public static class BDC3Cube {
    public static float3 Lerp(float3 a, float3 b, float t) {
        return t * a + (1f - t) * b;
    }
    public static float3 EvaluateCasteljau(NativeArray<float3> c, float t) {
        float3 bc = Lerp(c[1], c[2], t);
        return Lerp(Lerp(Lerp(c[0], c[1], t), bc, t), Lerp(bc, Lerp(c[2], c[3], t), t), t);
    }

    public static float3 Evaluate(NativeArray<float3> c, float t) {
        float omt = 1f - t;
        float omt2 = omt * omt;
        float t2 = t * t;
        return
            c[0] * (omt2 * omt) +
            c[1] * (3f * omt2 * t) +
            c[2] * (3f * omt * t2) +
            c[3] * (t2 * t);
    }

    public static float3 EvaluateTangent(NativeArray<float3> c, float t) {
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

    public static float3 EvaluateNormal(NativeArray<float3> c, float t, float3 up) {
        float3 tangent = EvaluateTangent(c, t);
        float3 binorm = math.cross(up, tangent);
        return math.normalize(math.cross(tangent, binorm));
    }

    public static float LengthEuclidApprox(NativeArray<float3> c, int steps) {
        float dist = 0;

        float3 pPrev = c[0];
        for (int i = 1; i <= steps; i++) {
            float t = i / (float)steps;
            float3 p = Evaluate(c, t);
            dist += math.length(p - pPrev);
            pPrev = p;
        }

        return dist;
    }

    public static float LengthEuclidApprox(NativeArray<float3> c, int steps, float t) {
        float dist = 0;

        float3 pPrev = c[0];
        for (int i = 1; i <= steps; i++) {
            float tNow = t * (i / (float)steps);
            float3 p = Evaluate(c, tNow);
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
    public static void CacheDistances(NativeArray<float3> c, NativeArray<float> outDistances) {
        float dist = 0;
        outDistances[0] = 0f;
        float3 pPrev = c[0];
        for (int i = 1; i < outDistances.Length; i++) {
            float t = i / (float)(outDistances.Length - 1);
            float3 p = Evaluate(c, t);
            dist += math.length(p - pPrev);
            outDistances[i] = dist;
            pPrev = p;
        }
    }
}

public static class BDCCubic2d {
    public static float2 GetCasteljau(NativeArray<float2> c, float t) {
        float2 bc = math.lerp(c[1], c[2], t);
        return math.lerp(math.lerp(math.lerp(c[0], c[1], t), bc, t), math.lerp(bc, math.lerp(c[2], c[3], t), t), t);
    }

    public static float2 Get(NativeArray<float2> c, float t) {
        float omt = 1f - t;
        float omt2 = omt * omt;
        float t2 = t * t;
        return
            c[0] * (omt2 * omt) +
            c[1] * (3f * omt2 * t) +
            c[2] * (3f * omt * t2) +
            c[3] * (t2 * t);
    }

    public static float2 GetAt(NativeArray<float2> c, float t, int idx) {
        float omt = 1f - t;
        float omt2 = omt * omt;
        float t2 = t * t;
        return
            c[idx + 0] * (omt2 * omt) +
            c[idx + 1] * (3f * omt2 * t) +
            c[idx + 2] * (3f * omt * t2) +
            c[idx + 3] * (t2 * t);
    }

    public static float2 GetTangent(NativeArray<float2> c, float t) {
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

    public static float2 GetTangentAt(NativeArray<float2> c, float t, int idx) {
        float omt = 1f - t;
        float omt2 = omt * omt;
        float t2 = t * t;
        return math.normalize(
            c[idx + 0] * (-omt2) +
            c[idx + 1] * (3f * omt2 - 2f * omt) +
            c[idx + 2] * (-3f * t2 + 2f * t) +
            c[idx + 3] * (t2)
        );
    }

    public static float2 GetNormal(NativeArray<float2> c, float t) {
        float2 tangent = math.normalize(GetTangent(c, t));
        return new float2(-tangent.y, tangent.x);
    }

    public static float2 GetNormalAt(NativeArray<float2> c, float t, int idx) {
        float2 tangent = math.normalize(GetTangentAt(c, t, idx));
        return new float2(-tangent.y, tangent.x);
    }

    public static float GetLength(NativeArray<float2> c, int steps) {
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

    public static float GetLength(NativeArray<float2> c, int steps, float t) {
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
}

public static class BDC3Quad {
    public static float Length(float3 v) {
        return math.sqrt(v.x * v.x + v.y * v.y + v.z * v.z);
    }

    public static float3 Lerp(float3 a, float3 b, float t) {
        return t * a + (1f - t) * b;
    }
    public static float3 EvaluateCasteljau(float3 a, float3 b, float3 c, float t) {
        return Lerp(Lerp(a, b, t), Lerp(b, c, t), t);
    }

    public static float3 Evaluate(float3 a, float3 b, float3 c, float t) {
        float3 u = 1f - t;
        return u * u * a + 2f * t * u * b + t * t * c;
    }

    public static float3 EvaluateNormalApprox(float3 a, float3 b, float3 c, float t) {
        const float EPS = 0.001f;

        float3 p0 = Evaluate(a, b, c, t - EPS);
        float3 p1 = Evaluate(a, b, c, t + EPS);

        return math.cross(new float3(0, 0, 1), math.normalize(p1 - p0));
    }

    public static float LengthEuclidApprox(float3 a, float3 b, float3 c, int steps) {
        float dist = 0;

        float3 pPrev = Evaluate(a, b, c, 0f);
        for (int i = 1; i <= steps; i++) {
            float t = i / (float)steps;
            float3 p = Evaluate(a, b, c, t);
            dist += Length(p - pPrev);
            pPrev = p;
        }

        return dist;
    }

    public static float LengthEuclidApprox(float3 a, float3 b, float3 c, int steps, float t) {
        float dist = 0;

        float3 pPrev = Evaluate(a, b, c, 0f);
        for (int i = 1; i <= steps; i++) {
            float tNow = t * (i / (float)steps);
            float3 p = Evaluate(a, b, c, tNow);
            dist += Length(p - pPrev);
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
    public static void CacheDistances(float3 a, float3 b, float3 c, NativeArray<float> outDistances) {
        float dist = 0;
        outDistances[0] = 0f;
        float3 pPrev = Evaluate(a, b, c, 0f); // Todo: this is just point a
        for (int i = 1; i < outDistances.Length; i++) {
            float t = i / (float)(outDistances.Length - 1);
            float3 p = Evaluate(a, b, c, t);
            dist += Length(p - pPrev);
            outDistances[i] = dist;
            pPrev = p;
        }
    }
}


public static class BDC2 {
    public static float Length(float2 v) {
        return math.sqrt(v.x * v.x + v.y * v.y);
    }

    public static float2 Lerp(float2 a, float2 b, float t) {
        return t * a + (1f - t) * b;
    }
    public static float2 EvaluateWithLerp(float2 a, float2 b, float2 c, float t) {
        return Lerp(Lerp(a, b, t), Lerp(b, c, t), t);
    }

    public static float2 Evaluate(float2 a, float2 b, float2 c, float t) {
        float2 u = 1f - t;
        return u * u * a + 2f * t * u * b + t * t * c;
    }

    public static float LengthEuclidean(float2 a, float2 b, float2 c, int steps) {
        float dist = 0;

        float2 pPrev = BDC2.Evaluate(a, b, c, 0f);
        for (int i = 1; i <= steps; i++) {
            float t = i / (float)steps;
            float2 p = BDC2.Evaluate(a, b, c, t);
            dist += Length(p - pPrev);
            pPrev = p;
        }

        return dist;
    }
}