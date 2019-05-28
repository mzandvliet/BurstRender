using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Orbit : MonoBehaviour
{
    [SerializeField] private Vector3 _center;
    [SerializeField] private float _distance;


    // Start is called before the first frame update
    void Start()
    {
        transform.position = _center - Vector3.forward * _distance;
    }

    // Update is called once per frame
    void Update()
    {
        transform.RotateAround(_center, Vector3.up, 10f * Time.deltaTime);
    }
}
