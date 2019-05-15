using UnityEngine;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Rng = Unity.Mathematics.Random;
using Ramjet;
using UnityEngine.Rendering;
using System.Runtime.InteropServices;

/*
    Drawing a circle with 4 quadratic rational bezier segments,
    initialized by hand.

    Todo: expand this with camera project, homogeneous coordinates
 */

public class RationalBezier : MonoBehaviour {
    [SerializeField] private Camera _camera;
    private NativeArray<float3> _curveRat2d;
    private Rng _rng;

    private const int NUMCURVES = 4;
    private const int CONTROLS_PER_CURVE = 3;

    private void Awake() {
        _curveRat2d = new NativeArray<float3>(NUMCURVES * CONTROLS_PER_CURVE, Allocator.Persistent);
        _rng = new Rng(1234);

        GenerateCurve();
    }

    private void GenerateCurve() {
        const float halfsqrt2 = 0.707107f;
        _curveRat2d[0] = new float3(1f, 0f, 1);
        _curveRat2d[1] = new float3(halfsqrt2, halfsqrt2, halfsqrt2);
        _curveRat2d[2] = new float3(0f, 1f, 1);

        _curveRat2d[3] = new float3(0f, 1f, 1);
        _curveRat2d[4] = new float3(-halfsqrt2, halfsqrt2, halfsqrt2);
        _curveRat2d[5] = new float3(-1f, 0f, 1);

        _curveRat2d[6] = new float3(-1f, 0f, 1);
        _curveRat2d[7] = new float3(-halfsqrt2, -halfsqrt2, halfsqrt2);
        _curveRat2d[8] = new float3(0f, -1f, 1);

        _curveRat2d[9] = new float3(0f, -1f, 1);
        _curveRat2d[10] = new float3(halfsqrt2, -halfsqrt2, halfsqrt2);
        _curveRat2d[11] = new float3(1f, 0f, 1);
    }

    private void OnDestroy() {
        _curveRat2d.Dispose();
    }

    private JobHandle _handle;

    private void OnDrawGizmos() {
        if (!Application.isPlaying) {
            return;
        }

        DrawRational2dCurve();
    }


    private void DrawRational2dCurve() {
        Gizmos.color = Color.blue;
        for (int i = 0; i < _curveRat2d.Length; i++) {
            var p = _curveRat2d[i];
            p = new Vector3(p.x / p.z, p.y / p.z, 1f);
            Gizmos.DrawSphere(p, 0.05f);
        }

        for (int i = 0; i < 4; i++) {
            Gizmos.color = Color.white;
            var pPrev = BDCQuadratic3d.Get(_curveRat2d[i*3+0], _curveRat2d[i * 3 + 1], _curveRat2d[i * 3 + 2], 0f);
            Gizmos.DrawSphere(pPrev, 0.01f);
            int steps = 16;
            for (int j = 1; j <= steps; j++) {
                float t = j / (float)(steps);
                var p = BDCQuadratic3d.Get(_curveRat2d[i * 3 + 0], _curveRat2d[i * 3 + 1], _curveRat2d[i * 3 + 2], t);
                p = new Vector3(p.x / p.z, p.y / p.z, 1f);
                Gizmos.DrawLine(pPrev, p);
                Gizmos.DrawSphere(p, 0.01f);

                pPrev = p;
            }
        }
        
    }
}