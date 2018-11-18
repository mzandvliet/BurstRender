using UnityEngine;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Rng = Unity.Mathematics.Random;
using Ramjet;
using UnityEngine.Rendering;
using System.Runtime.InteropServices;

// Make in-game gizmos for editing splines
// Create, add points
// split, merge
// Make surface patch
// 3d (rational so we can do approximations of spherical curves), with 2d perspective projections

/*
    I need to decide on a preferred way to render these things. Tesselating and rasterization
    seems to be the way to go (unless...). But which parts do I choose to do on the cpu or gpu?

    I have some cpu-side testing data now, but I haven't tried anything on the gpu. Perhaps
    tesselation of the curves can be done there, too. It will help to have a small api for
    moving curve data to and from the gpu, anyway.

 */

public class SplineEditor : MonoBehaviour {
    [SerializeField] private Camera _camera;
    private NativeArray<float3> _curve3d;

    private NativeArray<float3> _left;
    private NativeArray<float3> _right;

    private Rng _rng;

    private const int CONTROLS_PER_CURVE = 4;

    private void Awake() {
        _curve3d = new NativeArray<float3>(CONTROLS_PER_CURVE, Allocator.Persistent);
        _left = new NativeArray<float3>(CONTROLS_PER_CURVE, Allocator.Persistent);
        _right = new NativeArray<float3>(CONTROLS_PER_CURVE, Allocator.Persistent);
        _rng = new Rng(1234);

        Update();
    }

    private void OnDestroy() {
        _curve3d.Dispose();
        _left.Dispose();
        _right.Dispose();
    }

    private JobHandle _handle;

    private void Update() {
        if (Time.frameCount % 240 == 0) {
            GenerateRandom3dCurve();
            float t = _rng.NextFloat(0.2f, 0.8f);
            BDCCubic3d.Split(_curve3d, t, _left, _right);
        }
    }

    private void OnDrawGizmos() {
        if (!Application.isPlaying) {
            return;
        }

        if (Time.frameCount % 60 > 30) {
            Draw3dCurve(_curve3d);
        } else {
            Draw3dCurve(_left);
            Draw3dCurve(_right);
        }
        
    }

    private void GenerateRandom3dCurve() {
        float3 p = new float3(0, 0, 5);
        for (int i = 0; i < _curve3d.Length; i++) {
            _curve3d[i] = p;
            p += _rng.NextFloat3Direction() * (2f / (float)(1 + i));
        }
    }

    private static void Draw3dCurve(NativeArray<float3> curve) {
        Gizmos.color = Color.blue;
        for (int i = 0; i < curve.Length; i++) {
            Gizmos.DrawSphere(curve[i], 0.05f);
            if (i > 0) {
                Gizmos.DrawLine(curve[i-1], curve[i]);
            }
        }

        Gizmos.color = Color.white;
        float3 pPrev = BDCCubic3d.Get(curve, 0f);
        Gizmos.DrawSphere(pPrev, 0.01f);
        int steps = 16;
        for (int i = 1; i <= steps; i++) {
            float t = i / (float)(steps);
            float3 p = BDCCubic3d.Get(curve, t);
            Gizmos.DrawLine(pPrev, p);
            Gizmos.DrawSphere(p, 0.01f);

            // float3 tg = BDCCubic3d.GetTangent(curve, t);
            // float3 n = BDCCubic3d.GetNormal(curve, t, new float3(0, 1, 0));
            // Gizmos.color = Color.blue;
            // Gizmos.DrawRay(p, n * 0.3f);
            // Gizmos.DrawRay(p, -n * 0.3f);
            // Gizmos.color = Color.green;
            // Gizmos.DrawRay(p, tg);

            pPrev = p;
        }
    }
}