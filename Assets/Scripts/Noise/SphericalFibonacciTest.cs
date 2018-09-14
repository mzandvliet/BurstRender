using UnityEngine;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;

/*
    https://www.irit.fr/~David.Vanderhaeghe/M2IGAI-CO/2016-g1/docs/spherical_fibonacci_mapping.pdf

    Todo:
    - Study how this algorithm works, write it in a couple of different ways
    - We can try to algebraically reformulate this to using complex arithmetic and no transcendentals
    - I want one that just samples a single index for me out of n, on demand

    Use cases:
    - Generating random points on the unit sphere, such as when sampling diffuse lighting in a raytracer
        - basically, integration of functions over spherical domains
    - Density tracking for an atmosphere simulation on the surface of a sphere (alternative to projected cube grids?)
 */

public class SphericalFibonacciTest : MonoBehaviour {
    private Vector3[] _points;

    private void Awake() {
        _points = new Vector3[128];
        SphericalFibComplex(ref _points);
    }
    private void OnDrawGizmos() {
        if (!Application.isPlaying) {
            return;
        }

        for (int i = 0; i < _points.Length; i++) {
            var p = _points[i] * 10f;
            Gizmos.color = Color.white;
            Gizmos.DrawLine(Vector3.zero, p);
            Gizmos.color = Color.blue;
            Gizmos.DrawSphere(p, 0.2f);
        }
    }

    void SphericalFib(ref Vector3[] output) {
        float n = output.Length / 2.0f;
        float pi = Mathf.PI;
        float dphi = pi * (3.0f - math.sqrt(5.0f));
        float phi = 0f;
        float dz = 1.0f / n;
        float z = 1.0f - dz / 2.0f;
        int[] indices = new int[output.Length];

        for (int j = 0; j < n; j++) {
            float zj = z;
            float thetaj = math.acos(zj);
            float phij = phi % (2f * pi);
            z = z - dz;
            phi = phi + dphi;

            // spherical -> cartesian, with r = 1
            output[j] = new Vector3((float)(math.cos(phij) * math.sin(thetaj)),
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

    void SphericalFibComplex(ref Vector3[] output) {
        float n = output.Length / 2.0f;
        float dz = 1.0f / n;
        float z = 1.0f - dz / 2.0f;
        int[] indices = new int[output.Length];

        float2 phi = new float2(1,0);
        float2 dphi;
        math.sincos(Mathf.PI * (3.0f - math.sqrt(5.0f)), out dphi.x, out dphi.y);

        for (int j = 0; j < n; j++) {
            float zj = z;
            float thetaj = math.acos(zj);
            z = z - dz;
            phi = ComplexF.Mul(dphi, phi);

            float sinThetaJ = math.sin(thetaj);

            output[j] = new Vector3(phi.x * sinThetaJ,
                                    zj,
                                    sinThetaJ * phi.y);
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

public static class ComplexF {
    public const float Tau = Mathf.PI * 2f;

    public static float2 Mul(float2 a, float2 b) {
        return new float2(
            a.x * b.x - a.y * b.y,
            a.x * b.y + a.y * b.x);
    }

    public static float2 GetRotor(float freq, int samplerate) {
        float phaseStep = (Tau * freq) / samplerate;

        return new float2(
            math.cos(phaseStep),
            math.sin(phaseStep));
    }
}

public static class ComplexI {
    public static int2 Mul(int2 a, int2 b) {
        return new int2(
            a.x * b.x - a.y * b.y,
            a.x * b.y + a.y * b.x);
    }
}