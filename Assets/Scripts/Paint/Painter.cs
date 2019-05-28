using UnityEngine;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Rng = Unity.Mathematics.Random;
using Ramjet;
using UnityEngine.Rendering;
using System.Runtime.InteropServices;

public class Painter : MonoBehaviour {
    [SerializeField] private Material _paintMaterial;

    [SerializeField] private Material _blitClearCanvasMaterial;
    [SerializeField] private Material _blitAddLayerMaterial;

    private GameObject _meshObj;
    private Mesh _mesh;
    private MeshFilter _meshFilter;
    private MeshRenderer _meshRenderer;

    private Camera _camera;

    private NativeArray<float3> _verts;
    private NativeArray<float3> _normals;
    private NativeArray<float4> _vertColors;
    private NativeArray<float2> _uvs;
    private NativeArray<int> _indices;
    private Vector3[] _vertsMan;
    private Vector3[] _normalsMan;
    private Color[] _vertColorsMan;
    private int[] _indicesMan;
    private Vector2[] _uvsMan;

    private Rng _rng;

    private RenderTexture _canvasTex;

    private const int CONTROLS_PER_CURVE = 4;
    private const int TESSELATE_VERTICAL = 16;
    private const int TESSELATE_HORIZONTAL = 3; 

    private void Awake() {
        _canvasTex = new RenderTexture(Screen.currentResolution.width, Screen.currentResolution.height, 24);
        _canvasTex.Create();

        _camera = gameObject.GetComponent<Camera>();
        _camera.orthographicSize = 4f;
        _camera.orthographic = true;
        _camera.targetTexture = _canvasTex;
        _camera.enabled = false;

        _rng = new Rng(1234);

        _mesh = new Mesh();
        _meshObj = new GameObject("Mesh");
        _meshFilter = _meshObj.AddComponent<MeshFilter>();
        _meshRenderer = _meshObj.AddComponent<MeshRenderer>();
        _meshFilter.mesh = _mesh;
        _meshRenderer.material = _paintMaterial;

        Clear();
    }

