using UnityEngine;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;

public class NoiseTest : MonoBehaviour {
    Texture2D _tex;

    const int res = 2048;
    const int numVals = res * res;
    // Todo: profiling
    
    private void Start() {
        
        // _tex = new Texture2D(res, res, TextureFormat.ARGB32, false, true);
        // var data = new Color[res * res];

        var mag2 = new RamjetMath.Mag2(4, Allocator.Persistent);
        for (uint i = 0; i < mag2._length; i++) {
            mag2[i] = i;
        }
        for (uint i = 0; i < mag2._length; i++) {
            Debug.Log(mag2[i]);
        }
        mag2.Dispose();

        var values = new NativeArray<float>(numVals, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

        // 226ms
        var rm = new Meisui.Random.MersenneTwister(1234);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < values.Length; i++) {
            values[i] = (float)rm.genrand_real2();
        }
        sw.Stop();
        Debug.Log("MT Managed: " + sw.ElapsedMilliseconds);

        // 765ms
        var rrm = new RamjetMath.MersenneTwister(1234);
        sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < values.Length; i++) {
            values[i] = rrm.genrand_real2();
        }
        sw.Stop();
        Debug.Log("MT Burst: " + sw.ElapsedMilliseconds);

        sw = System.Diagnostics.Stopwatch.StartNew();
        var j = new RandomJob();
        j.Values = values;
        j.Random = rrm;
        j.Schedule().Complete();
        sw.Stop();
        Debug.Log("MT Burst Job: " + sw.ElapsedMilliseconds);

        rrm.Dispose();

        // 220ms
        var rs = new System.Random(1234);
        sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < values.Length; i++) {
            values[i] = (float)rs.NextDouble();
        }
        sw.Stop();
        Debug.Log("System.Random: " + sw.ElapsedMilliseconds);

        // 171ms
        sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < values.Length; i++) {
            values[i] = Random.value;
        }
        sw.Stop();
        Debug.Log("Unity.Random.value: " + sw.ElapsedMilliseconds);

        // float min = 2f;
        // float max = -1f;

        // for (int i = 0; i < numVals; i++) {
        //     float val = values[i];
        //     if (val > max) {
        //         max = val;
        //     }
        //     if (val < min) {
        //         min = val;
        //     }
        //     data[i] = new Color(val, val, val, 1f);
        // }

        // Debug.Log(min + ", " + max);

        // _tex.SetPixels(0, 0, res, res, data, 0);
        // _tex.Apply();

        values.Dispose();
    }


    // private void OnGUI() {
    //     GUI.DrawTexture(new Rect(0f, 0f, res, res), _tex);
    // }

    [BurstCompile]
    public struct RandomJob : IJob {
        [WriteOnly] public NativeArray<float> Values;
        public RamjetMath.MersenneTwister Random;

        public void Execute() {
            for (int i = 0; i < Values.Length; i++) {
                Values[i] = Random.genrand_real2();
            }
        }
    }
}