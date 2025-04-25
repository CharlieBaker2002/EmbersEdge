using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VesselCol : MonoBehaviour
{
    [SerializeField] private Vessel v;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.name == "Character")
        {
           v.InvokeThisVessel();
        }
    }
}
