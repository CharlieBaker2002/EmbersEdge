using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;


[CanEditMultipleObjects]
[CustomEditor(typeof(ChargerTurret))]
public class MoreCustomEditors : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        if(GUILayout.Button("LevelUp")){
            ((ChargerTurret)target).LevelUp();
        }
    }
}

[CanEditMultipleObjects]
[CustomEditor(typeof(SpawnManager))]
public class SpawnManagerEdtir : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        if(GUILayout.Button("NextDay"))
        {
            SpawnManager.instance.NextDayFR();
        }
    }
}

[CanEditMultipleObjects]
[CustomEditor(typeof(SplinePlacer))]
public class SplinePlacerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        if (GUILayout.Button("Add New Splines"))
        {
            ((SplinePlacer)target).PlaceSpline();
        }
        if (GUILayout.Button("Remove Splines"))
        {
            ((SplinePlacer)target).DeleteSplineData();
        }
    }
}

[CanEditMultipleObjects]
[CustomEditor(typeof(MapManager))]
public class MapManagerEditor : Editor
{
    //float x;
    //float y = -10;
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        //x = EditorGUILayout.FloatField(x);
        //y = EditorGUILayout.FloatField(y);
        //if (GUILayout.Button("Follow Mouse"))
        //{
        //    ((MapManager)target).FollowMouse();
        //}
    }
}


[CanEditMultipleObjects]
[CustomEditor(typeof(UIManager))]
public class UIManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        if (GUILayout.Button("SetToBase"))
        {
            ((UIManager)target).SetTelePhone(UIManager.TeleMode.Base, 1f, () => Debug.Log("Base!") );
        }
    }
}

[CanEditMultipleObjects]
[CustomEditor(typeof(ResourceManager))]
public class ResourceManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        if (GUILayout.Button("Fill"))
        {
            ((ResourceManager)target).AddCores();
        }
        if (GUILayout.Button("Use"))
        {
            ((ResourceManager)target).UseCores(1,1);
        }
        if (GUILayout.Button("Increase"))
        {
            ((ResourceManager)target).IncreaseMaxCores();
        }
    }
}

[CustomEditor(typeof(Connector))]
public class ConnectorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        if (GUILayout.Button("Fill"))
        {
            ((Connector)target).Play();
        }
    }
}

[CustomEditor(typeof(DashPump))]
public class DashPumpEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        if (GUILayout.Button("ActivatePumps"))
        {
            DashPump.ActivatePumps();
        }
    }
}

[CustomEditor(typeof(Laser))]
public class LaserEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        if (GUILayout.Button("LevelUp"))
        {
            ((Spell)target).LevelUp();
        }
    }
}

[CustomEditor(typeof(EmitterTurret))]
public class EmitterTurretEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        if (GUILayout.Button("S1"))
        {
            ((EmitterTurret)target).SwarmUpgrade(1);
        }
        if (GUILayout.Button("S2"))
        {
            ((EmitterTurret)target).SwarmUpgrade(2);
        }
        if (GUILayout.Button("S3"))
        {
            ((EmitterTurret)target).SwarmUpgrade(3);
        }
        if (GUILayout.Button("Shrapnel"))
        {
            ((EmitterTurret)target).ShrapnelUpgrade();
        }
    }
}



[CanEditMultipleObjects]
[CustomEditor(typeof(RefreshManager))]
public class RefreshEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        if(GUILayout.Button("RESET VALUES TO PUBLISH")){
            ((RefreshManager)target).ResetValues(true);
        }
        if(GUILayout.Button("DEVELOPMENT MODE")){
            ((RefreshManager)target).ResetValues(false);
        }
    }
}

[CustomEditor(typeof(Vessel))]
public class VesselEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        if(GUILayout.Button("AddParts")){
            foreach (Vessel v in Vessel.vessels)
            {
                v.InvokeThisVessel();
            }
           
        }
    }
}


[CustomEditor(typeof(MechaSuit))]
public class MechaEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        if(GUILayout.Button("CheckPrevs")){
            MechaSuit.prevs.ForEach(x=>Debug.Log(x.Item1 + x.Item2));
        }
    }
}


[CustomEditor(typeof(Finder))]
public class FinderEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        if (GUILayout.Button("OnTurrets"))
        {
            Finder.TurnOnTurrets();
        }
    }
}


