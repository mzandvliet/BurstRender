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

    Visualize wave table behaviour

    Implement some late-80s physical model approximations, control them using
    Midi and Oculus Touch.

    Optimization:

    https://gist.github.com/LotteMakesStuff/c2f9b764b15f74d14c00ceb4214356b4
    For fast interfacing with the audio api data structure. I could probably
    manage to pin a managed float[], wrap a NativeArray<Float> around it.

    Implement a NativeCircularBuffer
 */

// public class StringSim : MonoBehaviour {
//     private NativeArray<float> _waveBuffer;
//     private NativeArray<float> _frameBuffer;

//     private AudioSource _source;
//     private GenerateSoundJob _job;

//     void Start() {
//         _source = gameObject.AddComponent<AudioSource>();

//         int sr = 44100;
//         AudioSettings.outputSampleRate = sr;
//         int dspBufferSize = 128, dspNumBuffers = 2;
//         //AudioSettings.GetDSPBufferSize(out dspBufferSize, out dspNumBuffers);
//         AudioSettings.SetDSPBufferSize(dspBufferSize, dspNumBuffers);

//         int freq = 86;
//         int period = sr/freq;

//         _frameBuffer = new NativeArray<float>(dspBufferSize, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
//         _waveBuffer = new NativeArray<float>(period, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

//         Random rng = new Random(1234);
//         PluckRandom(_waveBuffer, ref rng);

//         _job = new GenerateSoundJob();
//         _job.buffer = _waveBuffer;
//         _job.result = _frameBuffer;
//         _job.period = _waveBuffer.Length;

//         _job.Schedule().Complete();
//     }

//     private void OnDestroy() {
//         _waveBuffer.Dispose();
//         _frameBuffer.Dispose();
//     }

//     private JobHandle _handle;

//     private void OnAudioFilterRead(float[] buffer, int channels) {
//         // Todo: race the buffer with burst jobs.
//         // Can you schedule burst jobs from inside this function? Seems not...

//         _handle.Complete();
//         Copy(_frameBuffer, buffer);
//         _handle = _job.Schedule();

//         /*
//         Jikes:
//         UnityException: CreateJobReflectionData can only be called from the main thread.
//         Constructors and field initializers will be executed from the loading thread when loading a scene.
//         Don't use this function in the constructor or field initializers, instead move initialization code to the Awake or Start function.
//         Unity.Jobs.LowLevel.Unsafe.JobsUtility.CreateJobReflectionData (System.Type type, Unity.Jobs.LowLevel.Unsafe.JobType jobType, System.Object managedJobFunction0, System.Object managedJobFunction1, System.Object managedJobFunction2) (at C:/buildslave/unity/build/Runtime/Jobs/ScriptBindings/Jobs.bindings.cs:96)
//         Unity.Jobs.IJobExtensions+JobStruct`1[T].Initialize () (at C:/buildslave/unity/build/Runtime/Jobs/Managed/IJob.cs:23)
//         Unity.Jobs.IJobExtensions.Schedule[T] (T jobData, Unity.Jobs.JobHandle dependsOn) (at C:/buildslave/unity/build/Runtime/Jobs/Managed/IJob.cs:36)
//         StringSim.OnAudioFilterRead (System.Single[] buffer, System.Int32 channels) (at Assets/Scripts/Sound/StringSim.cs:86)

//         If I can somehow generate job reflection data in Start, before scheduling from here, would that work?

//         Nope, Schedule() can only be called from the main thread.

//         Can I... from main thread schedule a bunch of jobs that never complete, and communicate to them from here? :P

//         Easiest fix for now: run app at 120fps, schedule jobs from render thread
//         Generate ahead of time so you don't run out of buffers, can always ditch
//         and regenerate them if it turns out you still have time.
//         */
//     }

//     private unsafe static void Copy(NativeArray<float> from, float[] to) {
//         fixed(void* toPointer = to) {
//             UnsafeUtility.MemCpy(
//                 toPointer, 
//                 NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(from),
//                 from.Length * (long)UnsafeUtility.SizeOf<float>());
//         }
//     }

//     [BurstCompile]
//     public struct GenerateSoundJob : IJob {
//         public NativeArray<float> buffer;
//         public NativeArray<float> result;

//         public int period;

//         public void Execute() {
//             int bufIndex = 0;
//             for (int i = 0; i < result.Length; i++) {
//                 result[i] = buffer[bufIndex];
//                 buffer[bufIndex] = (buffer[bufIndex] + buffer[(bufIndex + 1) % period]) * 0.5f;
//                 bufIndex = (bufIndex + 1) % period;
//             }
//         }
//     }

//     private static void PluckRandom(NativeArray<float> buffer, ref Random rng) {
//         for (int i = 0; i < buffer.Length; i++) {
//             buffer[i] = rng.NextFloat(-1f, 1f);
//         }
//     }
// }

// public class StringSim : MonoBehaviour {
//     private NativeArray<float> _waveBuffer;

//     private NativeArray<float> _clipData;
//     private float[] _clipDataManaged;
//     private AudioClip _clip;
//     private AudioSource _source;
//     private GenerateSoundJob _job;

//     void Start() {
//         _source = gameObject.AddComponent<AudioSource>();

//         int numSeconds = 30;
//         int numSamples = AudioSettings.outputSampleRate * numSeconds;
//         _clipData = new NativeArray<float>(numSamples, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
//         _clipDataManaged = new float[numSamples];

