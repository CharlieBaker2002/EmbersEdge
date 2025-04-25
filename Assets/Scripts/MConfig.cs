using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "MConfig", menuName = "ScriptableObjects/MConfig")]
public class MConfig : ScriptableObject
{
    public List<PConfig> partConfigs = new List<PConfig>();
}

[System.Serializable]
public struct PConfig
{
    public Blueprint bp;
    public Vector2 pos;
    public Quaternion rot;
    public Vector2 transPosition;
    public int lvl;
    
    public PConfig(Blueprint b, Vector2 tp, Vector2 p, Quaternion r, int levl)
    {
        bp = b;
        transPosition = tp;
        pos = p;
        rot = r;
        lvl = levl;
    }
}