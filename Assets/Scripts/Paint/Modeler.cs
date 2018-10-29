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

    projection is off:
    - something that should be directly in the middle of the screen is not
    - projection still seems isometric

    Culling

    We might specify color and with only at the begin and end of a stroke.

    make compositions

    Render grandfather's painting

    Make it such that a spline path can be arbitary length. Possibly gets multi-stroke if longer than x.
    Then we can start generating some Gogh-likes by spiraling splines around attractors.
 */

public class Modeler : MonoBehaviour {
    [SerializeField] private Painter _painter;

    private Camera _camera;

    private NativeArray<float3> _controls;
    
    private NativeArray<float> _widths;

    private NativeArray<float2> _projectedControls;
    private NativeArray<float> _projectedWidths;
    private NativeArray<float3> _colors;

    private Rng _rng;

    private const int NUM_CURVES = 1;
    private const int CONTROLS_PER_CURVE = 4;

    private void Awake() {
        _camera = gameObject.GetComponent<Camera>();
        _camera.enabled = true;

        _controls = new NativeArray<float3>(NUM_CURVES * CONTROLS_PER_CURVE, Allocator.Persistent);
        _widths = new NativeArray<float>(NUM_CURVES, Allocator.Persistent);

        _projectedControls = new NativeArray<float2>(NUM_CURVES * CONTROLS_PER_CURVE, Allocator.Persistent);
        _projectedWidths = new NativeArray<float>(NUM_CURVES, Allocator.Persistent);
        _colors = new NativeArray<float3>(NUM_CURVES, Allocator.Persistent);

        _rng = new Rng(1234);
    }

    private void Start() {
        _painter.Init(NUM_CURVES);
    }

    private void OnDestroy() {
        _controls.Dispose();
        _widths.Dispose();

        _projectedControls.Dispose();
        _projectedWidths.Dispose();
        _colors.Dispose();
    }

    private void Update() {
        var h = new JobHandle();

        _painter.Clear();

        var cj = new GenerateSpiralJob();
        cj.time = Time.frameCount * 0.01f;
        cj.rng = new Rng((uint)_rng.NextInt());
        cj.controlPoints = _controls;
        cj.widths = _widths;
        cj.colors = _colors;
        h = cj.Schedule();

        var pj = new ProjectCurvesJob();
        pj.worldToCamMatrix = _camera.worldToCameraMatrix;
        pj.projectionMatrix = _camera.projectionMatrix;
        pj.controlPoints = _controls;
        pj.widths = _widths;
        pj.projectedControls = _projectedControls;
        pj.projectedWidths = _projectedWidths;
        h = pj.Schedule(h);

        h.Complete();

        // Project();

        _painter.Draw(_projectedControls, _projectedWidths, _colors);
    }

    public void Project() {
        int numCurves = _controls.Length / CONTROLS_PER_CURVE;
        for (int i = 0; i < numCurves; i++) {
            float avgZ = 0;
            for (int j = 0; j < CONTROLS_PER_CURVE; j++) {
                int idx = i * CONTROLS_PER_CURVE + j;
                float3 p = _camera.WorldToViewportPoint(_controls[idx]);
                _projectedControls[idx] = new float2(p.x, p.y) / p.z;

                avgZ += p.z;
            }
            avgZ *= 0.25f;
            _projectedWidths[i] = _widths[i] / avgZ;
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
        float3 pPrev = BDCCubic3d.Get(_controls, 0f);
        Gizmos.DrawSphere(pPrev, 0.01f);
        int steps = 16;
        for (int i = 1; i <= steps; i++) {
            float t = i / (float)(steps);
            float3 p = BDCCubic3d.Get(_controls, t);
            float3 tg = BDCCubic3d.GetTangent(_controls, t);
            float3 n = BDCCubic3d.GetNormal(_controls, t, new float3(0,1,0));
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
        float3 pPrev = ToFloat3(BDCCubic2d.Get(_projectedControls, 0f));
        Gizmos.DrawSphere(pPrev, 0.01f);
        int steps = 16;
        for (int i = 1; i <= steps; i++) {
            float t = i / (float)(steps);
            float3 p = ToFloat3(BDCCubic2d.Get(_projectedControls, t));
            float3 tg = ToFloat3(BDCCubic2d.GetTangent(_projectedControls, t));
            float3 n = ToFloat3(BDCCubic2d.GetNormal(_projectedControls, t));
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

    private struct GenerateSpiralJob : IJob {
        public Rng rng;

        [NativeDisableParallelForRestriction] public NativeArray<float3> controlPoints;
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
                        math.cos(time * 1.5f + idx * 1f) * 5f,
                        math.sin(time * 1.5f + idx * 1f) * 5f,
                        0.0f * idx);
                    controlPoints[idx] = p;
                }

                widths[i] = 1f;
                colors[i] = new float3(0.5f) + 0.5f * new float3(i * 0.07f % 1f, i * 0.063f % 1f, i * 0.047f % 1f);
            }
        }
    }

    private struct ProjectCurvesJob : IJob {
        [ReadOnly] public float4x4 worldToCamMatrix;
        [ReadOnly] public float4x4 projectionMatrix;

        [ReadOnly] public NativeArray<float3> controlPoints;
        [ReadOnly] public NativeArray<float> widths;

        public NativeArray<float2> projectedControls;
        public NativeArray<float> projectedWidths;

        public void Execute() {
            int numCurves = controlPoints.Length / CONTROLS_PER_CURVE;
            for (int i = 0; i < numCurves; i++) {
                float avgZ = 0;
                bool cull = false;
                for (int j = 0; j < CONTROLS_PER_CURVE; j++) {
                    int idx = i * CONTROLS_PER_CURVE + j;

                    float4 p = new float4(controlPoints[idx].x, controlPoints[idx].y, controlPoints[idx].z, 1);
                    p = WorldToScreenPoint(p, projectionMatrix, worldToCamMatrix);
                    projectedControls[idx] = new float2(p.x, p.y);

                    avgZ += p.z;

                    if (BDCCubic3d.GetNormalAt(controlPoints, j / (float)(CONTROLS_PER_CURVE-1), new float3(0,1,0), i).z > 0f) {
                        cull = true;
                    }
                }
                avgZ *= 0.25f;

                projectedWidths[i] = widths[i] / avgZ;
                
                // if (cull) {
                //     projectedWidths[i] = 0;
                // } else {
                //     projectedWidths[i] = widths[i] / avgZ;
                // }
                
            }
        }

        private static float4 WorldToScreenPoint(float4 wp, float4x4 projectionMatrix, float4x4 worldToCameraMatrix) {
            // calculate view-projection matrix
            float4x4 mat = math.mul(projectionMatrix, worldToCameraMatrix);

            // multiply world point by VP matrix
            Vector4 temp = math.mul(mat, wp);

            if (temp.w == 0f) {
                // point is exactly on camera focus point, screen point is undefined
                // unity handles this by returning 0,0,0
                return new float4();
            } else {
                // convert x and y from clip space to window coordinates
                temp.x = (temp.x / temp.w);
                temp.y = (temp.y / temp.w );
                return new float4(temp.x, temp.y, wp.z, 1);
            }
        }
    }
}