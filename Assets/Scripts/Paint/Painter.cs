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

    Sort out blending
    Start defining some geometries you want to draw, like:
    - a field of grass growing on smooth hills
    - a sphere, with lighting
    - ink outlines, with colored in gradients, respecting lighting.
    - this means define 3d spline geometry, project to 2d
        - Now think about order, sorting, planning
    
    Record videos

 */

public struct Vertex {
    public float3 vertex;
    public float3 normal; // Note: unused right now
    public float2 uv;
    public float3 color;
};

public class Painter : MonoBehaviour {
    private Camera _camera;
    private CommandBuffer _commandBuffer;

    [SerializeField] private Material _brushMaterial;

    private NativeArray<Vertex> _brushVerts;
    private ComputeBuffer _brushBuffer;
    
    
    private NativeArray<float2> _curves;
    private NativeArray<float3> _colors;
    private NativeArray<float> _distanceCache;
    private Rng _rng;

    private RenderTexture _canvasTex;

    private static readonly int2 CANVAS_RES = new int2(1920, 1080);

    private const int NUM_CURVES = 1;
    private const int CONTROLS_PER_CURVE = 4;
    private const int CURVE_TESSELATION = 6;
    private const int VERTS_PER_TESSEL = 6;

    private void Awake() {
        _canvasTex = new RenderTexture(CANVAS_RES.x, CANVAS_RES.y, 24);
        _canvasTex.Create();

        _camera = gameObject.GetComponent<Camera>();
        _camera.orthographicSize = 6f;
        _camera.clearFlags = CameraClearFlags.SolidColor;
        _camera.backgroundColor = Color.white;
        _camera.orthographic = true;
        // _camera.targetTexture = _canvasTex;

        _brushVerts = new NativeArray<Vertex>(NUM_CURVES * CURVE_TESSELATION * VERTS_PER_TESSEL, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        _brushBuffer = new ComputeBuffer(_brushVerts.Length, Marshal.SizeOf(typeof(Vertex)));

        _brushMaterial.SetBuffer("verts", _brushBuffer);

        _commandBuffer = new CommandBuffer();
        // Todo: what does the instanceCount parameter do here?
        _commandBuffer.DrawProcedural(transform.localToWorldMatrix, _brushMaterial, 0, MeshTopology.Triangles, _brushVerts.Length, 1);
        _camera.AddCommandBuffer(CameraEvent.BeforeForwardAlpha, _commandBuffer);

        _curves = new NativeArray<float2>(NUM_CURVES * CONTROLS_PER_CURVE, Allocator.Persistent);
        _colors = new NativeArray<float3>(NUM_CURVES, Allocator.Persistent);
        _distanceCache = new NativeArray<float>(32, Allocator.Persistent);

        _rng = new Rng(1234);
    }

    private void OnDestroy() {
        _curves.Dispose();
        _colors.Dispose();
        _brushVerts.Dispose();
        _distanceCache.Dispose();

        _brushBuffer.Dispose();
        _canvasTex.Release();
    }

    private JobHandle _handle;

    private void Update() {
        if (Time.frameCount % 15 == 0) {
            var genJob = new GenerateRandomCurvesJob();
            genJob._rng = new Rng((uint)_rng.NextInt());
            genJob._curves = _curves;
            genJob._colors = _colors;
            var h = genJob.Schedule(NUM_CURVES, 8);

            var tessJob = new TesslateCurvesJob();
            tessJob._curves = _curves;
            tessJob._colors = _colors;
            tessJob._brushVerts = _brushVerts;
            _handle = tessJob.Schedule(NUM_CURVES, 1, h);

            JobHandle.ScheduleBatchedJobs();
        }
    }

    private void LateUpdate() {
        _handle.Complete();

        _brushBuffer.SetData(_brushVerts);
        _camera.Render();
    }

    private void OnDrawGizmos() {
        if (!Application.isPlaying) {
            return;
        }

        Gizmos.color = Color.blue;
        for (int i = 0; i < _curves.Length; i++) {
            Gizmos.DrawSphere(Math.ToVec3(_curves[i]), 0.05f);
        }

        Gizmos.color = Color.white;
        float2 pPrev = BDCCubic2d.Get(_curves, 0f);
        Gizmos.DrawSphere(Math.ToVec3(pPrev), 0.01f);
        int steps = 16;
        for (int i = 1; i <= steps; i++) {
            float t = (i / (float)steps);
            float2 p = BDCCubic2d.Get(_curves, t);
            float2 tg = BDCCubic2d.GetTangent(_curves, t);
            float2 n = BDCCubic2d.GetNormal(_curves, t);
            Gizmos.DrawLine(Math.ToVec3(pPrev), Math.ToVec3(p));
            Gizmos.DrawSphere(Math.ToVec3(p), 0.01f);

            Gizmos.color = Color.blue;
            Gizmos.DrawRay(Math.ToVec3(p), Math.ToVec3(n));
            Gizmos.color = Color.green;
            Gizmos.DrawRay(Math.ToVec3(p), Math.ToVec3(tg));
            
            pPrev = p;
        }
    }


    private static float RampUpDown(float i) {
        return i <= 0.5f ? i * 2f : 2f - (i * 2f);
    }

    private static float3 ToFloat3(float2 v) {
        return new float3(v.x, v.y, 0f);
    }


    private struct GenerateRandomCurvesJob : IJobParallelFor {
        public Rng _rng;

        [NativeDisableParallelForRestriction] public NativeArray<float2> _curves;
        [NativeDisableParallelForRestriction] public NativeArray<float3> _colors;

        public void Execute(int i) {
            float2 p = new float2(_rng.NextFloat(0f, 10f), _rng.NextFloat(0.5f, 1f));
            for (int j = 0; j < CONTROLS_PER_CURVE; j++) {
                _curves[i * CONTROLS_PER_CURVE + j] = p;
                p += new float2(_rng.NextFloat(-1f, 0.5f), _rng.NextFloat(0.1f, 2f));
            }

            _colors[i] = new float3(_rng.NextFloat(0.3f, 0.5f), _rng.NextFloat(0.6f, 0.8f), _rng.NextFloat(0.3f, 0.6f));
        }
    }


    /*
        Todo: 
        
        Strong enough curvature will make the outer edges overlap and flip triangles
        
        maaaay want to do this in a ComputeShader instead, we'll see
     */

    private struct TesslateCurvesJob : IJobParallelFor {
        [NativeDisableParallelForRestriction] public NativeArray<float2> _curves;
        public NativeArray<float3> _colors;
        [NativeDisableParallelForRestriction] public NativeArray<Vertex> _brushVerts;

        public void Execute(int curveId) {
            int vertOffset = curveId * CURVE_TESSELATION * VERTS_PER_TESSEL;
            int firstControl = curveId * CONTROLS_PER_CURVE;

            for (int i = 0; i < CURVE_TESSELATION; i++) {
                float tA = i / (float)(CURVE_TESSELATION);

                float3 posA = ToFloat3(BDCCubic2d.GetAt(_curves, tA, firstControl));
                float3 norA = ToFloat3(-BDCCubic2d.GetNormalAt(_curves, tA, firstControl));

                float tB = (i + 1) / (float)(CURVE_TESSELATION);
                float3 posB = ToFloat3(BDCCubic2d.GetAt(_curves, tB, firstControl));
                float3 norB = ToFloat3(-BDCCubic2d.GetNormalAt(_curves, tB, firstControl));

                // todo: linearize the uvs using cached distances or lower degree Berstein Polys
                float uvYA = tA;
                float uvYB = tB;

                const float maxWidth = 0.25f;
                // Bug: the below ramps for a and b are off-by-one w.r.t. each other.
                float widthA = RampUpDown(tA) * maxWidth;
                float widthB = RampUpDown(tB) * maxWidth;
                // float widthA = maxWidth;
                // float widthB = maxWidth;

                var v = new Vertex();
                v.normal = new float3(0, 0, -1);
                float lightA = (0.3f + 0.7f * tA);
                float lightB = (0.3f + 0.7f * tB);

                v.vertex = posA - norA * widthA;
                v.uv = new float2(0, uvYA);
                v.color = _colors[curveId] * lightA;
                _brushVerts[vertOffset + i * VERTS_PER_TESSEL + 0] = v;

                v.vertex = posB - norB * widthB;
                v.uv = new float2(0, uvYB);
                v.color = _colors[curveId] * lightB;
                _brushVerts[vertOffset + i * VERTS_PER_TESSEL + 1] = v;

                v.vertex = posB + norB * widthB;
                v.uv = new float2(1, uvYB);
                v.color = _colors[curveId] * lightB;
                _brushVerts[vertOffset + i * VERTS_PER_TESSEL + 2] = v;

                v.vertex = posA - norA * widthA;
                v.uv = new float2(0, uvYA);
                v.color = _colors[curveId] * lightA;
                _brushVerts[vertOffset + i * VERTS_PER_TESSEL + 3] = v;

                v.vertex = posB + norB * widthB;
                v.uv = new float2(1, uvYB);
                v.color = _colors[curveId] * lightB;
                _brushVerts[vertOffset + i * VERTS_PER_TESSEL + 4] = v;

                v.vertex = posA + norA * widthA;
                v.uv = new float2(1, uvYA);
                v.color = _colors[curveId] * lightA;
                _brushVerts[vertOffset + i * VERTS_PER_TESSEL + 5] = v;
            }
        }
    }
}