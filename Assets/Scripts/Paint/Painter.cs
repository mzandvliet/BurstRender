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
    
    Draw along a spline on a canvas of.. pixels for now.
    
    Ideas:

    - Need to draw actual ink/paint. Needs to be smooth, anti-aliased.
        - GPU based 2d mesh along the splines I guess? And then rasterize that.
    - Having analytic projection from 3d cubic curve to 2d screenspace cubic curve will be useful
    - Can use distance fields to model scenes including curvature/normal information
    - Scene geometry should express how it wants to be drawn. Saves analysis.

 */

public struct Vertex {
    public float3 vertex;
    public float3 normal;
    public float2 uv;
    public float3 color;
};

public class Painter : MonoBehaviour {
    private Transform _cameraObject;
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

    private const float CANVAS_SCALE = 10f;
    private const int CANVAS_RES = 1024;

    private const int NUM_CURVES = 32;
    private const int CONTROLS_PER_CURVE = 4;
    private const int CURVE_TESSELATION = 16;
    private const int VERTS_PER_TESSEL = 6;

    private void Awake() {
        _canvasTex = new RenderTexture(CANVAS_RES, CANVAS_RES, 24);
        _canvasTex.Create();

        _cameraObject = new GameObject("BrushCamera").transform;
        _cameraObject.transform.position = new Vector3(5f, 5f, -10f);
        _camera = _cameraObject.gameObject.AddComponent<Camera>();
        _camera.clearFlags = CameraClearFlags.SolidColor;
        _camera.backgroundColor = Color.white;
        _camera.orthographic = true;
        _camera.targetTexture = _canvasTex;
        _camera.enabled = false;

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

        for (int i = 0; i < NUM_CURVES; i++) {
            float2 p = new float2(_rng.NextFloat(1f, 9f), _rng.NextFloat(0.5f, 1f));
            for (int j = 0; j < CONTROLS_PER_CURVE; j++) {
                _curves[i * CONTROLS_PER_CURVE + j] = p;
                p += new float2(_rng.NextFloat(-0.3f, 0.3f), _rng.NextFloat(1.5f, 3f));
            }

            _colors[i] = new float3(_rng.NextFloat(0.3f, 0.5f),_rng.NextFloat(0.6f, 0.8f),_rng.NextFloat(0.3f, 0.6f));
        }
    }

    private void Start() {
        for (int i = 0; i < NUM_CURVES; i++) {
            TesselateCurve(i);
        }

        _camera.Render();
    }

    /*
        Todo: 
        
        Strong enough curvature will make the outer edges overlap and flip triangles
        
        maaaay want to do this in a ComputeShader instead, we'll see
     */
    private void TesselateCurve(int curveId) {
        int vertOffset = curveId * CURVE_TESSELATION * VERTS_PER_TESSEL;
        int firstControl = curveId * CONTROLS_PER_CURVE;

        for (int i = 0; i < CURVE_TESSELATION; i++) {
            float tA = i / (float)(CURVE_TESSELATION);
            
            float3 posA = ToFloat3(BDCCubic2d.GetAt(_curves, tA, firstControl));
            float3 norA = ToFloat3(BDCCubic2d.GetNormalAt(_curves, tA, firstControl));
            float3 tngA = ToFloat3(BDCCubic2d.GetTangentAt(_curves, tA, firstControl));

            float tB = (i+1) / (float)(CURVE_TESSELATION);
            float3 posB = ToFloat3(BDCCubic2d.GetAt(_curves, tB, firstControl));
            float3 norB = ToFloat3(BDCCubic2d.GetNormalAt(_curves, tB, firstControl));
            float3 tngB = ToFloat3(BDCCubic2d.GetTangentAt(_curves, tB, firstControl));

            // todo: fix uvs
            float uvYA = tA;
            float uvYB = tB;

            const float width = 0.25f;

            var v = new Vertex();
            v.normal = new float3(0, 0, -1);
            v.color = _colors[curveId];

            v.vertex = posA - norA * width;
            v.uv = new float2(0,uvYA);
            _brushVerts[vertOffset + i * VERTS_PER_TESSEL + 0] = v;

            v.vertex = posB - norB * width;
            v.uv = new float2(0, uvYB);
            _brushVerts[vertOffset + i * VERTS_PER_TESSEL + 1] = v;

            v.vertex = posB + norB * width;
            v.uv = new float2(1, uvYB);
            _brushVerts[vertOffset + i * VERTS_PER_TESSEL + 2] = v;

            v.vertex = posA - norA * width;
            v.uv = new float2(0, uvYA);
            _brushVerts[vertOffset + i * VERTS_PER_TESSEL + 3] = v;

            v.vertex = posB + norB * width;
            v.uv = new float2(1, uvYB);
            _brushVerts[vertOffset + i * VERTS_PER_TESSEL + 4] = v;

            v.vertex = posA + norA * width;
            v.uv = new float2(1, uvYA);
            _brushVerts[vertOffset + i * VERTS_PER_TESSEL + 5] = v;
        }

        _brushBuffer.SetData(_brushVerts);
    }

    private static float3 ToFloat3(float2 v) {
        return new float3(v.x, v.y, 0f);
    }

    private void OnDestroy() {
        _curves.Dispose();
        _colors.Dispose();
        _brushVerts.Dispose();
        _distanceCache.Dispose();

        _brushBuffer.Dispose();
        _canvasTex.Release();
    }

    private void Update() {
        
    }

    private void OnGUI() {
        float dims = math.min(Screen.width, Screen.height);
        GUI.DrawTexture(new Rect(0,0, dims, dims), _canvasTex, ScaleMode.ScaleToFit);
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
            Gizmos.DrawLine(Math.ToVec3(pPrev), Math.ToVec3(p));
            Gizmos.DrawSphere(Math.ToVec3(p), 0.01f);
            pPrev = p;
        }
    }
}