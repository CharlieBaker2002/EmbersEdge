using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

[CanEditMultipleObjects]
[CustomEditor(typeof(LRToPolygon))]
public class LRToPolygonEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        if (GUILayout.Button("BuildPolgyonCollider2D"))
        {
            ((LRToPolygon)target).Update();
        }
    }
}
