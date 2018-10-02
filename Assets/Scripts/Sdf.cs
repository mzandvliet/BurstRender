using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace Tracing {
    public static class SDF {
        // Hey, if you trace these prims with dual numbers, you can include normal information in the returned samples

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Sphere(float3 localPos, float rad) {
            return math.length(localPos) - rad;
        }

        // For pure hit/miss detection, this would do:
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool SphereHit(float3 localPos, float rad) {
            return math.lengthsq(localPos) - rad * rad <= 0f;

        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float3 ModXZ(float3 localPos, float2 v) {
            return new float3(
                localPos.x % v.x- 0.5f * v.x,
                localPos.y,
                localPos.z % v.y - 0.5f * v.y);
        }
    }
}
