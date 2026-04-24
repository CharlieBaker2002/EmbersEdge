using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Battery : Building
{
    public float energy;
    public float maxEnergy;

    public Action<float> onUpdate  = f => { };
    public Action onUse = () => { };

    [SerializeField] bool visual = false;

    [SerializeField] private SpriteRenderer coil;
    [SerializeField] private Sprite[] coil0Sprs;
    [SerializeField] private Sprite[] coil1Sprs;
    [SerializeField] private Sprite[] coil2Sprs;
    
    [SerializeField] private Sprite[][] coilSprs;
    [SerializeField] private Sprite[] quickChargeSprs;
    [SerializeField] private Sprite[] energysprs;
    
    private float energyBuffer;
    private float buffer;
    private float t;

    [SerializeField] Material[] mats;

    private System.Action<float> onEraChange;
    

    private void Awake()
    {
        onUpdate = _ => { };
        onUse = () => { };
        coilSprs = new []{coil0Sprs,coil1Sprs,coil2Sprs};
    }

    /// <summary>
    /// COST IS +VE. Returns if has enough energy.
    /// </summary>
    public bool Use(float cost)
    {
        if (energy == 0f) return false;

        if (!(energy >= cost)) return false;
        energy -= cost;
        onUpdate.Invoke(energy);
        onUse.Invoke();
        buffer -= cost;
        return true;
    }


    public void Add(float amount)
    {
        if(energy == maxEnergy) return;
        
        float before = energy;
        energy += amount;
        if (energy > maxEnergy)
        {
            energy = maxEnergy;
        }

        buffer += energy - before;
        if(visual && energy - before >= 0.9f * maxEnergy)
        {
            StartCoroutine(QuickCharge());
        }
        
        onUpdate.Invoke(energy);
        
    }

    IEnumerator QuickCharge()
    {
        visual = false;
        sr.material = mats[GS.Era1()];
        yield return StartCoroutine(GS.Animate(sr, quickChargeSprs, 1f));
        // for(float z = 0f; z < 1f; z += Time.deltaTime)
        // {
        //     t += 3f * Time.deltaTime;
        //     if (t > 1f) t -= 1f;
        //     coil.sprite = GS.PercentParameter(coilSprs[GS.era], t);
        //     yield return null;
        // }
        visual = true;
    }

    private void Update()
    {
        if(!visual) return;

        energyBuffer = Mathf.Lerp(energyBuffer, energy, Time.deltaTime * 3f);
        
        buffer = Mathf.Lerp(buffer, 0f, Time.deltaTime);
        
        if (buffer > 0.1f)
        {
            sr.material = mats[0];
            sr.sprite = GS.PercentParameter(energysprs, energy / maxEnergy);
            t += buffer * Time.deltaTime;
            if (t > 1f) t -= 1f;
            coil.sprite = GS.PercentParameter(coilSprs[GS.era], t);
        }
        else if (buffer < -0.1f)
        {
            sr.material = mats[GS.Era1()];
            sr.sprite = GS.PercentParameter(energysprs, energyBuffer / maxEnergy);
            t += buffer * Time.deltaTime;
            if (t < 0f) t += 1f;
            coil.sprite = GS.PercentParameter(coilSprs[GS.era], t);
        }
        else
        {
            coil.sprite = null;
            sr.material = mats[0];
        }
    }

    public void Charge(float y, float speed)
    {
        StartCoroutine(ICharge(y));
        IEnumerator ICharge(float y)
        {
            while (Mathf.Abs(energy - y) > speed*Time.deltaTime*3f)
            {
                if(y > energy)
                {
                    Add(speed*Time.deltaTime);
                }
                else
                {
                    Use(speed*Time.deltaTime);
                }
                yield return null;
            }
        }
    }
}
