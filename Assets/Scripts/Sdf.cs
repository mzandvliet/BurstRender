using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace Tracing {
    public static class SDF {
        // Hey, if you trace these prims with dual numbers, you can include normal information in the returned samples

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Sphere(float3 localPos, float rad) {
            return math.length(localPos) - rad;
        }
    }
}
