using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Battery : MonoBehaviour
{
    public float energy;
    public float maxEnergy;
    [HideInInspector]
    public int gridID;

    /// <summary>
    /// COST IS +VE. Returns if has enough energy.
    /// </summary>
    public bool Use(float cost)
    {
        if (energy >= cost)
        {
            energy -= cost;
            EnergyManager.i.UpdateGrid(gridID);
            return true;
        }
        return false;
    }

    public float Set(float newVal)
    {
        if(newVal > maxEnergy)
        {
            energy = maxEnergy;
            return newVal - maxEnergy;
        }
        energy = newVal;
        return 0f;
    }
}
