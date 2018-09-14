using UnityEngine;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;

public class NoiseTest : MonoBehaviour {
    Texture2D _tex;

    const int res = 2048;
    const int numVals = res * res;

    private void Start() {
        XorshiftBurst xor = new XorshiftBurst(5132512, 3292391, 109854, 587295);
        for (int i = 0; i < 128; i++) {
            Debug.Log(xor.NextInt(0, 128));
            Debug.Log(xor._seed0 + ", " + xor._seed1 + ", " + xor._seed2 + ", " + xor._seed3);
        }
    }

    private void Profile() {
        _tex = new Texture2D(res, res, TextureFormat.ARGB32, false, true);
        var data = new Color[res * res];
        var values = new NativeArray<float>(numVals, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

        // 220ms, 55ms
        var rs = new System.Random(1234);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < values.Length; i++) {
            values[i] = (float)rs.NextDouble();
        }
        sw.Stop();
        Debug.Log("System.Random: " + sw.ElapsedMilliseconds);

        // 171ms, 40ms
        sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < values.Length; i++) {
            values[i] = Random.value;
        }
        sw.Stop();
        Debug.Log("Unity.Random.value: " + sw.ElapsedMilliseconds);

        // 226ms, 71ms
        var rm = new Meisui.Random.MersenneTwister(1234);
        sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < values.Length; i++) {
            values[i] = (float)rm.genrand_real2();
        }
        sw.Stop();
        Debug.Log("MT Managed: " + sw.ElapsedMilliseconds);

        // 351ms, 91ms
        var rrm = new RamjetMath.MersenneTwister(1234);
        sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < values.Length; i++) {
            values[i] = rrm.genrand_real2();
        }
        sw.Stop();
        Debug.Log("MT Burst: " + sw.ElapsedMilliseconds);

        // 359ms, 15ms
        sw = System.Diagnostics.Stopwatch.StartNew();
        var mtj = new MTJob();
        mtj.Values = values;
        mtj.Random = rrm;
        mtj.Schedule().Complete();
        sw.Stop();
        rrm.Dispose();
        Debug.Log("MT Burst Job: " + sw.ElapsedMilliseconds);

        // 162ms, 76
        var xor = new Xorshift(1234);
        sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < values.Length; i++) {
            values[i] = xor.NextFloat();
        }
        sw.Stop();
        Debug.Log("XOR Managed: " + sw.ElapsedMilliseconds);

        // 1107, 21ms
        sw = System.Diagnostics.Stopwatch.StartNew();
        var xorb = new XorshiftBurst(1234);
        var xorj = new XorShiftJob();
        xorj.Values = values;
        xorj.Random = xorb;
        xorj.Schedule().Complete();
        sw.Stop();
        Debug.Log("XOR Burst Job: " + sw.ElapsedMilliseconds);


        float min = 2f;
        float max = -1f;

        for (int i = 0; i < numVals; i++) {
            float val = values[i];
            if (val > max) {
                max = val;
            }
            if (val < min) {
                min = val;
            }
            data[i] = new Color(val, val, val, 1f);
        }

        Debug.Log(min + ", " + max);

        _tex.SetPixels(0, 0, res, res, data, 0);
        _tex.Apply();

        values.Dispose();
    }


    private void OnGUI() {
        GUI.DrawTexture(new Rect(0f, 0f, res, res), _tex);
    }

    [BurstCompile]
    public struct MTJob : IJob {
        [WriteOnly] public NativeArray<float> Values;
        public RamjetMath.MersenneTwister Random;

        public void Execute() {
            for (int i = 0; i < Values.Length; i++) {
                Values[i] = Random.genrand_real2();
            }
        }
    }

    [BurstCompile]
    public struct XorShiftJob : IJob {
        [WriteOnly] public NativeArray<float> Values;
        public XorshiftBurst Random;

        public void Execute() {
            for (int i = 0; i < Values.Length; i++) {
                Values[i] = Random.NextFloat();
            }
        }
    }
}