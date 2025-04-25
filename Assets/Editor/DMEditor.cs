using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(DM))]
public class DMEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        DM dm = (DM)target;
        if (GUILayout.Button("Era1"))
        {
            GS.era = 0;
            dm.MakeDungeon(0);
        }
        if (GUILayout.Button("Era2"))
        {
            GS.era = 1;
            dm.MakeDungeon(1);
        }
        if (GUILayout.Button("Era3"))
        {
            GS.era = 2;
            dm.MakeDungeon(2);
        }
    }
}
