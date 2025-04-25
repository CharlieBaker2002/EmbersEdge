using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AntiRotate : MonoBehaviour
{
    public bool local = false;
    void Update()
    {
        if (local)
        {
            transform.localRotation = Quaternion.identity;
        }
        else
        {
            transform.rotation = Quaternion.identity;
        }

    }

    private void LateUpdate()
    {
        if (local)
        {
            transform.localRotation = Quaternion.identity;
        }
        else
        {
            transform.rotation = Quaternion.identity;
        }
    }
}
