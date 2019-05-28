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
    Todo:

    - In this system as well: fix the error where we're first dividing by w and
    then interpolating. We need to interpolate and perspective divide the resulting
    point.

    - Make splines out of curves
    - Make oriented patches out of curves
    - When part of a spline falls outside of frustum, it needs to be cut off, or culled?
        - Maybe we don't need to do that, only generate strokes on geometry that lies
        within view in the first place.

    make compositions

    Render grandfather's painting

    Explore the generalized notion of the B-Spline Curve
 */

public class Modeler : MonoBehaviour {
    [SerializeField] private Painter _painter;

    private Camera _camera;

    private NativeArray<float3> _controls;
    private NativeArray<float3> _projectedControls;
    private NativeArray<float3> _colors;

    private Rng _rng;

    private const int NUM_CURVES = 8;
    private const int CONTROLS_PER_CURVE = 4;

    private void Awake() {
        _camera = gameObject.GetComponent<Camera>();
        _camera.enabled = true;

        _controls = new NativeArray<float3>(NUM_CURVES * CONTROLS_PER_CURVE, Allocator.Persistent);

        _projectedControls = new NativeArray<float3>(NUM_CURVES * CONTROLS_PER_CURVE, Allocator.Persistent);
        _colors = new NativeArray<float3>(NUM_CURVES, Allocator.Persistent);

        _rng = new Rng(1234);
    }

    private void OnDestroy() {
        _controls.Dispose();
        _projectedControls.Dispose();
        _colors.Dispose();
    }

    private void Start() {
        _painter.Init(NUM_CURVES);
    }

    private void Update() {
        if (Time.frameCount % 2 == 0) {
            Paint();
        }
    }

    private void Paint() {
        var h = new JobHandle();

        _painter.Clear();

        var cj = new GenerateCurvesJob();
        cj.time = Time.frameCount * 0.01f;
        cj.rng = new Rng((uint)_rng.NextInt());
        cj.controlPoints = _controls;
        cj.colors = _colors;
        h = cj.Schedule();

        var pj = new ProjectCurvesJob();
        pj.mat = _camera.projectionMatrix * _camera.worldToCameraMatrix;
        pj.controlPoints = _controls;
        pj.projectedControls = _projectedControls;
        h = pj.Schedule(h);

        h.Complete();

        _painter.Draw(_projectedControls, _colors);
    }

    private void OnDrawGizmos() {
        if (!Application.isPlaying) {
            return;
        }
        
        Draw3dSplines();
    }

    private void Draw3dSplines() {
        Gizmos.color = Color.white;
        for (int c = 0; c < NUM_CURVES; c++) {
            float3 pPrev = BDCCubic3d.GetAt(_controls, 0f, c);
            Gizmos.DrawSphere(pPrev, 0.01f);
            int steps = 8;
            for (int i = 1; i <= steps; i++) {
                float t = i / (float)(steps);
                float3 p = BDCCubic3d.GetAt(_controls, t, c);
                float3 tg = BDCCubic3d.GetTangentAt(_controls, t, c);
                float3 n = BDCCubic3d.GetNormalAt(_controls, t, new float3(0, 1, 0), c);
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
    }

    private static float3 ToFloat3(in float2 v) {
        return new float3(v.x, v.y, 0f);
    }

    private struct GenerateCurvesJob : IJob {
        public Rng rng;

        [NativeDisableParallelForRestriction] public NativeArray<float3> controlPoints;
        [NativeDisableParallelForRestriction] public NativeArray<float3> colors;
        public float time;

        public void Execute() {
            int numCurves = controlPoints.Length / CONTROLS_PER_CURVE;
            rng.InitState(1234);

            for (int c = 0; c < numCurves; c++) {
                var p = new float3(c * 2f,0f, 3f);

                for (int j = 0; j < CONTROLS_PER_CURVE; j++) {
                    int idx = c * CONTROLS_PER_CURVE + j;
                    // p += new float3(0.5f * j, 1f, 0.5f);
                    p += new float3(rng.NextFloat2(), 0f);

                    controlPoints[idx] = p;
                }

                colors[c] = rng.NextFloat3();
            }
        }
    }

    private struct ProjectCurvesJob : IJob {
        [ReadOnly] public float4x4 mat;
        [ReadOnly] public NativeArray<float3> controlPoints;
        [WriteOnly] public NativeArray<float3> projectedControls;

        public void Execute() {
            for (int i = 0; i < controlPoints.Length; i++) {
                float4 p = new float4(controlPoints[i], 1f);
                p = math.mul(mat, p);
                projectedControls[i] = new float3(p.x, p.y, p.w);
            }
        }
    }
}