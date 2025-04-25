using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Follower : MonoBehaviour
{
    public Transform t;
    private Quaternion rot;
    private void Awake()
    {
        rot = transform.rotation;
    }

    void LateUpdate()
    {
        if(t == null)return;
        transform.position = t.position;
        transform.rotation = rot;
    }
}