//         int freq = 110;
//         int period = AudioSettings.outputSampleRate / freq;

//         _waveBuffer = new NativeArray<float>(period, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
//         _clip = AudioClip.Create("MyStringSound", numSamples, 1, AudioSettings.outputSampleRate, false);
//         _source.clip = _clip;

//         Generate();
//         _source.Play();
//     }

//     private void OnDestroy() {
//         _waveBuffer.Dispose();
//         _clipData.Dispose();
//     }

//     private JobHandle _handle;

//     void Generate() {
//         var watch = System.Diagnostics.Stopwatch.StartNew();

//         Random rng = new Random(1234);
//         PluckRandom(_waveBuffer, ref rng);

//         var job = new GenerateSoundJob();
//         job.buffer = _waveBuffer;
//         job.result = _clipData;
//         job.period = _waveBuffer.Length;
//         job.Schedule().Complete();

//         watch.Stop();

//         Copy(_clipData, _clipDataManaged);

//         Debug.Log("Time taken: " + watch.ElapsedMilliseconds + "ms");

//         _clip.SetData(_clipDataManaged, 0);
//     }

//     private unsafe static void Copy(NativeArray<float> from, float[] to) {
//         fixed (void* toPointer = to) {
//             UnsafeUtility.MemCpy(
//                 toPointer,
//                 NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(from),
//                 from.Length * (long)UnsafeUtility.SizeOf<float>());
//         }
//     }

//     [BurstCompile]
//     public struct GenerateSoundJob : IJob {
//         public NativeArray<float> buffer;
//         public NativeArray<float> result;

//         public int period;

//         public void Execute() {
//             int bufIndex = 0;
//             for (int i = 0; i < result.Length; i++) {
//                 result[i] = buffer[bufIndex];
//                 buffer[bufIndex] = (buffer[bufIndex] + buffer[(bufIndex + 1) % period]) * 0.5f;
//                 bufIndex = (bufIndex + 1) % period;
//             }
//         }
//     }

//     private static void PluckRandom(NativeArray<float> buffer, ref Random rng) {
//         for (int i = 0; i < buffer.Length; i++) {
//             buffer[i] = rng.NextFloat(-1f, 1f);
//         }
//     }
// }

public class StringSim : MonoBehaviour {
    private NativeArray<float> _clipData;
    private NativeArray<float2> _waveBuffer;
    private Random _rng;

    void Start() {
        _clipData = new NativeArray<float>(64, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        _waveBuffer = new NativeArray<float2>(64, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        _rng = new Random(1234);

        PluckRandom(_waveBuffer, ref _rng);
    }

    private void OnDestroy() {
        _waveBuffer.Dispose();
        _clipData.Dispose();
    }

    private int _waveIndex;
    private int _clipIndex;
    private void Update() {
        if (Input.GetKeyDown(KeyCode.Space)) {
            PluckRandom(_waveBuffer, ref _rng);
        }

        _clipData[_clipIndex] = _waveBuffer[_waveIndex].x;
        _clipIndex = (_clipIndex+1)%_clipData.Length;

        // Original
        //_waveBuffer[_waveIndex] = (_waveBuffer[_waveIndex] + _waveBuffer[(_waveIndex+1)%_waveBuffer.Length]) * 0.5f;

        float2 wavePart = _waveBuffer[_waveIndex];
        wavePart.y = (wavePart.y + (_waveBuffer[(_waveIndex + 1) % _waveBuffer.Length].x - _waveBuffer[_waveIndex].x)) / 2f;
        wavePart.y *= 0.9f;
        wavePart.x += wavePart.y * 0.5f;
        _waveBuffer[_waveIndex] = wavePart;

        _waveIndex = (_waveIndex+1)%_waveBuffer.Length;
    }

    private static void PluckRandom(NativeArray<float2> buffer, ref Random rng) {
        for (int i = 0; i < buffer.Length; i++) {
            buffer[i] = new float2(rng.NextFloat(-1f, 1f), 0f);
        }
    }

    private void OnDrawGizmos() {
        if (!Application.isPlaying) {
            return;
        }

        float xScale = 1f/16f;

        Gizmos.DrawLine(Vector3.zero, Vector3.right * (_waveBuffer.Length * xScale));
        for (int i = 0; i < _waveBuffer.Length; i++) {
            Gizmos.color = Color.white;
            Vector3 p = new Vector3(i * xScale, 0f, 0f);
            Gizmos.DrawLine(p - Vector3.up, p + Vector3.up);
            Vector3 pos = p + new Vector3(0f, _waveBuffer[i].x, 0f);
            Gizmos.color = Color.blue;
            Gizmos.DrawRay(pos, new Vector3(0f, _waveBuffer[i].y, 0f));
            Gizmos.color = Color.red;
            Vector3 pos2 = new Vector3((i+1) * xScale, 0f, 0f) + new Vector3(0f, _waveBuffer[(i+1)%_waveBuffer.Length].x, 0f);
            Gizmos.DrawLine(pos, pos2);
            Gizmos.DrawSphere(pos, 0.02f);
        }

        // for (int i = _clipData.Length-1; i >= 0; i--) {
        //     Gizmos.color = i == _clipData.Length - 1 ? Color.red : Color.white;
        //     Gizmos.DrawRay(new Vector3(i / 16f, -1.0f, 0f), new Vector3(0f, _clipData[(_clipIndex + i)%_clipData.Length], 0f));
        // }
    }
}