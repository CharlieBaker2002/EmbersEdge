using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

[CanEditMultipleObjects]
[CustomEditor(typeof(EmbersEdge))]
public class EEEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
       
        if (GUILayout.Button("Acco"))
        {
            ((EmbersEdge)target).StartCoroutine(((EmbersEdge)target).Acco());
        }
        if (GUILayout.Button("DeAcco"))
        {
            ((EmbersEdge)target).StartCoroutine(((EmbersEdge)target).DeAcco());
        }
        if (GUILayout.Button("Dissapear"))
        {
            ((EmbersEdge)target).Dissapear();
        }

        GUILayout.Space(10);
        if (GUILayout.Button("Randomise"))
        {
            ((EmbersEdge)target).StartCoroutine(((EmbersEdge)target).Randomise());
        }
        if (GUILayout.Button("Relax"))
        {
            ((EmbersEdge)target).StartCoroutine(((EmbersEdge)target).Relax());
        }
        
    }
}
