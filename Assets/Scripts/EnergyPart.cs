using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class EnergyPart : Part
{
    public bool isEnergy = true;
    public float maxEnergy;
    public float energy = 4f;

    public static List<EnergyPart> energies;
    public static List<EnergyPart> fuels;

    [SerializeField] Sprite[] sprs;
    [SerializeField] Light2D l;
    [SerializeField] float maxIntensity = 0.5f;
    float prev;
    [SerializeField] private bool updatesprite = true;


    public void UpdateSprite()
    {
        if(!updatesprite) return;
        sr.sprite = GS.PercentParameter(sprs, (maxEnergy - energy) / maxEnergy);
        if(l!=null) l.intensity = maxIntensity *  energy / maxEnergy;
    }

    private void Update()
    {
        if(!updatesprite) return;
        engagement -= Time.deltaTime;
        if (engagement <= 0f)
        {
            engagement = 0f;
        }
    }

    public override void StartPart(MechaSuit mecha)
    {
        if (isEnergy)
        {
            energies.Add(this);
        }
        else
        {
            fuels.Add(this);
        }
    }

    public override void StopPart(MechaSuit mecha)
    {
        if (isEnergy)
        {
            energies.Remove(this);
        }
        else
        {
            fuels.Remove(this);
        }
    }

    public static void ChangeEnergy(float change)
    {
        foreach (EnergyPart p in energies)
        {
            change = p.UpdateEnergy(change);
            if (change == 0)
            {
                return;
            }
        }
    
    }
    
    public static void ChangeFuel(float change)
    {
        foreach(EnergyPart p in fuels)
        {
            change = p.UpdateEnergy(change);
            if (change == 0)
            {
                return;
            }
        }
    }
    
    public float UpdateEnergy(float change)
    {
        prev = energy;
        energy += change;
        if (energy > maxEnergy)
        {
            energy = maxEnergy;
        }
        else if (energy < 0f)
        {
            energy = 0f;
        }
        float delta = energy - prev;
        if (delta != 0 && updatesprite)
        {
            engagement = 1f;
        }

        if (isEnergy)
        {
            ResourceManager.instance.energy += delta;
        }
        else
        {
            ResourceManager.instance.fuel += delta;
        }
        change -= delta;
        UpdateSprite();
        return change;
    }
}