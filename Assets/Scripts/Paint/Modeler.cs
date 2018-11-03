using UnityEngine;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Rng = Unity.Mathematics.Random;
using Ramjet;
using UnityEngine.Rendering;
using System.Runtime.InteropServices;

using Curve2d = Unity.Collections.NativeArray<Unity.Mathematics.float2>;
using Curve3d = Unity.Collections.NativeArray<Unity.Mathematics.float3>;

/* 
    Todo:

    - Make splines out of curves
    - Make oriented patches out of curves
    - When part of a spline falls outside of frustum, it needs to be cut off, or culled?
        - Maybe we don't need to do that, only generate strokes on geometry that lies
        within view in the first place.

    make compositions

    Render grandfather's painting
 */

public class Modeler : MonoBehaviour {
    [SerializeField] private Painter _painter;

    private Camera _camera;

    private Curve3d _controls;
    
    private NativeArray<float> _widths;

    private Curve2d _projectedControls;
    private NativeArray<float> _projectedWidths;
    private NativeArray<float3> _colors;

    private Rng _rng;

    private const int NUM_CURVES = 128;
    private const int CONTROLS_PER_CURVE = 4;

    private void Awake() {
        _camera = gameObject.GetComponent<Camera>();
        _camera.enabled = true;

        _controls = new Curve3d(NUM_CURVES * CONTROLS_PER_CURVE, Allocator.Persistent);
        _widths = new NativeArray<float>(NUM_CURVES, Allocator.Persistent);

        _projectedControls = new Curve2d(NUM_CURVES * CONTROLS_PER_CURVE, Allocator.Persistent);
        _projectedWidths = new NativeArray<float>(NUM_CURVES, Allocator.Persistent);
        _colors = new NativeArray<float3>(NUM_CURVES, Allocator.Persistent);

        _rng = new Rng(1234);
    }

    private void OnDestroy() {
        _controls.Dispose();
        _widths.Dispose();

        _projectedControls.Dispose();
        _projectedWidths.Dispose();
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

        //_painter.Clear();

        var cj = new GenerateSpiralJob();
        cj.time = Time.frameCount * 0.01f;
        cj.rng = new Rng((uint)_rng.NextInt());
        cj.controlPoints = _controls;
        cj.widths = _widths;
        cj.colors = _colors;
        h = cj.Schedule();

        var pj = new ProjectCurvesJob();
        pj.mat = math.mul(_camera.projectionMatrix, _camera.worldToCameraMatrix);
        pj.controlPoints = _controls;
        pj.widths = _widths;
        pj.projectedControls = _projectedControls;
        pj.projectedWidths = _projectedWidths;
        h = pj.Schedule(h);

        h.Complete();

      

        _painter.Draw(_projectedControls, _projectedWidths, _colors);
    }

    private static void GenerateRandomStrokes(NativeArray<float2> controls, NativeArray<float> widths, NativeArray<float3> colors, ref Rng rng) {
        for (int i = 0; i < controls.Length / 4; i++) {
            SampleCurve(controls, i * 4, ref rng);
            widths[i] = math.max(2f - Time.frameCount * 0.001f, 0.04f);
            colors[i] = rng.NextFloat3();
        }
    }

    private static void SampleCurve(NativeArray<float2> c, int idx, ref Rng rng) {
        c[idx + 0] = new float2((-5f + Time.time * 5f % 15f) + rng.NextFloat(), 5f - (Time.time * 10f % 5f) + rng.NextFloat());
        c[idx + 1] = c[idx + 0] + new float2(rng.NextFloat(-1, 1f), rng.NextFloat(-0f, 2f));
        c[idx + 2] = c[idx + 1] + new float2(rng.NextFloat(-1, 1f), rng.NextFloat(-0f, 2f));
        c[idx + 3] = c[idx + 2] + new float2(rng.NextFloat(-1, 1f), rng.NextFloat(-0f, 2f));
    }

