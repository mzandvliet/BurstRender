/*
	Programming Pseudo 3D Planes aka MODE7 (C++)
	Javidx9, Youtube
	https://www.youtube.com/watch?v=ybLZyY655iY

	Todo:
	- use Burst for faster arithmetic

	- bonus if you take the frustum diagram and
	render it on top of the map

	Notes:
	- y coordinates are the other way around here
	- wraparound depends on texture sampler/impporter settings
	- 1/z=inf issue isn't handled yet
 */

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;

public class Mode8 : MonoBehaviour {
    [SerializeField] private Texture2D _ground;
    [SerializeField] private Texture2D _sky;
	[SerializeField] private bool _proceduralTextures;

    [SerializeField] private float _fovHalf = Mathf.PI / 4f;
    [SerializeField] private float _near = 0.01f;
    [SerializeField] private float _far = 0.1f;

    [SerializeField] private float _moveSpeed = 0.2f;

    private Texture2D _screen;

	private float2 _worldPos;
    private float _worldRot;

	private void Awake () {
		_screen = new Texture2D(320, 240, TextureFormat.ARGB32, false, true);
		
		if (_proceduralTextures) {
            _ground = new Texture2D(1024, 1024, TextureFormat.ARGB32, false, true);
            _sky = _ground;
            CreateTexture(_ground);
		}

        _screen.filterMode = FilterMode.Point;
        _ground.filterMode = FilterMode.Point;
		_sky.filterMode = FilterMode.Point;
	}

    private void OnGUI() {
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), _screen, ScaleMode.ScaleToFit);
    }
	
	private void Update() {
		Simulate();
		Render();
	}

	private void Simulate() {
		float turn = Input.GetAxis("Horizontal");
		float walk = Input.GetAxis("Vertical");

        _worldRot += turn * Time.deltaTime;

        float2 move;
        math.sincos(_worldRot, out move.x, out move.y);
        _worldPos += move * walk * _moveSpeed * Time.deltaTime;
	}

	private void Render() {
		// Calculate frustum points

        float rotL = _worldRot - _fovHalf;
        float rotR = _worldRot + _fovHalf;

        float2 frustumL;
        float2 frustumR;
        math.sincos(rotL, out frustumL.x, out frustumL.y);
        math.sincos(rotR, out frustumR.x, out frustumR.y);

        float2 farL = _worldPos + frustumL * _far;
        float2 farR = _worldPos + frustumR * _far;
        float2 nearL = _worldPos + frustumL * _near;
        float2 nearR = _worldPos + frustumR * _near;

        // Sample pixels per horizontal scanline
        // Ground takes up half the screen, with vanishing point in the middle
		// Sky is the same idea, but flipped upside down

        int halfHeight = _screen.height / 2;
        for (int y = 0; y < halfHeight; y++) {
            float z = 1f - (float)y / ((float)halfHeight);

            float2 start = nearL + (farL - nearL) / z;
            float2 end = nearR + (farR - nearR) / z;

            for (int x = 0; x < _screen.width; x++) {
                float sampleWidth = (float)x / (float)_screen.width;
                float2 sample = (end - start) * sampleWidth + start;

                Color gcol = _ground.GetPixel((int)(sample.x * _ground.width), (int)(sample.y * _ground.height));
                Color scol = _sky.GetPixel((int)(sample.x * _sky.width), (int)(sample.y * _sky.height));

                _screen.SetPixel(x, y, gcol);
                _screen.SetPixel(x, _screen.height - y, scol);
            }
        }

        _screen.Apply();
	}

    private void CreateTexture(Texture2D map) {
        for (int x = 0; x < map.width; x += 32) {
            for (int y = 0; y < map.height; y++) {
                // Draw horizontal lines
                map.SetPixel(x, y, Color.magenta);
                map.SetPixel(x + 1, y, Color.magenta);
                map.SetPixel(x - 1, y, Color.magenta);

                // Draw vertical lines (note: only works if map.width == map.height)
                map.SetPixel(y, x, Color.blue);
                map.SetPixel(y, x + 1, Color.blue);
                map.SetPixel(y, x - 1, Color.blue);
            }
        }
        map.Apply();
    }
}
