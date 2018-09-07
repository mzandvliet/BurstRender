using System;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using Unity.Mathematics;
using UnityEngine;

namespace RamjetMath {
    [System.Serializable]
    public struct Ray3f : System.IEquatable<Ray3f>, IFormattable {
        public float3 origin;
        public float3 direction;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Ray3f(float3 origin, float3 direction) {
            this.origin = origin;
            this.direction = direction;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(Ray3f other) {
            return this.origin.Equals(other.origin) && this.direction.Equals(other.direction);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string ToString(string format, IFormatProvider formatProvider) {
            return string.Format("{pos: {0}, dir: {1}}", origin, direction);
        }
    }

    // [System.Serializable]
    // public struct float3 : System.IEquatable<float3>, IFormattable {
    //     public float x;
    //     public float y;
    //     public float z;

    //     public static readonly float3 zero = new float3(0f, 0f, 0f);

    //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
    //     public float3(float x, float y, float z) {
    //         this.x = x;
    //         this.y = y;
    //         this.z = z;
    //     }

    //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
    //     public static float3 operator +(float3 lhs, float3 rhs) {
    //         return new float3(lhs.x + rhs.x, lhs.y + rhs.y, lhs.z + rhs.z);
    //     }

    //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
    //     public static float3 operator -(float3 lhs, float3 rhs) {
    //         return new float3(lhs.x - rhs.x, lhs.y - rhs.y, lhs.z - rhs.z);
    //     }

    //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
    //     public static float3 operator *(float3 lhs, float3 rhs) {
    //         return new float3(lhs.x * rhs.x, lhs.y * rhs.y, lhs.z * rhs.z);
    //     }

    //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
    //     public static float3 operator /(float3 lhs, float3 rhs) {
    //         return new float3(lhs.x / rhs.x, lhs.y / rhs.y, lhs.z / rhs.z);
    //     }

    //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
    //     public static float3 operator *(float3 lhs, float rhs) {
    //         return new float3(lhs.x * rhs, lhs.y * rhs, lhs.z * rhs);
    //     }

    //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
    //     public static float3 operator /(float3 lhs, float rhs) {
    //         return new float3(lhs.x / rhs, lhs.y / rhs, lhs.z / rhs);
    //     }

    //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
    //     public static float Length(float3 v) {
    //         return math.sqrt(v.x * v.x + v.y * v.y + v.z * v.z);
    //     }

    //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
    //     public static float SqrLength(float3 v) {
    //         return v.x * v.x + v.y * v.y + v.z * v.z;
    //     }

    //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
    //     public static float Dot(float3 lhs, float3 rhs) {
    //         return lhs.x * rhs.x + lhs.y * rhs.y + lhs.z * rhs.z;
    //     }

    //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
    //     public static float3 Cross(float3 lhs, float3 rhs) {
    //         return new float3(
    //             lhs.y * rhs.z - lhs.z * rhs.y,
    //             lhs.x * rhs.z - lhs.z * rhs.x,
    //             lhs.x * rhs.y - lhs.y * rhs.x
    //         );
    //     }

    //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
    //     public bool Equals(float3 other) {
    //         return this.x == other.x && this.y == other.y && this.z == other.z;
    //     }

    //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
    //     public string ToString(string format, IFormatProvider formatProvider) {
    //         return string.Format("{x: {0:0.000}, y: {1:0.000}, z: {2:0.000}}", x, y, z);
    //     }

    //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
    //     public static Vector3 ToVector3 (float3 v) {
    //         return new Vector3(v.x, v.y, v.z);
    //     }
    // }
}
