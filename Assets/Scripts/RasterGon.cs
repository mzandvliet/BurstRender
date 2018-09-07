/*
	Code-It-Yourself! 3D Graphics Engine Part #1 - Triangles & Projection
	Javidx9, Youtube
	https://www.youtube.com/watch?v=ih20l3pJoeU&t=833s

    Problem 1: defining the basic types

    Syntax here is more verbose than the C code
    That, and in C the array-in-struct is on the stack as embedded
    value type, but here here on the heap as reference. :/

    Finally, triangles

    Maybe we should jump immediately to vertex buffers with triangles
    as index lists?

    Notes:
    - With triangle rasterization, winding order is always important. It
    is used to encode the orientation, front-facing, back facing, which
    is then used for culling and such.
    - Geometric algebra brings this winding order to the front.
 */

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;

namespace Raster {
    public struct vec3f {
        public float x, y, z;

        public vec3f(float x, float y, float z) {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public static vec3f operator + (vec3f a, vec3f b) {
            return new vec3f(a.x + b.x, a.y + b.y, a.z + b.z);
        }

        public static vec3f operator - (vec3f a, vec3f b) {
            return new vec3f(a.x - b.x, a.y - b.y, a.z - b.z);
        }

        public static vec3f operator * (vec3f a, float b) {
            return new vec3f(a.x * b, a.y * b, a.z * b);
        }

        public static float Dot(vec3f a, vec3f b) {
            return a.x * b.x + a.y * b.y + a.z * b.z;
        }
    }

    // Todo: how to init the list/array structures?

    public struct Triangle {
        public NativeArray<vec3f> Verts;

        public Triangle(vec3f a, vec3f b, vec3f c) {
            Verts = new NativeArray<vec3f>(3, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        }
    }

    public struct Mesh {
        public NativeArray<Triangle> Tris;
    }

    public class RasterGon : MonoBehaviour {
        private Texture2D _screen;

        private void Awake() {
            _screen = new Texture2D(320, 240, TextureFormat.ARGB32, false, true);
            _screen.filterMode = FilterMode.Point;

            

            // var cubeMesh = new Mesh() {
            //     Tris = new List<Triangle>() {
            //         // South
            //         new Triangle() {
            //             Verts = new vec3f[] { new vec3f(0f, 0f, 0f), new vec3f(0f, 1f, 0f), new vec3f(1f, 1f, 0f) }
            //         },
            //         new Triangle() {
            //             Verts = new vec3f[] { new vec3f(0f, 0f, 0f), new vec3f(1f, 1f, 0f), new vec3f(1f, 0f, 0f) }
            //         }
            //     }
            // };
        }

        private void OnGUI() {
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), _screen, ScaleMode.ScaleToFit);
        }

        private void Update() {
        }
    }

}

