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

public class StringSim : MonoBehaviour {
    private NativeArray<float> _waveBuffer;
    private NativeArray<float> _frameBuffer;

    // private NativeArray<float> _clipData;
    // private float[] _clipDataManaged;
    // private AudioClip _clip;
    private AudioSource _source;
    private GenerateSoundJob _job;

    void Start() {
        _source = GetComponent<AudioSource>();

        // AudioConfiguration config = new AudioConfiguration();
        // config.dspBufferSize = 128;
        // config.numRealVoices = 32;
        // config.numVirtualVoices = 128;
        // config.sampleRate = 44100;
        // config.speakerMode = AudioSpeakerMode.Stereo;
        // AudioSettings.Reset(config);

        int sr = 44100;
        AudioSettings.outputSampleRate = 44100;
        int dspBufferSize = 128, dspNumBuffers = 2;
        //AudioSettings.GetDSPBufferSize(out dspBufferSize, out dspNumBuffers);
        AudioSettings.SetDSPBufferSize(dspBufferSize, dspNumBuffers);
        
        // int numSeconds = 100;
        // int numSamples = sr * numSeconds;
        // _clipData = new NativeArray<float>(numSamples, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        // _clipDataManaged = new float[numSamples];

        int freq = 86;
        int period = sr/freq;

        
        _frameBuffer = new NativeArray<float>(dspBufferSize, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

        _waveBuffer = new NativeArray<float>(period, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        // _clip = AudioClip.Create("MyStringSound", numSamples, 1, sr, false);
        // _source.clip = _clip;

        Random rng = new Random(1234);
        PluckRandom(_waveBuffer, ref rng);

        _job = new GenerateSoundJob();
        _job.buffer = _waveBuffer;
        _job.result = _frameBuffer;
        _job.period = _waveBuffer.Length;

        _job.Schedule().Complete();
    }

    private void OnDestroy() {
        _waveBuffer.Dispose();
        //_clipData.Dispose();

        _frameBuffer.Dispose();
    }

    void Update()
    {
        // if (Input.GetKeyDown(KeyCode.Space)) {
        //     Generate();
        //     _source.Play();
        // }
    }

    private JobHandle _handle;

    private void OnAudioFilterRead(float[] buffer, int channels) {
        // Todo: race the buffer with burst jobs.
        // Can you schedule burst jobs from inside this function? Seems not...

        _handle.Complete();
        Copy(_frameBuffer, buffer);
        _handle = _job.Schedule();

        /*
        Jikes:
        UnityException: CreateJobReflectionData can only be called from the main thread.
        Constructors and field initializers will be executed from the loading thread when loading a scene.
        Don't use this function in the constructor or field initializers, instead move initialization code to the Awake or Start function.
        Unity.Jobs.LowLevel.Unsafe.JobsUtility.CreateJobReflectionData (System.Type type, Unity.Jobs.LowLevel.Unsafe.JobType jobType, System.Object managedJobFunction0, System.Object managedJobFunction1, System.Object managedJobFunction2) (at C:/buildslave/unity/build/Runtime/Jobs/ScriptBindings/Jobs.bindings.cs:96)
        Unity.Jobs.IJobExtensions+JobStruct`1[T].Initialize () (at C:/buildslave/unity/build/Runtime/Jobs/Managed/IJob.cs:23)
        Unity.Jobs.IJobExtensions.Schedule[T] (T jobData, Unity.Jobs.JobHandle dependsOn) (at C:/buildslave/unity/build/Runtime/Jobs/Managed/IJob.cs:36)
        StringSim.OnAudioFilterRead (System.Single[] buffer, System.Int32 channels) (at Assets/Scripts/Sound/StringSim.cs:86)

        If I can somehow generate job reflection data in Start, before scheduling from here, would that work?
        
        Nope, Schedule() can only be called from the main thread.

        Can I... from main thread schedule a bunch of jobs that never complete, and communicate to them from here? :P

        Easiest fix for now: run app at 120fps, schedule jobs from render thread
        Generate ahead of time so you don't run out of buffers, can always ditch
        and regenerate them if it turns out you still have time.
        */
    }

    // void Generate() {
    //     var watch = System.Diagnostics.Stopwatch.StartNew();
    //     var job = new GenerateSoundJob();
    //     job.buffer = _waveBuffer;
    //     job.clip = _clipData;
    //     job.period = _waveBuffer.Length;
    //     job.Schedule().Complete();
    //     watch.Stop();

    //     Copy(_clipData, _clipDataManaged);

    //     Debug.Log("Time taken: " + watch.ElapsedMilliseconds + "ms");
        
    //     _clip.SetData(_clipDataManaged, 0);
    //     _source.Play();
    // }

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
        public NativeArray<float> result;

        public int period;
        
        public void Execute() {
            int bufIndex = 0;
            for (int i = 0; i < result.Length; i++) {
                result[i] = buffer[bufIndex];
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
