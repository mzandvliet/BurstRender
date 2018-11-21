using UnityEngine;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Rng = Unity.Mathematics.Random;
using Ramjet;
using UnityEngine.Rendering;
using System.Runtime.InteropServices;

// Hah, projection of the curves from 3d to 2d, at least isometrically, is trivial!
// Just project the control points and you're done.

public class HomogeneousProjection : MonoBehaviour {
    [SerializeField] private Camera _camera;
    private NativeArray<float3> _curve3d;
    private NativeArray<float2> _curve2d;
    private Rng _rng;

    private const int NUM_CURVES = 1;
    private const int CONTROLS_PER_CURVE = 4;

    private void Awake() {
        _curve3d = new NativeArray<float3>(NUM_CURVES * CONTROLS_PER_CURVE, Allocator.Persistent);
        _curve2d = new NativeArray<float2>(NUM_CURVES * CONTROLS_PER_CURVE, Allocator.Persistent);
        _rng = new Rng(1234);

        Generate3dCurve();
    }

    private void Generate3dCurve() {
        float3 p = new float3(0, 0, 5);
        for (int i = 0; i < _curve3d.Length; i++) {
            _curve3d[i] = p;
            p += _rng.NextFloat3Direction() * (2f / (float)(1 + i));
        }
    }

    private void ProjectCurve() {
        var worldToCam = (float4x4)_camera.worldToCameraMatrix;
        Debug.Log(worldToCam);
        for (int i = 0; i < _curve3d.Length; i++) {
            var camPos = math.mul(worldToCam, new float4(_curve3d[i].x,_curve3d[i].y, _curve3d[i].z, 1f));
            _curve2d[i] = new float2(camPos.x, camPos.y) / camPos.z;
        }
    }

    private void OnDestroy() {
        _curve3d.Dispose();
        _curve2d.Dispose();
    }

    private JobHandle _handle;

    private void Update() {
        if (Time.frameCount % (5 * 60) == 0) {
            Generate3dCurve();
        }
        ProjectCurve();
    }

    private void OnDrawGizmos() {
        if (!Application.isPlaying) {
            return;
        }

        Draw3dCurve();
        Draw2dCurve();
        DrawPerspectiveLines();
    }

    private void DrawPerspectiveLines() {
        Gizmos.color = Color.gray;
        int steps = 16;
        for (int i = 1; i <= steps; i++) {
            float t = i / (float)(steps);
            float3 p3 = BDCCubic3d.Get(_curve3d, t);
            // float2 p2d = BDCCubic2d.Get(_curve2d, t);
            // float3 p2 = new float3(p2d.x, p2d.y, 1f);
            float3 p2 = new float3(0f);
            Gizmos.DrawLine(p2, p3);
        }
    }

    private void Draw3dCurve() {
        Gizmos.color = Color.blue;
        for (int i = 0; i < _curve3d.Length; i++) {
            Gizmos.DrawSphere(_curve3d[i], 0.05f);
        }

        Gizmos.color = Color.white;
        float3 pPrev = BDCCubic3d.Get(_curve3d, 0f);
        Gizmos.DrawSphere(pPrev, 0.01f);
        int steps = 16;
        for (int i = 1; i <= steps; i++) {
            float t = i / (float)(steps);
            float3 p = BDCCubic3d.Get(_curve3d, t);
            float3 tg = BDCCubic3d.GetTangent(_curve3d, t);
            float3 n = BDCCubic3d.GetNormal(_curve3d, t, new float3(0, 1, 0));
            Gizmos.DrawLine(pPrev, p);
            Gizmos.DrawSphere(p, 0.01f);

            Gizmos.color = Color.blue;
            Gizmos.DrawRay(p, n * 0.3f);
            Gizmos.DrawRay(p, -n * 0.3f);
            Gizmos.color = Color.green;
            Gizmos.DrawRay(p, tg);

            pPrev = p;
        }
    }

    private void Draw2dCurve() {
        Gizmos.color = Color.blue;
        for (int i = 0; i < _curve2d.Length; i++) {
            Gizmos.DrawSphere(Math.ToVec3(_curve2d[i]), 0.05f);
        }

        Gizmos.color = Color.white;
        var pPrev = Math.ToVec3(BDCCubic2d.Get(_curve2d, 0f));
        Gizmos.DrawSphere(pPrev, 0.01f);
        int steps = 16;
        for (int i = 1; i <= steps; i++) {
            float t = i / (float)(steps-1);
            var p = Math.ToVec3(BDCCubic2d.Get(_curve2d, t), 1f);
            var tg = Math.ToVec3(BDCCubic2d.GetTangent(_curve2d, t));
            var n = Math.ToVec3(BDCCubic2d.GetNormal(_curve2d, t));
            Gizmos.DrawLine(pPrev, p);
            Gizmos.DrawSphere(p, 0.01f);

            // Gizmos.color = Color.blue;
            // Gizmos.DrawRay(p, n * 0.3f);
            // Gizmos.DrawRay(p, -n * 0.3f);
            // Gizmos.color = Color.green;
            // Gizmos.DrawRay(p, tg);

            pPrev = p;
        }
    }
}