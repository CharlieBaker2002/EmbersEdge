using System.Collections;
using System.Collections.Generic;
using ScriptableObjects.Blueprints.BPScripts;
using UnityEngine;
using UnityEditor;

[CanEditMultipleObjects]
[CustomEditor(typeof(Blueprint))]
public class BPEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
    }
}

[CanEditMultipleObjects]
[CustomEditor(typeof(BlueprintManager))]
public class BlueprintManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        if (GUILayout.Button("ToDiscover"))
        {
            foreach (Blueprint b in BlueprintManager.toDiscover)
            {
                Debug.Log(b.name);
            }
        }
        if (GUILayout.Button("Held"))
        {
            foreach(Blueprint b in BlueprintManager.held)
            {
                Debug.Log(b.name);
            }
        }
        if (GUILayout.Button("Stashed"))
        {
            foreach (Blueprint b in BlueprintManager.stashed)
            {
                Debug.Log(b.name);
            }
        }
        if (GUILayout.Button("Researched"))
        {
            foreach (Blueprint b in BlueprintManager.researched)
            {
                Debug.Log(b.name);
            }
        }
        if (GUILayout.Button("AddToStash"))
        {
            var b = BlueprintManager.toDiscover[Random.Range(0, BlueprintManager.toDiscover.Count)];
            if (b.classifier == Blueprint.Classifier.Bonus)
            {
                return;
            }
            BlueprintManager.toDiscover.Remove(b);
            BlueprintManager.stashed.Add(b);
            Debug.Log(b.name);
        }
        if (GUILayout.Button("AddToResearched"))
        {
            var b = BlueprintManager.stashed[Random.Range(0, BlueprintManager.stashed.Count)];
            BlueprintManager.stashed.Remove(b);
            BlueprintManager.researched.Add(b);
            Debug.Log(b.name);
        }
    }
}
