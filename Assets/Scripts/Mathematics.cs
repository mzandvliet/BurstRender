using Unity.Mathematics;
using Unity.Collections;
using Random = Unity.Mathematics.Random;

namespace Ramjet {
    public static class Math {
        public const float Tau = 6.2831853071795864769f;
        public const float Pi = Tau / 2f;

        public static float2 ToNormalizedCoords(uint screenIdx, uint2 resolution) {
            return new float2(
                (screenIdx % resolution.x), // Bug: wait what, no normalize?
                (screenIdx / resolution.x)
            );
        }

        public static uint2 To2DCoords(uint screenIdx, uint2 resolution) {
            return new uint2(
                (screenIdx % resolution.x),
                (screenIdx / resolution.x)
            );
        }
        
        public static uint To1Dindex(uint2 coord, uint2 resolution) {
            return coord.y * resolution.x + coord.x;
        }

        public static float3 RandomInUnitDisk(ref Random rng) {
            float theta = rng.NextFloat() * Tau;
            float r = math.sqrt(rng.NextFloat());
            return new float3(math.cos(theta) * r, math.sin(theta) * r, 0f);
        }

        public static void GenerateFibonacciSphere(NativeArray<float3> output) {
            float n = output.Length / 2.0f;
            float dphi = Pi * (3.0f - math.sqrt(5.0f));
            float phi = 0f;
            float dz = 1.0f / n;
            float z = 1.0f - dz / 2.0f;
            int[] indices = new int[output.Length];

            for (int j = 0; j < n; j++) {
                float zj = z;
                float thetaj = math.acos(zj);
                float phij = phi % Tau;
                z = z - dz;
                phi = phi + dphi;

                // spherical -> cartesian, with r = 1
                output[j] = new float3((float)(math.cos(phij) * math.sin(thetaj)),
                                        (float)(zj),
                                        (float)(math.sin(thetaj) * math.sin(phij)));
                indices[j] = j;
            }

            // The code above only covers a hemisphere, this mirrors it into a sphere.
            for (int i = 0; i < n; i++) {
                var vz = output[i];
                vz.y *= -1;
                output[output.Length - i - 1] = vz;
                indices[i + output.Length / 2] = i + output.Length / 2;
            }
        }
    }

}