    private void OnDrawGizmos() {
        if (!Application.isPlaying) {
            return;
        }
        
        Draw3dSplines();
        // DrawProjectedSplines();
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

                // Gizmos.color = Color.blue;
                // Gizmos.DrawRay(p, n * 0.3f);
                // Gizmos.DrawRay(p, -n * 0.3f);
                // Gizmos.color = Color.green;
                // Gizmos.DrawRay(p, tg);

                pPrev = p;
            }    
        }
    }

    private void DrawProjectedSplines() {
        for (int c = 0; c < NUM_CURVES; c++) {
            Gizmos.color = Color.white;
            float3 pPrev = ToFloat3(BDCCubic2d.GetAt(_projectedControls, 0f, c));
            Gizmos.DrawSphere(pPrev, 0.01f);
            int steps = 16;
            for (int i = 1; i <= steps; i++) {
                float t = i / (float)(steps);
                float3 p = ToFloat3(BDCCubic2d.GetAt(_projectedControls, t, c));
                float3 tg = ToFloat3(BDCCubic2d.GetTangentAt(_projectedControls, t, c));
                float3 n = ToFloat3(BDCCubic2d.GetNormalAt(_projectedControls, t, c));
                Gizmos.DrawLine(pPrev, p);
                Gizmos.DrawSphere(p, 0.03f);

                // Gizmos.color = Color.blue;
                // Gizmos.DrawRay(p, n * 0.3f);
                // Gizmos.DrawRay(p, -n * 0.3f);
                // Gizmos.color = Color.green;
                // Gizmos.DrawRay(p, tg);

                pPrev = p;
            }
        }
        
    }

    private static float3 ToFloat3(in float2 v) {
        return new float3(v.x, v.y, 0f);
    }

    private struct GenerateSpiralJob : IJob {
        public Rng rng;

        [NativeDisableParallelForRestriction] public Curve3d controlPoints;
        [NativeDisableParallelForRestriction] public NativeArray<float> widths;
        [NativeDisableParallelForRestriction] public NativeArray<float3> colors;
        public float time;

        public void Execute() {
            int numCurves = controlPoints.Length / CONTROLS_PER_CURVE;

            float3 o = new float3(0, 0, 5);

            for (int i = 0; i < numCurves; i++) {
                for (int j = 0; j < CONTROLS_PER_CURVE; j++) {
                    int idx = i * CONTROLS_PER_CURVE + j;

                    var p = o + new float3(
                        math.cos(time * 1.5f + idx * 1f) * 10f,
                        math.sin(time * 1.5f + idx * 1f) * 10f,
                        0.1f * idx);

                    controlPoints[idx] = p;
                }

                widths[i] = rng.NextFloat();
                widths[i] *= widths[i];
                colors[i] = new float3(0.5f) + 0.5f * new float3(i * 0.07f % 1f, i * 0.063f % 1f, i * 0.047f % 1f);
            }
        }
    }

    private struct ProjectCurvesJob : IJob {
        [ReadOnly] public float4x4 mat;

        [ReadOnly] public Curve3d controlPoints;
        [ReadOnly] public NativeArray<float> widths;

        public Curve2d projectedControls;
        public NativeArray<float> projectedWidths;

        public void Execute() {
            int numCurves = controlPoints.Length / CONTROLS_PER_CURVE;
            for (int c = 0; c < numCurves; c++) {
                float avgZ = 0;
                // bool cull = false;
                for (int j = 0; j < CONTROLS_PER_CURVE; j++) {
                    int idx = c * CONTROLS_PER_CURVE + j;

                    float4 p = new float4(controlPoints[idx].x, controlPoints[idx].y, controlPoints[idx].z, 1);
                    p = WorldToScreenPoint(p, mat);
                    projectedControls[idx] = new float2(p.x, p.y);

                    avgZ += p.z;

                    // if (BDCCubic3d.GetNormalAt(controlPoints, j / (float)(CONTROLS_PER_CURVE-1), new float3(0,1,0), c).z > 0f) {
                    //     cull = true;
                    // }
                }
                avgZ *= 0.25f;

                if (avgZ != 0f) {
                    projectedWidths[c] = widths[c] / avgZ;
                } else {
                    projectedWidths[c] = 0f;
                }
                
                
                // if (cull) {
                //     projectedWidths[i] = 0;
                // } else {
                //     projectedWidths[i] = widths[i] / avgZ;
                // }
                
            }
        }

        private static float4 WorldToScreenPoint(float4 p, float4x4 mat) {
            Vector4 temp = math.mul(mat, p);

            if (temp.w == 0f) {
                return new float4();
            } else {
                temp.x = (temp.x / temp.w);
                temp.y = (temp.y / temp.w );
                return new float4(temp.x, temp.y, p.z, 1);
            }
        }
    }
}