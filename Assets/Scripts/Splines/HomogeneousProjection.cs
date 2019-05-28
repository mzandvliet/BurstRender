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
    Steps are roughly:

    - Premultiply cameraProject * cameraView matrices
    - Formulate all 3d-space control points as homogeneous, tagging on w = 1
    - Project the 4d points using mat.
    - Evaluate resulting rational 2d curve [x,y,w] as a 3d bezier
    - Resulting 2d screen point can be perspective divided: [x/w, y/w]
    - Render that point sequence using a linear line drawing algorithm
    - Can later also store Z for rendering depth-aware stroke effects

    A proper test for verification:
    - Render 3d line using 3d piecewise linear line drawing
    - Rendering projected rational using 2 piecewise linear line drawin
    These two approaches should overlap, be approx the same render.
    Edit: They do!

    If you want to do things like create a perfect 3d sphere out of 8 triangular/
    quadratic bezier patches, each of those needs to be rational, meaning it will
    have [x,y,z,w] with non-unit w values for the middle control points. Note
    additionally that you *could* construct those by first projecting a 5d
    regular patch into 4d rational patch, which you can then further project into
    a 2d rational, after which you divide b z in order to get screen-space point?

    Hah.
 */

public class HomogeneousProjection : MonoBehaviour {
    [SerializeField] private Material _lineMaterial;
    [SerializeField] private Camera _camera;
    private NativeArray<float4> _curve3dHom;
    private NativeArray<float3> _curve2dRat;
    private Rng _rng;

    private const int NUM_CURVES = 1;
    private const int CONTROLS_PER_CURVE = 4;

    private void Awake() {
        _curve3dHom = new NativeArray<float4>(NUM_CURVES * CONTROLS_PER_CURVE, Allocator.Persistent);
        _curve2dRat = new NativeArray<float3>(NUM_CURVES * CONTROLS_PER_CURVE, Allocator.Persistent);
        _rng = new Rng(1234);

        Generate3dCurve();
    }

    private void OnDestroy() {
        _curve3dHom.Dispose();
        _curve2dRat.Dispose();
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
        DrawPerspectiveLines();
    }

    private void Generate3dCurve() {
        float4 p = new float4(0, 0, 7, 1);
        for (int i = 0; i < _curve3dHom.Length; i++) {
            _curve3dHom[i] = p;
            p += new float4(_rng.NextFloat3Direction() * (4f / (float)(1 + i)), 1f);
        }
    }

    private void ProjectCurve() {
        var camProj = _camera.projectionMatrix;

        // https://answers.unity.com/questions/12713/how-do-i-reproduce-the-mvp-matrix.html
        // for (int i = 0; i < 4; i++) {
        //     camProj[2, i] = camProj[2, i] * 0.5f + camProj[3, i] * 0.5f;
        // }

        var camMat = camProj * _camera.worldToCameraMatrix;

        for (int i = 0; i < _curve3dHom.Length; i++) {
            var screenPos = camMat * _curve3dHom[i];
            _curve2dRat[i] = new float3(screenPos.x, screenPos.y, screenPos.w);
        }
    }

    private void DrawPerspectiveLines() {
        Gizmos.color = Color.gray;
        int steps = 16;
        for (int i = 0; i <= steps; i++) {
            float t = i / (float)(steps);
            float3 p = Util.HomogeneousNormalize(BDCCubic4d.Get(_curve3dHom, t));
            Gizmos.DrawLine(_camera.transform.position, p);
        }
    }

    private void Draw3dCurve() {
        Gizmos.color = Color.blue;
        for (int i = 0; i < _curve3dHom.Length; i++) {
            var p = Util.HomogeneousNormalize(_curve3dHom[i]);
            Gizmos.DrawSphere(p, 0.03f);
            Gizmos.DrawLine(_camera.transform.position, p);
        }

        Gizmos.color = Color.white;
        float3 pPrev = Util.HomogeneousNormalize(BDCCubic4d.Get(_curve3dHom, 0f));
        Gizmos.DrawSphere(pPrev, 0.01f);
        int steps = 16;
        for (int i = 1; i <= steps; i++) {
            float t = i / (float)(steps);
            float3 p = Util.HomogeneousNormalize(BDCCubic4d.Get(_curve3dHom, t));

            Gizmos.DrawLine(pPrev, p);
            Gizmos.DrawSphere(p, 0.01f);

            pPrev = p;
        }
    }

    // Draw projected rational 2d spline using piecewise linear lines in screenspace
    void OnPostRender() {
        if (!_lineMaterial) {
            Debug.LogError("Please Assign a material on the inspector");
            return;
        }

        GL.PushMatrix();
        _lineMaterial.SetPass(0);
        GL.LoadOrtho();

        var pPrev = Util.PerspectiveDivide(BDCCubic3d.Get(_curve2dRat, 0f));
        pPrev = new float3(0.5f, 0.5f, 0f) + pPrev * 0.5f; // from NDC to screenspace
        int steps = 16;
        for (int i = 1; i <= steps; i++) {
            float t = i / (float)(steps - 1);
            var p = Util.PerspectiveDivide(BDCCubic3d.Get(_curve2dRat, t));
            p = new float3(0.5f, 0.5f, 0f) + p * 0.5f;  // from NDC to screenspace

            var pDelta = Util.PerspectiveDivide(BDCCubic3d.Get(_curve2dRat, t+0.01f));
            pDelta = new float3(0.5f, 0.5f, 0f) + pDelta * 0.5f;  // from NDC to screenspace

            var tangent = math.normalize((pDelta - p)) * 0.05f;
            var normal = new float3(-tangent.y, tangent.x, 0f);

            GL.Begin(GL.LINES);
            GL.Color(Color.red);
            GL.Vertex(pPrev);
            GL.Vertex(p);
            GL.End();

            // GL.Begin(GL.LINES);
            // GL.Color(Color.red);
            // GL.Vertex(p);
            // GL.Vertex(p + tangent);
            // GL.End();

            GL.Begin(GL.LINES);
            GL.Color(Color.red);
            GL.Vertex(p);
            GL.Vertex(p + normal);
            GL.End();

            pPrev = p;
        }

        GL.PopMatrix();
    }
}