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

    RENDER GRANDFATHER'S PAINTINGS

    Make it such that a spline path can be arbitary length. Possibly gets multi-stroke if longer than x.
    Then we can start generating some Gogh-likes by spiraling splines around attractors.

 */

public struct Vertex {
    public float3 vertex;
    public float3 normal; // Note: unused right now
    public float2 uv;
    public float3 color;
};

public class Painter : MonoBehaviour {
    [SerializeField] private CanvasRenderer _canvasRenderer;

    [SerializeField] private Material _brushMaterial;

    private Camera _camera;
    private CommandBuffer _commandBuffer;

    private NativeArray<Vertex> _brushVerts;
    private ComputeBuffer _brushBuffer;

    private Rng _rng;

    private RenderTexture _layerTex;

    private const int CONTROLS_PER_CURVE = 4;
    private const int CURVE_TESSELATION = 32;
    private const int VERTS_PER_TESSEL = 6 * 2; // 2 quads, each 2 tris, no vert sharing...

    private void Awake() {
        _layerTex = new RenderTexture(Screen.currentResolution.width, Screen.currentResolution.height, 24, RenderTextureFormat.ARGBFloat);
        _layerTex.Create();

        _camera = gameObject.GetComponent<Camera>();
        _camera.orthographicSize = 4f;
        _camera.orthographic = true;
        _camera.targetTexture = _layerTex;
        _camera.enabled = false;
        _camera.RemoveAllCommandBuffers();

        _rng = new Rng(1234);
    }

