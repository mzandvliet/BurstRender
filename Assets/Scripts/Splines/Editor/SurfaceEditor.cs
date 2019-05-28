using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(Surface)), CanEditMultipleObjects]
public class SurfaceEditor : Editor {
    // private int _selectedPoint = 0;


    // void OnEnable() {
    //     _selectedPoint = 0;
    // }

    protected virtual void OnSceneGUI() {
        Surface surface = (Surface)target;

        // Debug.Log("Nearest: " + HandleUtility.nearestControl);

        for (int i = 0; i < surface.Points.Length; i++) {
            // if (i == _selectedPoint) {
                EditorGUI.BeginChangeCheck();
                Vector3 newTargetPosition = Handles.PositionHandle(surface.Points[i], Quaternion.identity);
                if (EditorGUI.EndChangeCheck()) {
                    Undo.RecordObject(surface, "Move Surface Point Position");
                    surface.Points[i] = newTargetPosition;
                }
            // } else {
            //     Handles.SphereHandleCap(HandleUtility.AddControl(), surface.Points[i], Quaternion.identity, 0.1f, EventType.Repaint);
            // }
        }
    }
}