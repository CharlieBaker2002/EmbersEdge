using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ProgressEEEventButton : MonoBehaviour, IClickable
{
    public void OnClick()
    {
        Debug.Log("yo");
        GS.CallSpawnOrbs(Vector2.zero, new float[] { 100, 30, 10, 3 }, null, false);
    }
}
