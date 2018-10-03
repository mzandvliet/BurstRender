using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

public class StringSim : MonoBehaviour {
    private float[] _waveBuffer;

    private float[] _clipData;
    private AudioClip _clip;
    private AudioSource _source;

    void Start() {
        _source = GetComponent<AudioSource>();

        int numSeconds = 10;
        int sr = AudioSettings.outputSampleRate;
        int numSamples = sr * numSeconds;
        _clipData = new float[numSamples];

        int freq = 86;
        int period = sr/freq;

        _waveBuffer = new float[period];
        Random rng = new Random(1234);

        var watch = System.Diagnostics.Stopwatch.StartNew();

        for (int i = 0; i < _waveBuffer.Length; i++) {
            _waveBuffer[i] = -1f + 2f * rng.NextFloat();
        }

        int bufIndex = 0;
        for (int i = 0; i < numSamples; i++) {
            _clipData[i] = _waveBuffer[bufIndex];
            _waveBuffer[bufIndex] = (_waveBuffer[bufIndex] + _waveBuffer[(bufIndex + 1) % period]) * 0.5f;
            bufIndex = (bufIndex+1) % period;
        }

        watch.Stop();
        Debug.Log(watch.ElapsedMilliseconds);

        _clip = AudioClip.Create("MyStringSound", numSamples, 1, sr, false);
        _clip.SetData(_clipData, 0);

        _source.clip = _clip;
        _source.Play();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space)) {
            _source.Play();
        }
    }
}
