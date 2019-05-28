using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;

/* Todo: Is it possible to generic memcopy implementations, where Source, Dest : struct? */

public static class Util {
    public static unsafe void CopyToManaged(NativeArray<float3> source, Vector3[] destination) {
        fixed (void* vertexArrayPointer = destination) {
            UnsafeUtility.MemCpy(
                vertexArrayPointer,
                NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(source),
                destination.Length * (long)UnsafeUtility.SizeOf<float3>());
        }
    }

    public static unsafe void CopyToNative(Vector3[] source, NativeArray<float3> destination) {
        if (source.Length != destination.Length) {
            throw new System.ArgumentException("Source length is not equal to destination length");
        }
        fixed (void* sourcePointer = source) {
            UnsafeUtility.MemCpy(
                NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(destination),
                sourcePointer,
                destination.Length * (long)UnsafeUtility.SizeOf<float3>());
        }
    }

    public static unsafe void CopyToManaged(NativeArray<float4> source, Color[] destination) {
        fixed (void* vertexArrayPointer = destination) {
            UnsafeUtility.MemCpy(
                vertexArrayPointer,
                NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(source),
                destination.Length * (long)UnsafeUtility.SizeOf<float4>());
        }
    }

    public static unsafe void CopyToManaged(NativeArray<float2> source, Vector2[] destination) {
        fixed (void* vertexArrayPointer = destination) {
            UnsafeUtility.MemCpy(
                vertexArrayPointer,
                NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(source),
                destination.Length * (long)UnsafeUtility.SizeOf<float2>());
        }
    }

    public static unsafe void CopyToManaged(NativeArray<int> source, int[] destination) {
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

    public static float3 PerspectiveDivide(float4 v) {
        return new float3(v.x / v.w, v.y / v.w, v.z); // Todo:  also v.z / v.w ??
    }

    public static Vector3 ToVec3(float2 p) {
        return new Vector3(p.x, p.y, 0f);
    }
}