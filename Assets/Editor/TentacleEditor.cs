using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

[CanEditMultipleObjects]
[CustomEditor(typeof(Tentacle))]
public class TentacleEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        if (GUILayout.Button("Randomise"))
        {
            ((Tentacle)target).StartCoroutine(((Tentacle)target).Randomise());
        }
        if (GUILayout.Button("Relax"))
        {
            ((Tentacle)target).StartCoroutine(((Tentacle)target).Relax());
        }
        if (GUILayout.Button("Attack"))
        {
            ((Tentacle)target).StartCoroutine(((Tentacle)target).Attack(Random.insideUnitCircle));
        }
        if (GUILayout.Button("Lengthen"))
        {
            ((Tentacle)target).ChangeN((Mathf.CeilToInt(((Tentacle)target).N * 1.2f)));
        }
    }
}

[CustomEditor(typeof(TentacleD0))]
public class TentacleD0Editor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        if (GUILayout.Button("Randomise"))
        {
            ((Tentacle)target).StartCoroutine(((Tentacle)target).Randomise());
        }
        if (GUILayout.Button("Relax"))
        {
            ((Tentacle)target).StartCoroutine(((Tentacle)target).Relax());
        }
        if (GUILayout.Button("Attack"))
        {
            ((Tentacle)target).StartCoroutine(((Tentacle)target).Attack(Random.insideUnitCircle));
        }
        if (GUILayout.Button("Lengthen"))
        {
            ((Tentacle)target).ChangeN((Mathf.CeilToInt(((Tentacle)target).N * 1.2f)));
        }
    }
}