    public void Init(int maxCurves) {
        _brushVerts = new NativeArray<Vertex>(maxCurves * CONTROLS_PER_CURVE * CURVE_TESSELATION * VERTS_PER_TESSEL, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        _brushBuffer = new ComputeBuffer(_brushVerts.Length, Marshal.SizeOf(typeof(Vertex)));
        _brushMaterial.SetBuffer("verts", _brushBuffer);

        _commandBuffer = new CommandBuffer();
        _commandBuffer.ClearRenderTarget(true, true, new Color(0, 0, 0, 0));
        _commandBuffer.DrawProcedural(transform.localToWorldMatrix, _brushMaterial, 0, MeshTopology.Triangles, _brushVerts.Length, 1);
        _camera.AddCommandBuffer(CameraEvent.AfterForwardOpaque, _commandBuffer);
    }

    private void OnDestroy() {
        _brushVerts.Dispose();
        _brushBuffer.Dispose();
        _layerTex.Release();
    }

    public RenderTexture GetLayer() {
        return _layerTex;
    }

    private static int GetLevel(int i) {
        return (int)math.floor(math.log2(1 + i));

    }

    private static int GetFirstCurveIndexForLevel(int l) {
        return (int)math.floor(math.exp2(l)) - 1;
    }

    private static int GetCurveCountForLevel(int l) {
        return (int)math.floor(math.exp2(l));
    }

    public void Clear() {
        _canvasRenderer.Clear();
    }

    public void Draw(NativeArray<float2> curves, NativeArray<float> widths, NativeArray<float3> colors) {
        var tessJob = new TesslateCurvesJob();
        tessJob.curves = curves;
        tessJob.widths = widths;
        tessJob.colors = colors;
        tessJob.brushVerts = _brushVerts;
        var h = tessJob.Schedule(curves.Length / CONTROLS_PER_CURVE, 1);

        h.Complete();
        _brushBuffer.SetData(_brushVerts);
        _camera.Render();

        _canvasRenderer.Add(_layerTex);
    }

    // private void OnGUI() {
    //     GUI.DrawTexture(new Rect(0,0,512,512), _layerTex);
    // }

    // private void OnDrawGizmos() {
    //     if (!Application.isPlaying) {
    //         return;
    //     }

    //     Gizmos.color = Color.blue;
    //     for (int i = 0; i < _curves.Length; i++) {
    //         Gizmos.DrawSphere(Math.ToVec3(_curves[i]), 0.05f);
    //     }

    //     Gizmos.color = Color.white;
    //     float2 pPrev = BDCCubic2d.Get(_curves, 0f);
    //     Gizmos.DrawSphere(Math.ToVec3(pPrev), 0.01f);
    //     int steps = 16;
    //     for (int i = 1; i <= steps; i++) {
    //         float t = i / (float)(steps);
    //         float2 p = BDCCubic2d.Get(_curves, t);
    //         float2 tg = BDCCubic2d.GetTangent(_curves, t);
    //         float2 n = BDCCubic2d.GetNormal(_curves, t);
    //         Gizmos.DrawLine(Math.ToVec3(pPrev), Math.ToVec3(p));
    //         Gizmos.DrawSphere(Math.ToVec3(p), 0.01f);

    //         Gizmos.color = Color.blue;
    //         Gizmos.DrawRay(Math.ToVec3(p), Math.ToVec3(n * 0.3f));
    //         Gizmos.DrawRay(Math.ToVec3(p), -Math.ToVec3(n * 0.3f));
    //         Gizmos.color = Color.green;
    //         Gizmos.DrawRay(Math.ToVec3(p), Math.ToVec3(tg));
            
    //         pPrev = p;
    //     }
    // }

    private static float3 ToFloat3(in float2 v) {
        return new float3(v.x, v.y, 0f);
    }

    /*
        Todo: 
        
        Strong enough curvature will make the outer edges overlap and flip triangles
        maaaay want to do this in a ComputeShader instead, we'll see
     */

    private struct TesslateCurvesJob : IJobParallelFor {
        [ReadOnly] public NativeArray<float2> curves;
        [ReadOnly] public NativeArray<float3> colors;
        [ReadOnly] public NativeArray<float> widths;

        [NativeDisableParallelForRestriction] public NativeArray<Vertex> brushVerts;

        public void Execute(int curveId) {
            int vertOffset = curveId * CURVE_TESSELATION * VERTS_PER_TESSEL;
            int firstControl = curveId * CONTROLS_PER_CURVE;

            // var distances = new NativeArray<float>(8, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            // BDCCubic2d.CacheDistancesAt(curves, distances, firstControl);

            for (int i = 0; i < CURVE_TESSELATION; i++) {
                float tA = i / (float)(CURVE_TESSELATION);

                float3 posA = ToFloat3(BDCCubic2d.GetAt(curves, tA, firstControl));
                float3 norA = ToFloat3(-BDCCubic2d.GetNormalAt(curves, tA, firstControl));

                float tB = (i + 1) / (float)(CURVE_TESSELATION);
                float3 posB = ToFloat3(BDCCubic2d.GetAt(curves, tB, firstControl));
                float3 norB = ToFloat3(-BDCCubic2d.GetNormalAt(curves, tB, firstControl));

                // todo: linearize the uvs using cached distances or lower degree Berstein Polys
                float uvYA = tA;//BDCCubic2d.GetLength(distances, i / (float)(CURVE_TESSELATION)) / (distances[distances.Length - 1]);
                float uvYB = tB;//BDCCubic2d.GetLength(distances, (i+1) / (float)(CURVE_TESSELATION)) / distances[distances.Length - 1];

                float widthA = (0.4f + 0.6f * RampUpDown(uvYA)) * widths[curveId];
                float widthB = (0.4f + 0.6f * RampUpDown(uvYB)) * widths[curveId];

                norA *= widthA;
                norB *= widthB;

                int quadStartIdx = vertOffset + i * VERTS_PER_TESSEL;

                var v = new Vertex();
                v.normal = new float3(0, 0, -1);

                float uvTiling = 1f;

                // Triangle 1
                v.vertex = posA - norA;
                v.uv = new float2(0, uvYA * uvTiling);
                v.color = colors[curveId];
                brushVerts[quadStartIdx + 0] = v;

                v.vertex = posB - norB;
                v.uv = new float2(0, uvYB * uvTiling);
                v.color = colors[curveId];
                brushVerts[quadStartIdx + 1] = v;

                v.vertex = posB;
                v.uv = new float2(0.5f, uvYB * uvTiling);
                v.color = colors[curveId];
                brushVerts[quadStartIdx + 2] = v;

                // Triangle 2
                v.vertex = posB;
                v.uv = new float2(0.5f, uvYB * uvTiling);
                v.color = colors[curveId];
                brushVerts[quadStartIdx + 3] = v;

                v.vertex = posA;
                v.uv = new float2(0.5f, uvYA * uvTiling);
                v.color = colors[curveId];
                brushVerts[quadStartIdx + 4] = v;

                v.vertex = posA - norA;
                v.uv = new float2(0f, uvYA * uvTiling);
                v.color = colors[curveId];
                brushVerts[quadStartIdx + 5] = v;

                // Triangle 3
                v.vertex = posA;
                v.uv = new float2(0.5f, uvYA * uvTiling);
                v.color = colors[curveId];
                brushVerts[quadStartIdx + 6] = v;

                v.vertex = posB;
                v.uv = new float2(0.5f, uvYB * uvTiling);
                v.color = colors[curveId];
                brushVerts[quadStartIdx + 7] = v;

                v.vertex = posB + norB;
                v.uv = new float2(1f, uvYB * uvTiling);
                v.color = colors[curveId];
                brushVerts[quadStartIdx + 8] = v;

                // Triangle 4

                v.vertex = posB + norB;
                v.uv = new float2(1f, uvYB * uvTiling);
                v.color = colors[curveId];
                brushVerts[quadStartIdx + 9] = v;

                v.vertex = posA + norA;
                v.uv = new float2(1f, uvYA * uvTiling);
                v.color = colors[curveId];
                brushVerts[quadStartIdx + 10] = v;

                v.vertex = posA;
                v.uv = new float2(0.5f, uvYA * uvTiling);
                v.color = colors[curveId];
                brushVerts[quadStartIdx + 11] = v;
            }

            // distances.Dispose();

            float RampUpDown(in float i) {
                return math.pow(i <= 0.5f ? i * 2f : 2f - (i * 2f), 0.5f);
            }
        }
    }
}