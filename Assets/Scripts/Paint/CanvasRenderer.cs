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
    [SerializeField] private Material _blitClearCanvasMaterial;
    [SerializeField] private Material _blitAddLayerMaterial;
    
    private Camera _camera;

    private bool _clearCanvas;
    private RenderTexture _newPaintLayer;

    private void Awake() {
        _clearCanvas = true;
        _camera = gameObject.GetComponent<Camera>();
    }

    private void OnDestroy() {
    }

    public void Clear() {
        _clearCanvas = true;
    }

    public void Add(RenderTexture paintLayer) {
        _newPaintLayer = paintLayer;
    }

    public void OnRenderImage(RenderTexture source, RenderTexture destination) {
        if (_clearCanvas) {
            _blitAddLayerMaterial.SetTexture("_MainTex", source);
            Graphics.Blit(source, destination, _blitClearCanvasMaterial);
            _clearCanvas = false;
        }
        else if (_newPaintLayer != null) {
            _blitAddLayerMaterial.SetTexture("_MainTex", source);
            _blitAddLayerMaterial.SetTexture("_PaintTex", _newPaintLayer);
            Graphics.Blit(source, destination, _blitAddLayerMaterial);
            _newPaintLayer = null;
        }
        else {
            Graphics.Blit(source, destination);
        }
    }
}