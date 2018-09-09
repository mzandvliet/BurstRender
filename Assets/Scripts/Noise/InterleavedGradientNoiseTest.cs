using UnityEngine;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;

public class InterleavedGradientNoiseTest : MonoBehaviour {
    Texture2D _tex;

    const int res = 2048;
    const int numVals = res * res;

    private void Start() {
        _tex = new Texture2D(res, res, TextureFormat.ARGB32, false, true);
        var data = new Color[res * res];
        var values = new NativeArray<float>(numVals, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

        // 500ms, 6ms
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var rj = new GradientNoiseJob();
        rj.Values = values;
        var h = rj.Schedule();
        h.Complete();
        sw.Stop();
        Debug.Log("IGN Job: " + sw.ElapsedMilliseconds);

        // 96ms, 3ms
        sw = System.Diagnostics.Stopwatch.StartNew();
        var j = new GradientNoiseJobParallel();
        j.Values = values;
        h = j.Schedule(values.Length, 64, h);
        h.Complete();
        sw.Stop();
        Debug.Log("IGNParallel Job: " + sw.ElapsedMilliseconds);

        for (int i = 0; i < numVals; i++) {
            float val = values[i];
          
            data[i] = new Color(val, val, val, 1f);
        }

        _tex.SetPixels(0, 0, res, res, data, 0);
        _tex.Apply();

        values.Dispose();
    }


    private void OnGUI() {
        GUI.DrawTexture(new Rect(0f, 0f, res, res), _tex);
    }

    [BurstCompile]
    public struct GradientNoiseJob : IJob {
        [WriteOnly] public NativeArray<float> Values;

        public void Execute() {
            for (int i = 0; i < Values.Length; i++) {
                var xy = new float2((i % res), (i / res));
                Values[i] = InterleavedGradientNoise(xy);
            }
        }
    }

    [BurstCompile]
    public struct GradientNoiseJobParallel : IJobParallelFor {
        [WriteOnly] public NativeArray<float> Values;

        public void Execute(int i) {
            var xy = new float2((i % res), (i / res));
            Values[i] = InterleavedGradientNoise(xy);
        }
    }

    private static float InterleavedGradientNoise(float2 xy) {
        return math.frac(52.9829189f
                    * math.frac(xy.x * 0.06711056f
                            + xy.y * 0.00583715f));
    }
}