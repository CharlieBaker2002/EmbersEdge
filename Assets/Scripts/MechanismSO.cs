using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "Blueprint", menuName = "ScriptableObjects/MechanismSO")]
public class MechanismSO : Blueprint
{
    [Header("MechanismSO")]
    public Part p;
    public static Dictionary<string, int> ns;
    public int powerRequired = 1;
    public bool continual = false;
    public int level = 0;
}
