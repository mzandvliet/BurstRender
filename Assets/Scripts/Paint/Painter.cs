using UnityEngine;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Rng = Unity.Mathematics.Random;
using Ramjet;

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

public class Painter : MonoBehaviour {
    private NativeArray<float2> _curves;
    private NativeArray<float3> _colors;
    private NativeArray<float> _distanceCache;
    private Rng _rng;

    private NativeArray<float3> _canvas;
    private Texture2D _canvasTex;

    private const float CANVAS_SCALE = 10f;
    private const int CANVAS_RES = 1024;
    private const int NUM_CURVES = 64;

    private void Awake() {
        _curves = new NativeArray<float2>(NUM_CURVES * 4, Allocator.Persistent);
        _colors = new NativeArray<float3>(NUM_CURVES, Allocator.Persistent);
        _distanceCache = new NativeArray<float>(32, Allocator.Persistent);

        _rng = new Rng(1234);

        for (int i = 0; i < NUM_CURVES; i++) {
            float2 p = new float2(_rng.NextFloat(1f, 9f), _rng.NextFloat(0.5f, 1f));
            for (int j = 0; j < 4; j++) {
                _curves[i * 4 + j] = p;
                p += new float2(_rng.NextFloat(-0.3f, 0.3f), _rng.NextFloat(0.8f, 1.6f)) * j;
            }

            _colors[i] = new float3(_rng.NextFloat(0.3f, 0.5f),_rng.NextFloat(0.6f, 0.8f),_rng.NextFloat(0.3f, 0.6f));
        }

        _canvas = new NativeArray<float3>(CANVAS_RES * CANVAS_RES, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        _canvasTex = new Texture2D(CANVAS_RES, CANVAS_RES, TextureFormat.ARGB32, false, true);

        var ccj = new ClearCanvasJob();
        ccj.canvas = _canvas;
        var h = ccj.Schedule(_canvas.Length, 128);
        h.Complete();
    }

    private void OnDestroy() {
        _curves.Dispose();
        _colors.Dispose();
        _distanceCache.Dispose();
        _canvas.Dispose();
    }

    private void Update() {
        const int steps = 1024;
        if (Time.frameCount >= steps) {
            return;
        }

        var h = new JobHandle();
        for (int i = 0; i < NUM_CURVES; i++) {
            var sj = new StrokeJob();
            sj.canvas = _canvas;
            sj.curve = _curves;
            sj.distCache = _distanceCache;
            sj.steps = steps;
            sj.curveIndex = i;
            sj.color = _colors[i];
            sj.time = Time.frameCount;
            sj.canvasResolution = CANVAS_RES;
            sj.canvasScale = CANVAS_SCALE;
            h = sj.Schedule(h);
        }
        h.Complete();

        Util.ToTexture2D(_canvas, _canvasTex, new int2(CANVAS_RES, CANVAS_RES));
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
        float2 pPrev = BDC2Cube.Get(_curves, 0f);
        Gizmos.DrawSphere(Math.ToVec3(pPrev), 0.01f);
        int steps = 16;
        for (int i = 1; i <= steps; i++) {
            float t = (i / (float)steps);
            float2 p = BDC2Cube.Get(_curves, t);
            Gizmos.DrawLine(Math.ToVec3(pPrev), Math.ToVec3(p));
            Gizmos.DrawSphere(Math.ToVec3(p), 0.01f);
            pPrev = p;
        }
    }

    [BurstCompile]
    public struct ClearCanvasJob : IJobParallelFor {
        public NativeArray<float3> canvas;

        public void Execute(int i) {
            canvas[i] = new float3(1, 1, 1);
        }
    }

    [BurstCompile]
    public struct StrokeJob : IJob {
        [ReadOnly] public NativeArray<float2> curve;
        [ReadOnly] public NativeArray<float> distCache;
        public NativeArray<float3> canvas;
        public float3 color;
        public int curveIndex;
        public int steps;
        public int time;
        public int canvasResolution;
        public float canvasScale;

        public void Execute() {
            int i = time;
            float tScale = 1f / (float)(steps - 1);
            float t = i * tScale;
            float res = (float)canvasResolution;

            float2 p = BDC2Cube.GetAt(curve, t, curveIndex * 4);
            float2 n = BDC2Cube.GetNormalAt(curve, t, curveIndex * 4);

            int widthSteps = 4 + (int)math.round((1f - t) * 32f);
            float width = 0.33f;
            float widthStep = width / (float)(widthSteps-1);
            float2 start = p - n * (width*0.5f);

            for (int j = 0; j < widthSteps; j++) {
                float alpha = RampUpDown(j, widthSteps);
                int2 pi = ToGrid(start + n * (widthStep * j), canvasScale, canvasResolution);
                Draw(pi, canvas, color, alpha, canvasResolution);
            }
        }

        private static float RampUpDown(int i, int max) {
            float halfMax = (max-1) * 0.5f;
            return i <= halfMax ? i / halfMax : 2f - (i / halfMax);
        }

        private static int2 ToGrid(float2 p, float canvasScale, int canvasRes) {
            return (int2)math.round(p / canvasScale * canvasRes);
        }

        private static void Draw(int2 p, NativeArray<float3> canvas, float3 col, float alpha, int res)  {
            var current = canvas[p.y * res + p.x];
            canvas[p.y * res + p.x] = math.lerp(current, col, alpha);
        }
    }
}