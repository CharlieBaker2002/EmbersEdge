using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class PauseHints : MonoBehaviour
{
    [SerializeField] private Transform[] ts;
    [SerializeField] private string[] hints;
    [SerializeField] private string[] descrs;
    [SerializeField] private bool[] large;
    private Camera c;
    

    private void OnEnable()
    {
        c = CameraScript.i.cam;
        for (int i = 0; i < hints.Length; i++)
        {
            UIManager.MakeKey(hints[i], Pos(i), descrs[i], large[i], false);
        }
    }

    private void OnDisable()
    {
        foreach (var t in hints)
        {
            UIManager.DeleteKey(t);
        }
    }

    Vector2 Pos(int ind)
    {
        return c.ScreenToWorldPoint(ts[ind].position);
    }
}
