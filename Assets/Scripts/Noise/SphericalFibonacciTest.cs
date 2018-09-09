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

    Use cases:
    - Generating random points on the unit sphere, such as when sampling diffuse lighting in a raytracer
    - Density tracking for an atmosphere simulation on the surface of a sphere (alternative to projected cube grids?)
 */

public class SphericalFibonacciTest : MonoBehaviour {
    private Vector3[] _points;

    private void Awake() {
        _points = new Vector3[128];
        SphericalFib(ref _points);
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
}