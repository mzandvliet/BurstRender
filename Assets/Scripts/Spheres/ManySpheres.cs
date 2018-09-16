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

    private const int _numSpheres = 8192 * 128;
    private NativeArray<float3> _spheres;

    private CommandBuffer _commandBuffer;
    // Can we wrap a NativeArray pointer around a computebuffer pointer? _sphereBuffer.GetNativeBufferPtr
    private ComputeBuffer _sphereBuffer;
    private ComputeBuffer _indexBuffer;

    private JobHandle _updateHandle;


    private void Awake() {
        Debug.LogFormat("Updating and rendering {0} impostors", _numSpheres);

        _spheres = new NativeArray<float3>(_numSpheres, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        for (int i = 0; i < _numSpheres; i++) {
            _spheres[i] = UnityEngine.Random.insideUnitSphere * 400f;
        }

        var indices = new uint[] { 2, 1, 0, 2, 3, 1 }; // { 0, 1, 2, 1, 3, 2 }; reversed
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

    private void Update() {
        var j = new MoveSpheresJob() {
            Spheres = _spheres,
            Rotor = quaternion.AxisAngle(new float3(0, 1, 0), 1f * Time.deltaTime)
        };
        _updateHandle = j.Schedule(_spheres.Length, 64);
    }

    private void LateUpdate() {
        _updateHandle.Complete();
        _sphereBuffer.SetData(_spheres);
    }

    private void OnDestroy() {
        _spheres.Dispose();
        _sphereBuffer.Dispose();
        _indexBuffer.Dispose();
    }

    [BurstCompile]
    public struct MoveSpheresJob : IJobParallelFor {
        public NativeArray<float3> Spheres;
        public quaternion Rotor;

        public void Execute(int i) {
            Spheres[i] = math.mul(Rotor, Spheres[i]);
        }
    }
}