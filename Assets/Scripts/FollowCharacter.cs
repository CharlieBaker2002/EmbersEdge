using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FollowCharacter : MonoBehaviour
{
    Transform p;
    public Vector2 v = Vector2.zero;
    public bool matchRotation = false;
    public bool lookup = false;
    private void Start()
    {
        p = GS.CS();
    }

    void Update()
    {
        transform.position = p.position + (Vector3) v;
        if (lookup)
        {
            transform.up = Vector2.up;
        }
        if(matchRotation)
        {
            transform.rotation = p.rotation;
        }
    }

    private void LateUpdate()
    {
     
        Update();
        
    }
}
