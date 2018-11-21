using UnityEngine;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Rng = Unity.Mathematics.Random;
using Ramjet;
using UnityEngine.Rendering;
using System.Runtime.InteropServices;

// Drawing a circle approximation with 4 quadratic rational beziers.
// Note: they could share vertex data

public class RationalBezier : MonoBehaviour {
    [SerializeField] private Camera _camera;
    private NativeArray<float2> _curve2d;
    private NativeArray<float> _weights;
    private Rng _rng;

    private const int NUMCURVES = 4;
    private const int CONTROLS_PER_CURVE = 3;

    private void Awake() {
        _curve2d = new NativeArray<float2>(NUMCURVES * CONTROLS_PER_CURVE, Allocator.Persistent);
        _weights = new NativeArray<float>(NUMCURVES * CONTROLS_PER_CURVE, Allocator.Persistent);
        _rng = new Rng(1234);

        GenerateCurve();
    }

    private void GenerateCurve() {
        _curve2d[0] = new float2(1f, 0f);
        _curve2d[1] = new float2(1f, 1f);
        _curve2d[2] = new float2(0f, 1f);

        _curve2d[3] = new float2(0f, 1f);
        _curve2d[4] = new float2(-1f, 1f);
        _curve2d[5] = new float2(-1f, 0f);

        _curve2d[6] = new float2(-1f, 0f);
        _curve2d[7] = new float2(-1f, -1f);
        _curve2d[8] = new float2(0f, -1f);

        _curve2d[9] = new float2(0f, -1f);
        _curve2d[10] = new float2(1f, -1f);
        _curve2d[11] = new float2(1f, 0f);

        float w = 1f;//0.707f;//1f / math.sqrt(2f);

        _weights[0] = 1;
        _weights[1] = w;
        _weights[2] = 1;
        _weights[3] = 1;
        _weights[4] = w;
        _weights[5] = 1;
        _weights[6] = 1;
        _weights[7] = w;
        _weights[8] = 1;
        _weights[9] = 1;
        _weights[10] = w;
        _weights[11] = 1;
    }

    private void OnDestroy() {
        _weights.Dispose();
        _curve2d.Dispose();
    }

    private JobHandle _handle;

    private void Update() {
       
    }

    private void OnDrawGizmos() {
        if (!Application.isPlaying) {
            return;
        }

        Draw2dCurve();
    }


    private void Draw2dCurve() {
        Gizmos.color = Color.blue;
        for (int i = 0; i < _curve2d.Length; i++) {
            Gizmos.DrawSphere(Math.ToVec3(_curve2d[i]), 0.05f);
        }

        for (int i = 0; i < 4; i++) {
            Gizmos.color = Color.white;
            var pPrev = Math.ToVec3(BDCQuadratic2d.GetAt(_curve2d, _weights, 0f, i));
            Gizmos.DrawSphere(pPrev, 0.01f);
            int steps = 16;
            for (int j = 1; j <= steps; j++) {
                float t = j / (float)(steps);
                var p = Math.ToVec3(BDCQuadratic2d.GetAt(_curve2d, _weights, t, i), 0f);
                Gizmos.DrawLine(pPrev, p);
                Gizmos.DrawSphere(p, 0.01f);

                pPrev = p;
            }
        }
        
    }
}