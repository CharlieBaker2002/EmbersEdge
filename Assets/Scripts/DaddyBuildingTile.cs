using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DaddyBuildingTile : MonoBehaviour, IClickable
{
    [Tooltip("Building prefabs shown when this category opens. A null (None) entry starts a new row in the palette grid.")]
    public GameObject[] buildings;
    public static DaddyBuildingTile current = null;
    public static bool open = false;

    public void OnClick()
    {
        if (current == this) return;
        current = this;
        BM.i.SetupDaddy(this);
        BM.i.AddDaddyDel();
    }

   
}
