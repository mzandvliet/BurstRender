using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;

public static class Util {
    public static unsafe void Copy(Vector3[] destination, NativeArray<float3> source) {
        fixed (void* vertexArrayPointer = destination) {
            UnsafeUtility.MemCpy(
                vertexArrayPointer,
                NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(source),
                destination.Length * (long)UnsafeUtility.SizeOf<float3>());
        }
    }

    public static unsafe void Copy(Color[] destination, NativeArray<float4> source) {
        fixed (void* vertexArrayPointer = destination) {
            UnsafeUtility.MemCpy(
                vertexArrayPointer,
                NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(source),
                destination.Length * (long)UnsafeUtility.SizeOf<float4>());
        }
    }

    public static unsafe void Copy(Vector2[] destination, NativeArray<float2> source) {
        fixed (void* vertexArrayPointer = destination) {
            UnsafeUtility.MemCpy(
                vertexArrayPointer,
                NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(source),
                destination.Length * (long)UnsafeUtility.SizeOf<float2>());
        }
    }

    public static unsafe void Copy(int[] destination, NativeArray<int> source) {
        fixed (void* vertexArrayPointer = destination) {
            UnsafeUtility.MemCpy(
                vertexArrayPointer,
                NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(source),
                destination.Length * (long)UnsafeUtility.SizeOf<int>());
        }
    }
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

    public static float3 HomogeneousNormalize(float4 v) {
        return new float3(v.x / v.w, v.y / v.w, v.z / v.w);
    }

    public static float3 PerspectiveDivide(float3 v) {
        return new float3(v.x / v.z, v.y / v.z, 1f);
    }

    public static Vector3 ToVec3(float2 p) {
        return new Vector3(p.x, p.y, 0f);
    }
}