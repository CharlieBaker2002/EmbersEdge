using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;

[CustomEditor(typeof(Move))]
[CanEditMultipleObjects]
public class MoveEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
       
        if (GUILayout.Button("TrackMouse"))
        {
            ((Move)target).SetPositions();
            StartDirty();
        }

        if (GUILayout.Button("ContinueTrackingMouse"))
        {
            ((Move)target).ContinuePositions();
            StartDirty();
        }

        if (GUILayout.Button("Undo"))
        {
            ((Move)target).Undo();
            EditorUtility.SetDirty(((Move)target).gameObject);
            PrefabUtility.RecordPrefabInstancePropertyModifications((Move)target);
        }

        if (GUILayout.Button("Randomise"))
        {
            ((Move)target).Randomise();
            EditorUtility.SetDirty(((Move)target).gameObject);
            PrefabUtility.RecordPrefabInstancePropertyModifications((Move)target);
        }
    }

    private async void StartDirty()
    {
        await WaitForDirty();
    }

    private async Task WaitForDirty()
    {
        await Task.Delay(Mathf.CeilToInt(((Move)target).filmDuration * 1000 + 1500));
        EditorUtility.SetDirty(((Move)target).gameObject);
        PrefabUtility.RecordPrefabInstancePropertyModifications((Move)target);
    }
}
