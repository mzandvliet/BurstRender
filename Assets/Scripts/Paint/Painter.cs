using UnityEngine;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Rng = Unity.Mathematics.Random;
using Ramjet;
using UnityEngine.Rendering;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;

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
    private NativeArray<float2> _uvs;
    private NativeArray<int> _indices;
    private Vector3[] _vertsMan;
    private Vector3[] _normalsMan;
    private int[] _indicesMan;
    private Vector2[] _uvsMan;

    private Rng _rng;

    private RenderTexture _canvasTex;

    private const int CONTROLS_PER_CURVE = 4;
    private const int VERTS_VERTICAL = 16;
    private const int VERTS_HORIZONTAL = 3; 

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
        int numVerts = maxCurves * VERTS_VERTICAL * VERTS_HORIZONTAL;
        int numIndices = maxCurves * (VERTS_VERTICAL-1) * (VERTS_HORIZONTAL-1) * 2 * 3;

        Debug.Log(numVerts + ", " + numIndices);

        _verts = new NativeArray<float3>(numVerts, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        _normals = new NativeArray<float3>(numVerts, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        _uvs = new NativeArray<float2>(numVerts, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        _indices = new NativeArray<int>(numIndices, Allocator.Persistent, NativeArrayOptions.ClearMemory);

        _vertsMan = new Vector3[numVerts];
        _normalsMan = new Vector3[numVerts];
        _uvsMan = new Vector2[numVerts];
        _indicesMan = new int[numIndices];
    }

    private void OnDestroy() {
        _verts.Dispose();
        _normals.Dispose();
        _uvs.Dispose();
        _indices.Dispose();

        _canvasTex.Release();
    }

    public void Clear() {
        Graphics.Blit(_canvasTex, _canvasTex, _blitClearCanvasMaterial);
    }

    public void Draw(NativeArray<float2> curves, NativeArray<float> widths, NativeArray<float3> colors) {
        var tessJob = new TesslateCurvesJob();
        tessJob.controls = curves;
        tessJob.widths = widths;
        tessJob.colors = colors;

        tessJob.verts = _verts;
        tessJob.normals = _normals;
        tessJob.uvs = _uvs;
        tessJob.indices = _indices;
        var h = tessJob.Schedule();

        h.Complete();

        UpdateMesh();
        
        _camera.Render();
    }

    private void UpdateMesh() {
        Util.Copy(_vertsMan, _verts);
        Util.Copy(_normalsMan, _normals);
        Util.Copy(_indicesMan, _indices);
        Util.Copy(_uvsMan, _uvs);

        // The below is now the biggest bottleneck, at 0.24ms per frame on my machine
        _mesh.vertices = _vertsMan;
        _mesh.normals = _normalsMan;
        _mesh.triangles = _indicesMan;
        _mesh.uv = _uvsMan;
        _mesh.UploadMeshData(false);
    }

    private void OnGUI() {
        GUI.DrawTexture(new Rect(0,0,Screen.width,Screen.height), _canvasTex);
    }

    private static float3 ToFloat3(in float2 v) {
        return new float3(v.x, v.y, 0f);
    }

    /*
        Todo: 
        
        Strong enough curvature will make the outer edges overlap and flip triangles
        maaaay want to do this in a ComputeShader instead, we'll see
     */

    private struct TesslateCurvesJob : IJob {
        [ReadOnly] public NativeArray<float2> controls;
        [ReadOnly] public NativeArray<float3> colors;
        [ReadOnly] public NativeArray<float> widths;

        public NativeArray<float3> verts;
        public NativeArray<float3> normals;
        public NativeArray<float2> uvs;
        public NativeArray<int> indices;


        public void Execute() {
            int numCurves = controls.Length / 4;
            for (int curveId = 0; curveId < numCurves; curveId++) {
                int firstControl = curveId * CONTROLS_PER_CURVE;

                for (int i = 0; i < VERTS_VERTICAL; i++) {
                    int idx = curveId * VERTS_VERTICAL + i;
                    float t = i / (float)(VERTS_VERTICAL);

                    float3 pos = ToFloat3(BDCCubic2d.GetAt(controls, t, firstControl));
                    float3 edge = ToFloat3(-BDCCubic2d.GetNormalAt(controls, t, firstControl));
                    
                    float width = widths[curveId];
                    edge *= width;

                    float uvY = i / (float)(VERTS_VERTICAL - 1);

                    var normal = new float3(0, 0, -1);

                    verts[idx * 3 + 0] = pos - edge;
                    verts[idx * 3 + 1] = pos;
                    verts[idx * 3 + 2] = pos + edge;

                    normals[idx * 3 + 0] = normal;
                    normals[idx * 3 + 1] = normal;
                    normals[idx * 3 + 2] = normal;

                    const float uvTile = 1f;

                    uvs[idx * 3 + 0] = new float2(0.0f, uvY * uvTile);
                    uvs[idx * 3 + 1] = new float2(0.5f, uvY * uvTile);
                    uvs[idx * 3 + 2] = new float2(1.0f, uvY * uvTile);
                }
            }

            for (int i = 0; i < numCurves * (VERTS_VERTICAL-1); i++) {
                int idx = i * 12;

                indices[idx + 0 ] = (i + 0) * 3 + 0;
                indices[idx + 1 ] = (i + 1) * 3 + 0;
                indices[idx + 2 ] = (i + 1) * 3 + 1;

                indices[idx + 3 ] = (i + 0) * 3 + 0;
                indices[idx + 4 ] = (i + 1) * 3 + 1;
                indices[idx + 5 ] = (i + 0) * 3 + 1;

                indices[idx + 6 ] = (i + 0) * 3 + 1;
                indices[idx + 7 ] = (i + 1) * 3 + 1;
                indices[idx + 8 ] = (i + 1) * 3 + 2;

                indices[idx + 9 ] = (i + 0) * 3 + 1;
                indices[idx + 10] = (i + 1) * 3 + 2;
                indices[idx + 11] = (i + 0) * 3 + 2;
            }
        }
    }

    public static class Util {
        public static Vector3 ToVec3(float2 p) {
            return new Vector3(p.x, p.y, 0f);
        }

        public static unsafe void Copy(Vector3[] destination, NativeArray<float3> source) {
            fixed (void* vertexArrayPointer = destination) {
                UnsafeUtility.MemCpy(
                    vertexArrayPointer,
                    NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(source),
                    destination.Length * (long)UnsafeUtility.SizeOf<float3>());
            }
        }

        public static unsafe void Copy(Vector2[] destination, NativeArray<float2> source) {
            fixed (void* vertexArrayPointer = destination) {
                UnsafeUtility.MemCpy(
                    vertexArrayPointer,
                    NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(source),
                    destination.Length * (long)UnsafeUtility.SizeOf<float2>());
            }
        }

        public static unsafe void Copy(int[] destination, NativeArray<int> source) {
            fixed (void* vertexArrayPointer = destination) {
                UnsafeUtility.MemCpy(
                    vertexArrayPointer,
                    NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(source),
                    destination.Length * (long)UnsafeUtility.SizeOf<int>());
            }
        }

    }
}