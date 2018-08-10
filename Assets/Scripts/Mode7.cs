/*
	Programming Pseudo 3D Planes aka MODE7 (C++)
	Javidx9, Youtube
	https://www.youtube.com/watch?v=ybLZyY655iY

	Todo:
	- first write in same way, commit
	- then, rewrite using linalg arithmetic
 */

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;

public class Mode7 : MonoBehaviour {
	private Texture2D _screen;
	private Texture2D _map;

	private readonly int2 ScreenSize = new int2(320, 240);
    private readonly int2 MapSize = new int2(1024, 1024);

	private float _worldX;
    private float _worldY;
    private float _worldRot;
	private float _fovHalf = Mathf.PI / 4f;
	private float _near = 0.01f;
	private float _far = 0.1f;

	private void Start () {
		_screen = new Texture2D(ScreenSize.x, ScreenSize.y, TextureFormat.ARGB32, false, true);
        _map = new Texture2D(MapSize.x, MapSize.y, TextureFormat.ARGB32, false, true);
		_screen.filterMode = FilterMode.Point;
        _map.filterMode = FilterMode.Point;
		
		CreateMap(_map);
	}

	private void CreateMap(Texture2D map) {
		for (int x = 0; x < map.width; x+=32) {
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
	
	private void Update() {
		float farX1 = _worldX + math.cos(_worldRot - _fovHalf) * _far;
        float farY1 = _worldY + math.sin(_worldRot - _fovHalf) * _far;

        float farX2 = _worldX + math.cos(_worldRot + _fovHalf) * _far;
        float farY2 = _worldY + math.sin(_worldRot + _fovHalf) * _far;

        float nearX1 = _worldX + math.cos(_worldRot - _fovHalf) * _near;
        float nearY1 = _worldY + math.sin(_worldRot - _fovHalf) * _near;

        float nearX2 = _worldX + math.cos(_worldRot + _fovHalf) * _near;
        float nearY2 = _worldY + math.sin(_worldRot + _fovHalf) * _near;

		// Map takes up half the screen, with vanishing point in the middle
		// Process per horizontal scanline
		int halfHeight = _screen.height / 2;
		for (int y = 0; y < halfHeight; y++) {
			float sampleDepth = (float)y / ((float)halfHeight / 2f);

			float startX = (farX1 - nearX1) * sampleDepth + nearX1;
            float startY = (farY1 - nearY1) * sampleDepth + nearY1;

			float endX = (farX2 - nearX2) * sampleDepth + nearX2;
            float endY = (farY2 - nearY2) * sampleDepth + nearY2;

			for (int x = 0; x < _screen.width; x++) {
				float sampleWidth = (float)x / (float)_screen.width;
				float sampleX = (endX - startX) * sampleWidth + startX;
                float sampleY = (endY - startY) * sampleWidth + startY;
				
				Color col = _map.GetPixel((int)(sampleX * _map.width), (int)(sampleY * _map.height));

				_screen.SetPixel(x, y, col);
			}
        }

		_screen.Apply();
	}

	private void OnGUI() {
		GUI.DrawTexture(new Rect(0f, 0f, _map.width * 0.5f, _map.height * 0.5f), _map, ScaleMode.ScaleToFit);
        GUI.DrawTexture(new Rect(_map.width * 0.5f, 0f, _screen.width * 2f, _screen.height * 2f), _screen, ScaleMode.ScaleToFit);
	}
}
