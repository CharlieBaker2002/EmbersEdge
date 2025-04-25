using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ProgressEEEventButton : MonoBehaviour, IClickable
{
    public void OnClick()
    {
        for (int i = 0; i < 5; i++)
        {
            EmbersEdge.EEExplodeEvent.Invoke();
        }
        
    }
}
