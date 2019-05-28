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
    [SerializeField] private Surface _surface;
    [SerializeField] private Painter _painter;

    private Camera _camera;

    private NativeArray<float3> _controls;
    private NativeArray<float3> _projectedControls;
    private NativeArray<float3> _colors;

    private Rng _rng;

    private void Awake() {
        _camera = gameObject.GetComponent<Camera>();
        _camera.enabled = true;

        _rng = new Rng(1234);
    }

    private void OnDestroy() {
        _controls.Dispose();
        _projectedControls.Dispose();
        _colors.Dispose();
    }

    private void Start() {
        const int numStrokes = 16;
        _controls = new NativeArray<float3>(numStrokes * BDCCubic3d.NUM_POINTS, Allocator.Persistent);
        _projectedControls = new NativeArray<float3>(_controls.Length, Allocator.Persistent);
        _colors = new NativeArray<float3>(numStrokes, Allocator.Persistent);

        _painter.Init(numStrokes);
    }

    private void Update() {
        if (Time.frameCount % 2 == 0) {
            Paint();
        }
    }

    private void Paint() {
        var h = new JobHandle();
        
        CreateStrokesForSurface();

        _painter.Clear();

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
        var numCurves = _controls.Length / 4;
        for (int c = 0; c < numCurves; c++) {
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

    /*  Now this will be a tricky thing

        Given some surface patch, we want to generate brush
        strokes over it, satisfying some constraints.

        Constraints can be:

        - Should cover surface, leave no gaps
        - Have n gradations of lighting
        - Should follow this hashing pattern

        etc.

        Another function might want to:
        - Extract silhouette
        
    */

    private void CreateStrokesForSurface() {
        var points = _surface.Points;
        var numSurfaceCurves = _surface.Points.Length / 4;

        var surfacePoints = new NativeArray<float3>(numSurfaceCurves * BDCCubic3d.NUM_POINTS, Allocator.TempJob);
        Util.CopyToNative(points, surfacePoints);

        var j = new GenerateSurfaceStrokesJob();
        j.surfacePoints = surfacePoints;
        j.rng = new Rng((uint)_rng.NextInt());
        j.controlPoints = _controls;
        j.colors = _colors;
        j.Schedule().Complete();

        surfacePoints.Dispose();
    }

    private struct GenerateSurfaceStrokesJob : IJob {
        public Rng rng;

        [ReadOnly, NativeDisableParallelForRestriction] public NativeArray<float3> surfacePoints;

        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<float3> controlPoints;
        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<float3> colors;

        public void Execute() {
            int numTargetCurves = controlPoints.Length / BDCCubic3d.NUM_POINTS;

            rng.InitState(1234);

            var tempCurve = new NativeArray<float3>(4, Allocator.Temp, NativeArrayOptions.ClearMemory);

            // Todo: Instead of GetAt(points), we may simply Get(points.Slice)
            for (int c = 0; c < numTargetCurves; c++) {
                float tVert = c / (float)(numTargetCurves-1);

                tempCurve[0] = BDCCubic3d.GetAt(surfacePoints, tVert, 0);
                tempCurve[1] = BDCCubic3d.GetAt(surfacePoints, tVert, 1);
                tempCurve[2] = BDCCubic3d.GetAt(surfacePoints, tVert, 2);
                tempCurve[3] = BDCCubic3d.GetAt(surfacePoints, tVert, 3);

                for (int j = 0; j < BDCCubic3d.NUM_POINTS; j++) {
                    float tHori = j / (float)(BDCCubic3d.NUM_POINTS - 1);
                    
                    var p = BDCCubic3d.Get(tempCurve, tHori);
                    controlPoints[c * BDCCubic3d.NUM_POINTS + j] = p;
                }

                colors[c] = rng.NextFloat3();
            }

            tempCurve.Dispose();
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