    public void Init(int maxCurves) {
        int numVerts = maxCurves * TESSELATE_VERTICAL * TESSELATE_HORIZONTAL;
        int numIndices = maxCurves * (TESSELATE_VERTICAL-1) * (TESSELATE_HORIZONTAL-1) * 6;

        _verts = new NativeArray<float3>(numVerts, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        _normals = new NativeArray<float3>(numVerts, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        _vertColors = new NativeArray<float4>(numVerts, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        _uvs = new NativeArray<float2>(numVerts, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        _indices = new NativeArray<int>(numIndices, Allocator.Persistent, NativeArrayOptions.ClearMemory);

        _vertsMan = new Vector3[numVerts];
        _normalsMan = new Vector3[numVerts];
        _vertColorsMan = new Color[numVerts];
        _uvsMan = new Vector2[numVerts];
        _indicesMan = new int[numIndices];
    }

    private void OnDestroy() {
        _verts.Dispose();
        _normals.Dispose();
        _vertColors.Dispose();
        _uvs.Dispose();
        _indices.Dispose();

        _canvasTex.Release();
    }

    public void Clear() {
        Graphics.Blit(_canvasTex, _canvasTex, _blitClearCanvasMaterial);
    }

    public void Draw(NativeArray<float3> curves, NativeArray<float3> colors) {
        var tessJob = new TesselateCurvesJob();
        tessJob.controls = curves;
        tessJob.colors = colors;

        tessJob.verts = _verts;
        tessJob.normals = _normals;
        tessJob.vertColors = _vertColors;
        tessJob.uvs = _uvs;
        tessJob.indices = _indices;
        var h = tessJob.Schedule();

        h.Complete();

        UpdateMesh();
        
        _camera.Render();
    }

    private void UpdateMesh() {
        Util.CopyToManaged(_verts, _vertsMan);
        Util.CopyToManaged(_normals, _normalsMan);
        Util.CopyToManaged(_vertColors, _vertColorsMan);
        Util.CopyToManaged(_indices, _indicesMan);
        Util.CopyToManaged(_uvs, _uvsMan);

        // The below is now the biggest bottleneck, at 0.24ms per frame on my machine
        _mesh.vertices = _vertsMan;
        _mesh.normals = _normalsMan;
        _mesh.triangles = _indicesMan;
        _mesh.colors = _vertColorsMan;
        _mesh.uv = _uvsMan;
        _mesh.RecalculateBounds();
        _mesh.UploadMeshData(false);
    }

    private void OnGUI() {
        GUI.DrawTexture(new Rect(0,0,Screen.width,Screen.height), _canvasTex);
    }

    /*
        Todo: 
        
        Strong enough curvature will make the outer edges overlap and flip triangles
        maaaay want to do this in a ComputeShader instead, we'll see
     */

    private struct TesselateCurvesJob : IJob {
        [ReadOnly] public NativeArray<float3> controls;
        [ReadOnly] public NativeArray<float3> colors;

        public NativeArray<float3> verts;
        public NativeArray<float3> normals;
        public NativeArray<float2> uvs;
        public NativeArray<float4> vertColors;
        public NativeArray<int> indices;


        public void Execute() {
            int numCurves = controls.Length / 4;
            for (int curveId = 0; curveId < numCurves; curveId++) {
                float4 color = new float4(colors[curveId].x, colors[curveId].y, colors[curveId].z, 1f);
                
                for (int i = 0; i < TESSELATE_VERTICAL; i++) {
                    int stepId = curveId * TESSELATE_VERTICAL + i;
                    float t = i / (float)(TESSELATE_VERTICAL-1);

                    float3 pos = Util.PerspectiveDivide(BDCCubic3d.GetAt(controls, t, curveId));
                    float3 posDelta = Util.PerspectiveDivide(BDCCubic3d.GetAt(controls, t+0.01f, curveId));

                    float3 curveTangent = math.normalize(posDelta - pos);
                    float3 curveNormal = new float3(-curveTangent.y, curveTangent.x, 0f);

                    float width = 0.1f;
                    curveNormal *= width;

                    var surfaceNormal = new float3(0, 0, -1);

                    verts[stepId * 3 + 0] = pos + curveNormal;
                    verts[stepId * 3 + 1] = pos;
                    verts[stepId * 3 + 2] = pos - curveNormal;

                    normals[stepId * 3 + 0] = surfaceNormal;
                    normals[stepId * 3 + 1] = surfaceNormal;
                    normals[stepId * 3 + 2] = surfaceNormal;

                    vertColors[stepId * 3 + 0] = color;
                    vertColors[stepId * 3 + 1] = color;
                    vertColors[stepId * 3 + 2] = color;

                    const float uvTile = 1f;

                    float uvY = t;
                    uvs[stepId * 3 + 0] = new float2(0.0f, uvY * uvTile);
                    uvs[stepId * 3 + 1] = new float2(0.5f, uvY * uvTile);
                    uvs[stepId * 3 + 2] = new float2(1.0f, uvY * uvTile);
                }
            }

            // Todo: only have to generate and set these once
            for (int curveId = 0; curveId < numCurves; curveId++) {
                for (int i = 0; i < TESSELATE_VERTICAL-1; i++) {
                    int baseIdx = curveId * (TESSELATE_VERTICAL-1) * 12 + i * 12;
                    int stepIdx = curveId * TESSELATE_VERTICAL + i;

                    indices[baseIdx + 0 ] = (stepIdx + 0) * 3 + 0;
                    indices[baseIdx + 1 ] = (stepIdx + 1) * 3 + 0;
                    indices[baseIdx + 2 ] = (stepIdx + 1) * 3 + 1;

                    indices[baseIdx + 3 ] = (stepIdx + 0) * 3 + 0;
                    indices[baseIdx + 4 ] = (stepIdx + 1) * 3 + 1;
                    indices[baseIdx + 5 ] = (stepIdx + 0) * 3 + 1;

                    indices[baseIdx + 6 ] = (stepIdx + 0) * 3 + 1;
                    indices[baseIdx + 7 ] = (stepIdx + 1) * 3 + 1;
                    indices[baseIdx + 8 ] = (stepIdx + 1) * 3 + 2;

                    indices[baseIdx + 9 ] = (stepIdx + 0) * 3 + 1;
                    indices[baseIdx + 10] = (stepIdx + 1) * 3 + 2;
                    indices[baseIdx + 11] = (stepIdx + 0) * 3 + 2;
                }
            }
        }
    }
}