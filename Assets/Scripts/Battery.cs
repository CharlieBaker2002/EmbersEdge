using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Battery : MonoBehaviour
{
    public float energy;
    public float maxEnergy;
    [HideInInspector]
    public int gridID;

    public Action<float> act  = f => { };

    private void Awake()
    {
        act = _ => { };
    }


    /// <summary>
    /// COST IS +VE. Returns if has enough energy.
    /// </summary>
    
    public bool Use(float cost)
    {
        if (energy >= cost)
        {
            energy -= cost;
            EnergyManager.i.UpdateGrid(gridID);
            act.Invoke(energy);
            return true;
        }
        return false;
    }

    public float Set(float newVal)
    {
        if(newVal > maxEnergy)
        {
            energy = maxEnergy;
            act.Invoke(energy);
            return newVal - maxEnergy;
        }
        energy = newVal;
        act.Invoke(energy);
        return 0f;
    }
}
