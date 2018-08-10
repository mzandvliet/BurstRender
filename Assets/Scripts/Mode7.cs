/*
	Programming Pseudo 3D Planes aka MODE7 (C++)
	Javidx9, Youtube
	https://www.youtube.com/watch?v=ybLZyY655iY
 */

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
// using Unity.Burst;
// using Unity.Jobs;
// using Unity.Collections;
// using Unity.Mathematics;

public class Mode7 : MonoBehaviour {
	private Texture2D _screen;
	private Texture2D _map;

	private readonly int2 ScreenSize = new int2(320, 240);
    private readonly int2 MapSize = new int2(128, 128);

	private void Start () {
		_screen = new Texture2D(ScreenSize.x, ScreenSize.y, TextureFormat.ARGB32, false, true);
        _map = new Texture2D(MapSize.x, MapSize.y, TextureFormat.ARGB32, false, true);
		_screen.filterMode = FilterMode.Point;
        _map.filterMode = FilterMode.Point;
	}
	
	private void Update() {
		
	}

	private void OnGUI() {
		GUI.DrawTexture(new Rect(0f, 0f, 128f, 128f), _map, ScaleMode.ScaleToFit);
        GUI.DrawTexture(new Rect(128f, 0f, 640, 480), _screen, ScaleMode.ScaleToFit);
	}
}
