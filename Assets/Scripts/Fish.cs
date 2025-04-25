using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Fish : MonoBehaviour
{
    public static List<Fish> f;
    
    [SerializeField] private SpriteRenderer sr;
    [SerializeField] private Sprite[] sprs;
    private float t;
    private int ind;

    [SerializeField] private float animSpeed = 1f;

    private float thru;

    public static Vector2 Next (ref float t)
    {
        return Vector2.zero;
    }
    
    //Efficient Animate @ 12fps
    private void Update()
    {
        t += Time.deltaTime * animSpeed;
        if (!(t >= 0.08333333f)) return;
        t -= 0.08333333f;
        ind += 1;
        if (ind >= sprs.Length)
        {
            ind = 0;
        }
        sr.sprite = sprs[ind];
    }
}
