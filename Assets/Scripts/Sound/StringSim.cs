using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using Random = Unity.Mathematics.Random;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections.LowLevel.Unsafe;

/* 
    Todo:

    Fix build error (burst chokes)

    Optimization:

    https://gist.github.com/LotteMakesStuff/c2f9b764b15f74d14c00ceb4214356b4
    For fast interfacing with the audio api data structure. I could probably
    manage to pin a managed float[], wrap a NativeArray<Float> around it.

    Implement a NativeCircularBuffer
 */

public class StringSim : MonoBehaviour {
    private NativeArray<float> _waveBuffer;

    private NativeArray<float> _clipData;
    private float[] _clipDataManaged;
    private AudioClip _clip;
    private AudioSource _source;

    void Start() {
        _source = GetComponent<AudioSource>();

        int numSeconds = 100;
        int sr = AudioSettings.outputSampleRate;
        int numSamples = sr * numSeconds;
        _clipData = new NativeArray<float>(numSamples, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        _clipDataManaged = new float[numSamples];

        int freq = 86;
        int period = sr/freq;

        _waveBuffer = new NativeArray<float>(period, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        _clip = AudioClip.Create("MyStringSound", numSamples, 1, sr, false);
        _source.clip = _clip;
    }

    private void OnDestroy() {
        _waveBuffer.Dispose();
        _clipData.Dispose();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space)) {
            Generate();
            _source.Play();
        }
    }

    private void OnAudioFilterRead(float[] buffer, int channels) {
        // Todo: race the buffer with burst jobs.
        // Can you schedule burst jobs from inside this function?
    }

    void Generate() {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var job = new GenerateSoundJob();
        job.buffer = _waveBuffer;
        job.clip = _clipData;
        job.period = _waveBuffer.Length;
        job.Schedule().Complete();
        watch.Stop();

        Copy(_clipData, _clipDataManaged);

        Debug.Log("Time taken: " + watch.ElapsedMilliseconds + "ms");
        
        _clip.SetData(_clipDataManaged, 0);
        _source.Play();
    }

    private unsafe static void Copy(NativeArray<float> from, float[] to) {
        fixed(void* toPointer = to) {
            UnsafeUtility.MemCpy(
                toPointer, 
                NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(from),
                from.Length * (long)UnsafeUtility.SizeOf<float>());
        }
    }

    // private unsafe static void ManagedCopy(NativeArray<float> from, float[] to) {
    //     for (int i = 0; i < from.Length; i++) {
    //         to[i] = from[i];
    //     }
    // }

    [BurstCompile]
    public struct GenerateSoundJob : IJob {
        public NativeArray<float> buffer;
        public NativeArray<float> clip;

        public int period;
        
        public void Execute() {
            Random rng = new Random(1234);
            PluckRandom(buffer, ref rng);

            int bufIndex = 0;
            for (int i = 0; i < clip.Length; i++) {
                clip[i] = buffer[bufIndex];
                buffer[bufIndex] = (buffer[bufIndex] + buffer[(bufIndex + 1) % period]) * 0.5f;
                bufIndex = (bufIndex + 1) % period;
            }
        }
    }

    private static void PluckRandom(NativeArray<float> buffer, ref Random rng) {
        for (int i = 0; i < buffer.Length; i++) {
            buffer[i] = -1f + 2f * rng.NextFloat();
        }
    }
}
