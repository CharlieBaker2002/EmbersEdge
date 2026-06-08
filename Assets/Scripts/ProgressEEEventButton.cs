using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ProgressEEEventButton : MonoBehaviour, IClickable
{
    public void OnClick()
    {
        this.QA(()=> MapManager.i.NewCoreDebug(),0.1f);
    }
}
