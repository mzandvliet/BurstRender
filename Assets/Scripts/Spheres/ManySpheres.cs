using UnityEngine;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.Rendering;
using System.Runtime.InteropServices;

// https://gist.github.com/sebbbi/a599d7896aa3ad36642145d54459f32b

public class ManySpheres : MonoBehaviour {
    [SerializeField] private Material _sphereMat;
    [SerializeField] private Camera _camera;

    private const int _numSpheres = 1024;
    //private NativeArray<float3> _spheres;
    private float3[] _spheres;

    private CommandBuffer _commandBuffer;
    // Can we wrap a NativeArray pointer around a computebuffer pointer? _sphereBuffer.GetNativeBufferPtr
    private ComputeBuffer _sphereBuffer;
    private ComputeBuffer _indexBuffer;


    private void Awake() {
        //_spheres = new NativeArray<float3>(_numSpheres, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        _spheres = new float3[_numSpheres];
        for (int i = 0; i < _numSpheres; i++) {
            //_spheres[i] = new float3(i % 32, i / 32, 0f) * 2f;
            _spheres[i] = Random.insideUnitSphere * 10f;
        }

        var indices = new uint[] { 2, 1, 0, 2, 3, 1 }; // { 0, 1, 2, 1, 3, 2 };
        _indexBuffer = new ComputeBuffer(6, sizeof(uint));
        _indexBuffer.SetData(indices);
        _sphereMat.SetBuffer("indices", _indexBuffer);

        _sphereBuffer = new ComputeBuffer(_numSpheres, Marshal.SizeOf(typeof(float3)));
        _sphereBuffer.SetData(_spheres);
        _sphereMat.SetBuffer("spheres", _sphereBuffer);

        _commandBuffer = new CommandBuffer();
        _commandBuffer.DrawProcedural(transform.localToWorldMatrix, _sphereMat, 0, MeshTopology.Triangles, _numSpheres * 6);
        _camera.AddCommandBuffer(CameraEvent.AfterForwardOpaque, _commandBuffer);
    }

    private void OnDrawGizmos() {
        if (!Application.isPlaying) {
            return;
        }

        for (int i = 0; i < _spheres.Length; i++) {
            Gizmos.DrawWireSphere(_spheres[i], 0.5f);
        }
    }

    private void OnDestroy() {
        //_spheres.Dispose();
        _sphereBuffer.Dispose();
        _indexBuffer.Dispose();
    }

    private void Update() {
        
    }
}