using UnityEngine;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Rng = Unity.Mathematics.Random;

[ExecuteInEditMode]
public class Surface : MonoBehaviour {
    [SerializeField] private float3[] _points = new float3[] {
        new float3(0, 0, 0),
        new float3(0, 1, 0),
        new float3(0, 2, 0),
        new float3(0, 3, 0),

        new float3(1, 0, 0),
        new float3(1, 1, 0),
        new float3(1, 2, 0),
        new float3(1, 3, 0),

        new float3(2, 0, 0),
        new float3(2, 1, 0),
        new float3(2, 2, 0),
        new float3(2, 3, 0),

        new float3(3, 0, 0),
        new float3(3, 1, 0),
        new float3(3, 2, 0),
        new float3(3, 3, 0),
    };

    public float3[] Points {
        get { return _points; }
        set { _points = value; }
    }

    private void OnDrawGizmos() {
        Gizmos.color = Color.white;
        for (int c = 0; c < 4; c++) {
            for (int p = 0; p < 3; p++) {
                Gizmos.DrawLine(_points[c * 4 + p], _points[c * 4 + p + 1]);
            }
        }

        for (int p = 0; p < 3; p++)  {
            for (int c = 0; c < 4; c++) {
                Gizmos.DrawLine(_points[p * 4 + c], _points[(p+1) * 4 + c]);
            }
        }
    }
}