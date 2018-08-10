/*
	Programming Pseudo 3D Planes aka MODE7 (C++)
	Javidx9, Youtube
	https://www.youtube.com/watch?v=ybLZyY655iY

	Todo:
	- rewrite using linalg arithmetic
	- use Burst for faster arithmetic

	- bonus if you take the frustum diagram and
	render it on top of the map

	Notes:
	- y coordinates are the other way around here
	- wraparound depends on texture sampler/impporter settings
	- 1/z=inf issue isn't handled yet

	See Mode8.cs for a different take on the same code
 */

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;

public class Mode7 : MonoBehaviour {
    [SerializeField] private Texture2D _ground;
    [SerializeField] private Texture2D _sky;
	[SerializeField] private bool _proceduralTextures;

    [SerializeField] private float _fovHalf = Mathf.PI / 4f;
    [SerializeField] private float _near = 0.01f;
    [SerializeField] private float _far = 0.1f;

    private Texture2D _screen;

	private float _worldX;
    private float _worldY;
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
		float move = Input.GetAxis("Vertical");

		_worldRot += turn * Time.deltaTime;
        _worldX += move * math.cos(_worldRot) * 0.2f * Time.deltaTime;
        _worldY += move * math.sin(_worldRot) * 0.2f * Time.deltaTime;
	}

	private void Render() {
		// Calculate frustum points

        float farX1 = _worldX + math.cos(_worldRot - _fovHalf) * _far;
        float farY1 = _worldY + math.sin(_worldRot - _fovHalf) * _far;

        float farX2 = _worldX + math.cos(_worldRot + _fovHalf) * _far;
        float farY2 = _worldY + math.sin(_worldRot + _fovHalf) * _far;

        float nearX1 = _worldX + math.cos(_worldRot - _fovHalf) * _near;
        float nearY1 = _worldY + math.sin(_worldRot - _fovHalf) * _near;

        float nearX2 = _worldX + math.cos(_worldRot + _fovHalf) * _near;
        float nearY2 = _worldY + math.sin(_worldRot + _fovHalf) * _near;

        // Sample pixels per horizontal scanline
        // Ground takes up half the screen, with vanishing point in the middle
		// Sky is the same idea, but flipped upside down

        int halfHeight = _screen.height / 2;
        for (int y = 0; y < halfHeight; y++) {
            float sampleDepth = 1f - (float)y / ((float)halfHeight);

            float startX = (farX1 - nearX1) / sampleDepth + nearX1;
            float startY = (farY1 - nearY1) / sampleDepth + nearY1;

            float endX = (farX2 - nearX2) / sampleDepth + nearX2;
            float endY = (farY2 - nearY2) / sampleDepth + nearY2;

            for (int x = 0; x < _screen.width; x++) {
                float sampleWidth = (float)x / (float)_screen.width;
                float sampleX = (endX - startX) * sampleWidth + startX;
                float sampleY = (endY - startY) * sampleWidth + startY;

                Color gcol = _ground.GetPixel((int)(sampleX * _ground.width), (int)(sampleY * _ground.height));
                Color scol = _sky.GetPixel((int)(sampleX * _sky.width), (int)(sampleY * _sky.height));

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
