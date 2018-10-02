using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

public static class Util {
    public static void ToTexture2D(NativeArray<float3> screen, Texture2D tex, int2 resolution) {
        Color[] colors = new Color[screen.Length];

        for (int i = 0; i < screen.Length; i++) {
            var c = screen[i];
            colors[i] = new Color(c.x, c.y, c.z, 1f);
        }

        tex.SetPixels(0, 0, (int)resolution.x, (int)resolution.y, colors, 0);
        tex.Apply();
    }

    public static void ExportImage(Texture2D texture, string folder) {
        var bytes = texture.EncodeToJPG(100);
        System.IO.File.WriteAllBytes(
            System.IO.Path.Combine(folder, string.Format("render_{0}.png", System.DateTime.Now.ToFileTimeUtc())),
            bytes);
    }
}