using UnityEngine;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;

/* Todo: Unity.Mathematics.Random was just introduced
    Threadsafe, only uses single uint of state, has generators
    for many types we want.

    Note that here too, burst jobs are much faster the **second time** they run.
 */

public class NoiseTest : MonoBehaviour {
    Texture2D _tex;

    const int res = 2048;
    const int numVals = res * res;

    private void Start() {
        ProfileRNGs();
    }

    private void Update() {
        if (Input.GetKeyDown(KeyCode.Space)) {
            ProfileRNGs();
        }
    }

    private void TestXorShiftBurst() {
        XorshiftBurst xor = new XorshiftBurst(5132512, 3292391, 109854, 587295);
        for (int i = 0; i < 128; i++) {
            Debug.Log(xor.NextInt(0, 128));
            Debug.Log(xor._seed0 + ", " + xor._seed1 + ", " + xor._seed2 + ", " + xor._seed3);
        }
    }

    private void ProfileRNGs() {
        // Time it takes for many RNG implementations to fill a buffer with random 32 bit floats with distribution [0,1)
        // For each RNG I list two times measures on my machine: editor with safetychecks, build without safetychecks
        // If burst job is used, it is single-threaded. Obviously, using multiple threads with multiple RNGs is faster.

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
            values[i] = UnityEngine.Random.value;
        }
        sw.Stop();
        Debug.Log("Unity.Random: " + sw.ElapsedMilliseconds);

        // 245ms, 84ms
        sw = System.Diagnostics.Stopwatch.StartNew();
        var uMathRng = new Unity.Mathematics.Random(1234);
        for (int i = 0; i < values.Length; i++) {
            values[i] = uMathRng.NextFloat();
        }
        sw.Stop();
        Debug.Log("Unity.Mathematics.Random: " + sw.ElapsedMilliseconds);

        // 248ms, 10ms
        sw = System.Diagnostics.Stopwatch.StartNew();
        var umrj = new UMathRngJob();
        umrj.Random = uMathRng;
        umrj.Values = values;
        umrj.Schedule().Complete();
        sw.Stop();
        Debug.Log("Unity.Mathematics.Random BurstJob: " + sw.ElapsedMilliseconds);

        // 226ms, 71ms
        var rm = new Meisui.Random.MersenneTwister(1234);
        sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < values.Length; i++) {
            values[i] = (float)rm.genrand_real2();
        }
        sw.Stop();
        Debug.Log("MersenneTwister Managed: " + sw.ElapsedMilliseconds);

        // 351ms, 91ms
        var rrm = new Ramjet.MersenneTwister(1234);
        sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < values.Length; i++) {
            values[i] = rrm.genrand_real2();
        }
        sw.Stop();
        Debug.Log("MersenneTwister Burst: " + sw.ElapsedMilliseconds);

        // 359ms, 15ms
        sw = System.Diagnostics.Stopwatch.StartNew();
        var mtj = new MTJob();
        mtj.Values = values;
        mtj.Random = rrm;
        mtj.Schedule().Complete();
        sw.Stop();
        rrm.Dispose();
        Debug.Log("MersenneTwister BurstJob: " + sw.ElapsedMilliseconds);

        // 162ms, 76
        var xor = new Xorshift(1234);
        sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < values.Length; i++) {
            values[i] = xor.NextFloat();
        }
        sw.Stop();
        Debug.Log("XORShift Managed: " + sw.ElapsedMilliseconds);

        // 1107, 21ms
        sw = System.Diagnostics.Stopwatch.StartNew();
        var xorb = new XorshiftBurst(1234);
        var xorj = new XorShiftJob();
        xorj.Values = values;
        xorj.Random = xorb;
        xorj.Schedule().Complete();
        sw.Stop();
        Debug.Log("XORShift BurstJob: " + sw.ElapsedMilliseconds);


        // Find min and max values in buffer, and turn it into a texture for visual inspection
        // Todo: do this for all RNGs, not just for the last one to generate values.

        // float min = 2f;
        // float max = -1f;

        // _tex = new Texture2D(res, res, TextureFormat.ARGB32, false, true);
        // var data = new Color[res * res];

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

    private void OnDrawGizmos() {
        var rng = new Unity.Mathematics.Random(1234); 
        for (int i = 0; i < 1024; i++) {
            var pos = RandomInUnitDisk(ref rng) * 10f;
            Gizmos.DrawSphere(pos, 0.1f);
        }
    }

    private static float3 RandomInUnitDisk(ref Unity.Mathematics.Random rng) {
        float theta = rng.NextFloat() * Mathf.PI * 2f;
        float r = math.sqrt(rng.NextFloat());
        return new float3(math.cos(theta) * r, math.sin(theta) * r, 0f);
    }

    [BurstCompile]
    public struct UMathRngJob : IJob {
        [WriteOnly] public NativeArray<float> Values;
        public Unity.Mathematics.Random Random;

        public void Execute() {
            for (int i = 0; i < Values.Length; i++) {
                Values[i] = Random.NextFloat();
            }
        }
    }

    [BurstCompile]
    public struct MTJob : IJob {
        [WriteOnly] public NativeArray<float> Values;
        public Ramjet.MersenneTwister Random;

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