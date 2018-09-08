using UnityEngine;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;

public class NoiseTest : MonoBehaviour {
    Texture2D _tex;

    const int res = 2048;
    
    private void Awake() {
        
        _tex = new Texture2D(res, res, TextureFormat.ARGB32, false, true);

        var data = new Color[res * res];

        var r = new RamjetMath.MersenneTwister(1234);

        float min = 2f;
        float max = -1f;
        
        for (int i = 0; i < data.Length; i++) {
            float val = (float)r.genrand_real2();
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
    }
    private void OnGUI() {
        GUI.DrawTexture(new Rect(0f, 0f, res, res), _tex);
    }
}