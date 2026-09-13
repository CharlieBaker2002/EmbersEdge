using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ResourceManager : MonoBehaviour
{
    public static ResourceManager instance;
    public GameObject player;
    public float fuel = 0f;
    public float energy = 0f;
    public float maxFuel = 0f;
    public float maxEnergy = 0f;
    public float fuelToEnergy = 2f;
    public float fuelConsumption = 0.25f;
    public CoreSlider energySlider;
    public CoreSlider fuelSlider;
    public TextMeshProUGUI[] resourceUIs = new TextMeshProUGUI[] { };
    public LatentShield latentShield;
    [SerializeField] Animator[] coreAnims;

    public static float staticGeneration;
    public static float solarGeneration;
    public static float absorptionGeneration;
    public Sprite[] hpSprites;
    public Sprite[] shieldSprites;
    public Sprite[] shieldRegenSprites;
    private float shieldAnimTimer = 0f;
    private int shieldAnimInd = 0;

    private void Awake()
    {
        instance = this;
        staticGeneration = 0f;
        solarGeneration = 0f;
        absorptionGeneration = 0f;
    }

    public void Refresh(bool fillResources = true)
    {
        maxFuel = 0;
        maxEnergy = 0;
        if (fillResources)
        {
            foreach(EnergyPart p in EnergyPart.fuels)
            {
                p.energy = p.maxEnergy;
                maxFuel += p.maxEnergy;
            }
            foreach (EnergyPart p in EnergyPart.energies)
            {
                p.energy = p.maxEnergy;
                maxEnergy += p.maxEnergy;
            }
        }
        else
        {
            foreach(EnergyPart p in EnergyPart.fuels)
            {
                maxFuel += p.maxEnergy;
            }
            foreach (EnergyPart p in EnergyPart.energies)
            {
                maxEnergy += p.maxEnergy;
            }
        }
        fuelSlider.InitialiseSlider(maxFuel);
        energySlider.InitialiseSlider(maxEnergy);
    }

    private void Update()
    {
        float energySum = 0f;
        var energies = EnergyPart.energies;
        for (int i = 0; i < energies.Count; i++)
        {
            energySum += energies[i].energy;
        }
        energy = energySum;
        float fuelSum = 0f;
        var fuels = EnergyPart.fuels;
        for (int i = 0; i < fuels.Count; i++)
        {
            fuelSum += fuels[i].energy;
        }
        fuel = fuelSum;
        energySlider.UpdateSlider(energy);
        fuelSlider.UpdateSlider(fuel);
        latentShield.enabled = energy > 0f;

        GS.DistributeSprites(DefensePart.hps,hpSprites,1f - CharacterScript.CS.ls.hp/CharacterScript.CS.ls.maxHp);
        var csShields = CharacterScript.CS.ls.shields;
        int latentID = CharacterScript.CS.latentShield.ID;
        for (int i = 0; i < csShields.Count; i++)
        {
            if (csShields[i].ID == latentID)
            {
                GS.DistributeSprites(DefensePart.shields,shieldSprites,1f - csShields[i].strength / CharacterScript.CS.latentShield.max);
                break;
            }
        }
        if (DefensePart.shieldRegens.Count == 0) return;
        //Doing shield regen animation here for whatever reason...
        shieldAnimTimer -= Time.deltaTime;
        if (!(shieldAnimTimer <= 0f)) return;
        shieldAnimTimer += 0.25f;
        shieldAnimInd++;
        if(shieldAnimInd >= shieldRegenSprites.Length)
        {
            shieldAnimInd = 0;
        }
        foreach(SpriteRenderer sr in DefensePart.shieldRegens)
        {
            sr.sprite = shieldRegenSprites[shieldAnimInd];
        }
    }

    /// <summary>Building is free: always affordable, nothing is ever charged or refunded.
    /// Kept as a seam for the coming chips+ember delivery economy.</summary>
    public bool CanAfford(int[] cost, bool invert = false, bool useResources = true)
    {
        return true;
    }

    public Transform FindPlayer()
    {
        return player.transform;
    }

    /// <summary>Legacy settle hook (held orbs are gone). Kept as a no-op seam — callers all over
    /// the UI still settle before affordability checks.</summary>
    public void DropResources(int ind = -1, bool swapres = true)
    {
    }

    public bool ChangeFuels(float change)
    {
        if(change > 0) //adding fuels
        {
            if (!(fuel < maxFuel)) return true;
            float prev = fuel;
            EnergyPart.ChangeFuel(Mathf.Min(maxFuel - fuel,change));
            if(fuel - prev < change)
            {
                EnergyPart.ChangeEnergy(Mathf.Min(maxEnergy - energy, change - (fuel - prev)));
            }
            return true;
        }
        //else
        if (energy >= -change)
        {
            EnergyPart.ChangeEnergy(change);
            return true;
        }

        if (!(fuel + energy >= -change)) return false;
        EnergyPart.ChangeFuel(change + energy);
        EnergyPart.ChangeEnergy(-energy);
        return true;
    }

    // FOR AUTOMATIONS THAT DON'T WANT TO USE FUEL!
    public bool UseEnergy(float cost)
    {
        if (energy + cost > 0f)
        {
            EnergyPart.ChangeEnergy(cost);
            return true;
        }
        return false;
    }

    public void IncreaseMaxCores()
    {
        Animator anim;
        for(int i = coreAnims.Length - 1; i >= 0; i--)
        {
            anim = coreAnims[i];
            if (!anim.enabled)
            {
                anim.enabled = true;
                anim.GetComponent<Image>().color = Color.white;
                return;
            }
        }
    }

    /// <summary>
    /// Returns -1 if not enough cores for min param.
    /// </summary>
    public int UseCores(int min, int max, bool ignorethisbooleanplis = false) //recursive 1st sweep checks, 2nd sweep implements.
    {
        int used = 0;
        foreach(Animator anim in coreAnims)
        {
            if (!anim.enabled)
            {
                continue;
            }
            if (anim.GetBool("Full"))
            {
                used++;
                if (ignorethisbooleanplis)
                {
                    anim.SetBool("Full", false);
                }
                if(used == max)
                {
                    if (ignorethisbooleanplis)
                    {
                        return max;
                    }
                    else
                    {
                        UseCores(min, max, true);
                        return max;
                    }
                }
            }
        }
        if(used >= min && !ignorethisbooleanplis)
        {
            UseCores(min, max, true);
            return used;
        }
        return 0;
    }

    public void AddCores(int cores = 1)
    {
        Animator anim;
        for (int i = coreAnims.Length - 1; i >= 0; i--)
        {
            anim = coreAnims[i];
            if (!anim.enabled)
            {
                continue;
            }
            if (!anim.GetBool("Full"))
            {
                anim.SetBool("Full", true);
                cores--;
                if(cores <= 0)
                {
                    return;
                }
            }
        }
    }

    /// <summary>Building actions are free and instant now: every task fires immediately.</summary>
    public bool NewTask(GameObject g, int[] cost, Action act, bool onlyImmediate = true)
    {
        act?.Invoke();
        return true;
    }
}
