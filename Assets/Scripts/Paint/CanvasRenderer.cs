using UnityEngine;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Rng = Unity.Mathematics.Random;
using Ramjet;
using UnityEngine.Rendering;
using System.Runtime.InteropServices;

public class CanvasRenderer : MonoBehaviour {
    [SerializeField] private Painter _painter;
    [SerializeField] private Material _blitClearCanvasMaterial;
    [SerializeField] private Material _blitAddLayerMaterial;
    

    private Camera _camera;
    private CommandBuffer _commandBuffer;

    private bool _clearCanvas;

    private void Awake() {
        _camera = gameObject.GetComponent<Camera>();

        _camera.orthographicSize = 4f;
        _camera.clearFlags = CameraClearFlags.Nothing;
        _camera.orthographic = true;

        _clearCanvas = true;
    }

    private void Update() {
        if (Time.frameCount % 60 == 0) {
            _clearCanvas = true;
        }
    }

    void OnRenderImage(RenderTexture source, RenderTexture destination) {
        if (_clearCanvas) {
            _blitClearCanvasMaterial.SetTexture("_MainTex", source);
            Graphics.Blit(source, source, _blitClearCanvasMaterial);
            _clearCanvas = false;
        }

        _blitAddLayerMaterial.SetTexture("_MainTex", source);
        _blitAddLayerMaterial.SetTexture("_PaintTex", _painter.GetCanvas());
        Graphics.Blit(source, destination, _blitAddLayerMaterial);
    }
}