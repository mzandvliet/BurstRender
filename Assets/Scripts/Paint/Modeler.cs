using UnityEngine;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Rng = Unity.Mathematics.Random;
using Ramjet;
using UnityEngine.Rendering;
using System.Runtime.InteropServices;

public class Modeler : MonoBehaviour {
    [SerializeField] private Painter _painter;

    private Camera _camera;

    private NativeArray<float3> _curves;
    private NativeArray<float2> _projectedCurves;
    private NativeArray<float> _widths;
    private NativeArray<float3> _colors;
    private Rng _rng;

    private const int NUM_CURVES = 8;
    private const int CONTROLS_PER_CURVE = 4;

    private void Awake() {
        _camera = gameObject.GetComponent<Camera>();
        _camera.enabled = true;

        _curves = new NativeArray<float3>(NUM_CURVES * CONTROLS_PER_CURVE, Allocator.Persistent);
        _projectedCurves = new NativeArray<float2>(NUM_CURVES * CONTROLS_PER_CURVE, Allocator.Persistent);
        _widths = new NativeArray<float>(NUM_CURVES, Allocator.Persistent);
        _colors = new NativeArray<float3>(NUM_CURVES, Allocator.Persistent);

        _rng = new Rng(1234);

        var cj = new GenerateCurveJob();
        cj.curveIdx = 0;
        cj.rng = new Rng((uint)_rng.NextInt());
        cj.curves = _curves;
        cj.widths = _widths;
        cj.colors = _colors;
        cj.Schedule().Complete();
    }

    private void Start() {
        _painter.Init(NUM_CURVES);
    }

    private void OnDestroy() {
        _curves.Dispose();
        _projectedCurves.Dispose();
        _colors.Dispose();
        _widths.Dispose();
    }

    private void Update() {
        var h = new JobHandle();

        if (Time.frameCount % 60 == 0) {
            _painter.Clear();
        }

        if (Time.frameCount % 5 == 0) {
            var cj = new GenerateCurveJob();
            cj.curveIdx = 0;
            cj.rng = new Rng((uint)_rng.NextInt());
            cj.curves = _curves;
            cj.widths = _widths;
            cj.colors = _colors;
            h = cj.Schedule();

            var pj = new ProjectCurvesJob();
            pj.curveIdx = 0;
            pj.projectionMatrix = _camera.projectionMatrix * _camera.worldToCameraMatrix;
            pj.curves = _curves;
            pj.projectedCurves = _projectedCurves;
            pj.widths = _widths;
            h = pj.Schedule(h);

            h.Complete();

            _painter.Draw(_projectedCurves, _widths, _colors);
        }
    }

    private void OnDrawGizmos() {
        if (!Application.isPlaying) {
            return;
        }
        
        Draw3dSplines();
        DrawProjectedSplines();
    }

    private void Draw3dSplines() {
        Gizmos.color = Color.white;
        float3 pPrev = BDCCubic3d.Get(_curves, 0f);
        Gizmos.DrawSphere(pPrev, 0.01f);
        int steps = 16;
        for (int i = 1; i <= steps; i++) {
            float t = i / (float)(steps);
            float3 p = BDCCubic3d.Get(_curves, t);
            float3 tg = BDCCubic3d.GetTangent(_curves, t);
            float3 n = BDCCubic3d.GetNormal(_curves, t, new float3(0,1,0));
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

    private void DrawProjectedSplines() {
        Gizmos.color = Color.white;
        float3 pPrev = ToFloat3(BDCCubic2d.Get(_projectedCurves, 0f));
        Gizmos.DrawSphere(pPrev, 0.01f);
        int steps = 16;
        for (int i = 1; i <= steps; i++) {
            float t = i / (float)(steps);
            float3 p = ToFloat3(BDCCubic2d.Get(_projectedCurves, t));
            float3 tg = ToFloat3(BDCCubic2d.GetTangent(_projectedCurves, t));
            float3 n = ToFloat3(BDCCubic2d.GetNormal(_projectedCurves, t));
            Gizmos.DrawLine(pPrev, p);
            Gizmos.DrawSphere(p, 0.03f);

            Gizmos.color = Color.blue;
            Gizmos.DrawRay(p, n * 0.3f);
            Gizmos.DrawRay(p, -n * 0.3f);
            Gizmos.color = Color.green;
            Gizmos.DrawRay(p, tg);

            pPrev = p;
        }
    }

    private static float3 ToFloat3(in float2 v) {
        return new float3(v.x, v.y, 0f);
    }

    private struct GenerateCurveJob : IJob {
        public Rng rng;
        public int curveIdx;

        [NativeDisableParallelForRestriction] public NativeArray<float3> curves;
        [NativeDisableParallelForRestriction] public NativeArray<float> widths;
        [NativeDisableParallelForRestriction] public NativeArray<float3> colors;

        public void Execute() {
            float3 p = new float3(rng.NextFloat(1f, 9f), rng.NextFloat(0.5f, 2f), rng.NextFloat(5, 10f));
            for (int j = 0; j < CONTROLS_PER_CURVE; j++) {
                curves[curveIdx * CONTROLS_PER_CURVE + j] = p;
                p += rng.NextFloat3Direction() * 1;
                
            }

            widths[curveIdx] = 0.5f;
            colors[curveIdx] = new float3(rng.NextFloat(0.3f, 0.5f), rng.NextFloat(0.6f, 0.8f), rng.NextFloat(0.3f, 0.6f));
        }
    }

    private struct ProjectCurvesJob : IJob {
        [ReadOnly] public int curveIdx;
        [ReadOnly] public float4x4 projectionMatrix;

        [ReadOnly] public NativeArray<float3> curves;
        [ReadOnly] public NativeArray<float> widths;

        public NativeArray<float2> projectedCurves;

        public void Execute() {
            for (int i = 0; i < curves.Length; i++) {
                float4 p = new float4(curves[i].x,curves[i].y,curves[i].z, 1);
                p = math.mul(projectionMatrix, p);
                projectedCurves[i] = new float2(p.x, p.y);
            }

            // Todo: z-based scaling. 1/z, etc.
            // widths[curveIdx] = 0.5f; 
        }
    }
}