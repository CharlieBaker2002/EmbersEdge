using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DaddyBuildingTile : MonoBehaviour, IClickable
{
    public GameObject[] buildings;
    public static DaddyBuildingTile current = null;
    public static bool open = false;

    public void OnClick()
    {
        if (current == this) return;
        current = this;
        BM.i.SetupDaddy(this);
        IM.i.pi.Player.Escape.performed -= UIManager.i.escapeDel;
        BM.i.AddDaddyDel();
    }

   
}
