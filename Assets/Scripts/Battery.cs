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

    private bool init = false;
    private bool added = false;

    private void Awake()
    {
        act = _ => { };
    }

    void Start()
    {
        init = true;
        OnEnable();
    }

    private void OnEnable()
    {
        if(!init) return;
        if(added) return;
        energy = 0;
        if (!EnergyManager.i.NewBattery(this))
        {
            //icon.SetActive(true);
        }
        added = true;
    }

    private void OnDisable()
    {
        if (!added) return;
        added = false;
        EnergyManager.i.RemoveBattery(this);
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

    /// <summary>
    /// HELPER FOR ENERGYMANAGERONLY!
    /// </summary>
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

    public void Add(float amount)
    {
        energy += amount;
        EnergyManager.i.UpdateGrid(gridID);
    }
}
