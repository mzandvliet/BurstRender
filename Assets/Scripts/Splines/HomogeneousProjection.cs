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
    Edit: Note that all of the below code is wrong, and I didn't know what I was doing

    Proper steps:

    - Premultiply cameraProject * cameraView matrices
    - Formulate all 3d-space control points as homogeneous, tagging on w = 1
    - Project the 4d points using mat.
    - Make 3d vecs [x,y,w], evaluate that as 3d bezier
    - Resulting 2d screen point is [x/w, y/w]
    - Render that point sequence
    - Can later also store Z for rendering depth-aware stroke effects

    A proper test for verification:
    - Render 3d line using 3d piecewise linear line drawing
    - Rendering projected rational using 2 piecewise linear line drawin
    These two approaches should overlap, be approx the same render.

    If you want to do things like create a perfect 3d sphere out of 8 triangular/
    quadratic bezier patches, each of those needs to be rational, meaning it will
    have [x,y,z,w] with non-unit w values for the middle control points. Note
    additionally that you *could* construct those by first projecting a 5d
    regular patch into 4d rational patch, which you can then further project into
    a 2d rational, after which you divide b z in order to get screen-space point?

    Hah.
 */

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

    private void Generate3dCurve() {
        float3 p = new float3(0, 0, 5);
        for (int i = 0; i < _curve3d.Length; i++) {
            _curve3d[i] = p;
            p += _rng.NextFloat3Direction() * (2f / (float)(1 + i));
        }
    }

    private void ProjectCurve() {
        var worldToCam = (float4x4)_camera.worldToCameraMatrix; //wrong
        for (int i = 0; i < _curve3d.Length; i++) {
            var camPos = math.mul(worldToCam, new float4(_curve3d[i].x, _curve3d[i].y, _curve3d[i].z, 1f));
            _curve2d[i] = new float2(camPos.x, camPos.y) / camPos.z; // wrong, save divide until last
        }
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