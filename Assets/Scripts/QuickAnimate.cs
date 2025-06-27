using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class QuickAnimate : MonoBehaviour
{
    public Sprite[] sprs;
    private SpriteRenderer sr;
    public float speed = 1f;
    public bool loopBack = true;
    private float t;
    
    private void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
    }

    private void Update()
    {
        t += Time.deltaTime * speed;
        sr.sprite = GS.PercentParameter(sprs, t);
        if (t >= 1f)
        {
            if (loopBack)
            {
                t = 0f;
                Array.Reverse(sprs);
            }
            else
            {
                t -=1f;
            }
        }
    }
}