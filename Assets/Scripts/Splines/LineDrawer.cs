using UnityEngine;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.Rendering;
using System.Runtime.InteropServices;

// https://gist.github.com/sebbbi/a599d7896aa3ad36642145d54459f32b

// Todo: turn this into a spiffy line renderer that does millions of them

public class LineDrawer : MonoBehaviour {
    [SerializeField] private Material _mat;
    [SerializeField] private Camera _camera;

    private const int _numLines = 128 * 32;
    private const int _numPointsPerLine = 2;
    private NativeArray<float3> _lines;

    private CommandBuffer _commandBuffer;
    private ComputeBuffer _lineBuffer;
    // private ComputeBuffer _colorBuffer;
    private ComputeBuffer _indexBuffer;

    private JobHandle _updateHandle;

    private int _count;


    private void Awake() {
        _lines = new NativeArray<float3>(_numLines * _numPointsPerLine, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

        var indices = new uint[] { 2, 1, 0, 2, 3, 1 }; // { 0, 1, 2, 1, 3, 2 }; reversed
        _indexBuffer = new ComputeBuffer(6, sizeof(uint));
        _indexBuffer.SetData(indices);
        _mat.SetBuffer("indices", _indexBuffer);

        _lineBuffer = new ComputeBuffer(_numLines, Marshal.SizeOf(typeof(float3)));
        _lineBuffer.SetData(_lines);
        _mat.SetBuffer("lines", _lineBuffer);
        _mat.SetInt("numLines", _numLines);
        _mat.SetInt("numPointsPerLine", _numPointsPerLine);

        _commandBuffer = new CommandBuffer();
        _commandBuffer.DrawProcedural(transform.localToWorldMatrix, _mat, 0, MeshTopology.Triangles, (_numLines*_numPointsPerLine) * 6);
        _camera.AddCommandBuffer(CameraEvent.AfterForwardOpaque, _commandBuffer);
    }

    public void Add(float3 a, float3 b, Color c) {
        if (_count < _numLines) {
            _lines[_count * _numPointsPerLine + 0] = a;
            _lines[_count * _numPointsPerLine + 1] = b;
            _count++;
        }
    }

    public void Begin() {
        _count = 0;
    }

    public void End() {
        // Todo: instead tell command buffer to draw up to max used index instead
        for (int i = _count; i < _lines.Length; i++) {
            _lines[i] = 0;
        }
    }

    private void LateUpdate() {
        // _updateHandle.Complete();
        _lineBuffer.SetData(_lines);
    }

    private void OnDestroy() {
        _lines.Dispose();
        _lineBuffer.Dispose();
        _indexBuffer.Dispose();
    }
}