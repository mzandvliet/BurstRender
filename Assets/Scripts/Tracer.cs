using UnityEngine;
using Unity.Collections;
using Unity.Burst;
using Unity.Jobs;
using Unity.Mathematics;
using RamjetMath;

using ScreenBuffer = Unity.Collections.NativeArray<float>;
using System.Runtime.CompilerServices;

/* 
Todo:
- Experiment with different ways to generate rays
- This way getting from a Burst-style screenbuffer to a cpu-texture to a gpu render is dumb
*/

public class Tracer : MonoBehaviour {
    private ScreenBuffer _screen;

    private ClearJob _clear;
    private TraceJob _trace;

    private JobHandle _renderHandle;

    private static readonly CameraInfo Cam = new CameraInfo(
        new int2(512, 256),
        new float3(-2.0f, -1.0f, 1.0f),
        new float3(4f, 0f, 0f),
        new float3(0f, 2f, 0f));

    private Color[] _colors;
    private Texture2D _tex;


    private void Awake() {
        _screen = new ScreenBuffer(Cam.resolution.x * Cam.resolution.y, Allocator.Persistent, NativeArrayOptions.ClearMemory);

        _clear = new ClearJob();
        _clear.Buffer = _screen;

        _trace = new TraceJob();
        _trace.Screen = _screen;
        _trace.Cam = Cam;

        _colors = new Color[Cam.resolution.x * Cam.resolution.y];
        _tex = new Texture2D(Cam.resolution.x, Cam.resolution.y, TextureFormat.ARGB32, false, true);
    }

    private void OnDestroy() {
        _screen.Dispose();
    }

    private void Update() {
        var h = new JobHandle();
        h = _clear.Schedule(_screen.Length, 64, h);
        h = _trace.Schedule(_screen.Length, 64, h);
        _renderHandle = h;

        _renderHandle.Complete();
    }

    private void LateUpdate() {
        ToTexture2D(_screen, _colors, _tex);
    }

    private void OnGUI() {
        GUI.DrawTexture(new Rect(0f, 0f, _tex.width * 2f, _tex.height * 2f), _tex);
    }

    // private void OnDrawGizmos() {
    //     var res = Cam.resolution;

    //     for (int i = 0; i < res.x * res.y; i++) {
    //         var screenPos = ToXY(i, Cam) / (float2)Cam.resolution;
    //         var r = MakeRay(screenPos, Cam);

    //         Gizmos.color = new Color(screenPos.x, 0f, screenPos.y, 1f);
    //         Gizmos.DrawRay(r.origin, r.direction);
    //     }
    // }

    [BurstCompile]
    private struct ClearJob : IJobParallelFor {
        public NativeArray<float> Buffer;
        public void Execute(int i) {
            Buffer[i] = 0f;
        }
    }

    [BurstCompile]
    private struct TraceJob : IJobParallelFor {
        public ScreenBuffer Screen;
        public CameraInfo Cam;

        public void Execute(int i) {
            // Create a camera ray
            // sample distance to sphere at origin

            var screenPos = ToXY(i, Cam) / (float2)Cam.resolution;
            var r = MakeRay(screenPos, Cam);

            Screen[i] = 0f;

            var spherePos = new float3(0f, 0f, 2f);

            float depth = 0;
            for (int d = 0; d < 8; d++) {
                var p = r.origin + r.direction * depth;
                p -= spherePos;

                float dist = Trace.Sphere(p, 1f);

                if (dist < 0.001f) {
                    Screen[i] = 1f;
                    return;
                }
                depth += dist;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int2 ToXY(int screenIdx, CameraInfo cam) {
        return new int2(
            (screenIdx % cam.resolution.x),
            (screenIdx / cam.resolution.x)
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Ray3f MakeRay(float2 screenPos, CameraInfo cam) {
        return new Ray3f(
            new float3(),
            math.normalize(cam.lowerLeft + cam.hori * screenPos.x + cam.vert * screenPos.y));
    }

    private static void ToTexture2D(ScreenBuffer screen, Color[] colors, Texture2D tex) {
        for (int i = 0; i < screen.Length; i++) {
            float gray = screen[i];
            colors[i] = new Color(gray, gray, gray, 1f);
        }

        tex.SetPixels(0, 0, Cam.resolution.x, Cam.resolution.y, colors, 0);
        tex.Apply();
    }

    private struct CameraInfo {
        public int2 resolution;
        public float3 lowerLeft;
        public float3 hori;
        public float3 vert;

        public CameraInfo(int2 res, float3 ll, float3 ho, float3 ve) {
            resolution = res;
            lowerLeft = ll;
            hori = ho;
            vert = ve;
        }
    }
}



public static class Trace {
    // Hey, if you trace these prims with dual numbers, you can include normal information in the returned samples

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Sphere(float3 localPos, float rad) {
        return math.length(localPos) - rad;
    }
}


/*
Hmm, wanted this class for easy multidim indexing
but it's non-trivial to write
*/
// public struct Shape : System.IDisposable {
//     public readonly NativeArray<int> Dimensions;

//     public Shape(params int[] dims) {
//         Dimensions = new NativeArray<int>(dims, Allocator.Persistent);
//     }

//     public void Dispose() {
//         Dimensions.Dispose();
//     }
// }