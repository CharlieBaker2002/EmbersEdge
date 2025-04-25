using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Linq;

public class ResourceManager : MonoBehaviour
{
    public static ResourceManager instance;
    public int[] orbs; //white, green, grey, red
    public int[] orbCaps = new int[] { 0, 0, 0, 0 };
    public int[] initResources;
    public GameObject player;
    public OrbMagnet[] throneMags;
    public List<OrbMagnet> magnets = new List<OrbMagnet>();
    public List<OrbPylon> pylons = new List<OrbPylon>();
    public float fuel = 0f;
    public float energy = 0f;
    public float maxFuel = 0f;
    public float maxEnergy = 0f;
    public float fuelToEnergy = 2f;
    public float fuelConsumption = 0.25f;
    public List<OrbScript> heldOrbs = new List<OrbScript>();
    public CoreSlider energySlider;
    public CoreSlider fuelSlider;
    public TextMeshProUGUI[] resourceUIs = new TextMeshProUGUI[] { };
    
    public int[] held = new int[] { 0, 0, 0, 0 };
    public int[] maxHeld = new int[] { 50, 25, 10, 4 };
    public LatentShield latentShield;
    public static float[] debt;
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
        debt = new float[4] { 0, 0, 0, 0 };
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

        // fuelConsumption = 0f;
        // fuelToEnergy = 2f;

        // float effiencyN = StatModifierPart.all.Count(x => x.modifier == StatModifierPart.modifierType.efficiencyThrottle && x.power > 0);
        // if (effiencyN > 0)
        // {
        //     float efficiencyToConverterRatio = effiencyN / (effiencyN + StatModifierPart.all.Count(x => x.modifier == StatModifierPart.modifierType.converter && x.power > 0));
        //     fuelToEnergy = Mathf.Lerp(2f, 5f,  efficiencyToConverterRatio);
        // }
        // else
        // {
        //     fuelToEnergy = 2f;
        // }
        //
        // foreach (StatModifierPart p in StatModifierPart.all)
        // {
        //     switch (p.modifier)
        //     {
        //         case StatModifierPart.modifierType.converter:
        //             fuelConsumption += 0.1f * p.power;
        //             break;
        //         case StatModifierPart.modifierType.staticGenerator:
        //             staticGeneration += p.power;
        //             break;
        //         case StatModifierPart.modifierType.solarPannel:
        //             solarGeneration += p.power;
        //             break;
        //         case StatModifierPart.modifierType.emberCondenser:
        //             absorptionGeneration += p.power;
        //             break;
        //         case StatModifierPart.modifierType.munitions:
        //             return;
        //     }
        // }
    }
    private IEnumerator Start()
    {
        yield return null;
        for (int i = 0; i < 4; i++)
        {
            for (int j = 0; j < initResources[i]; j++)
            {
                var orb = SpawnManager.instance.orbPools[i].Get();
                heldOrbs.Add(orb.GetComponent<OrbScript>());
                orb.SetActive(false);
                //orb.GetComponent<BoxCollider2D>().enabled = false;
            }
        }
        yield return null;
        DropResources();
    }

    private void Update()
    {
        energy = EnergyPart.energies.Sum(x => x.energy);
        fuel = EnergyPart.fuels.Sum(x => x.energy);
        energySlider.UpdateSlider(energy);
        fuelSlider.UpdateSlider(fuel);
        latentShield.enabled = energy > 0f;
        
        GS.DistributeSprites(DefensePart.hps,hpSprites,1f - CharacterScript.CS.ls.hp/CharacterScript.CS.ls.maxHp);
        GS.DistributeSprites(DefensePart.shields,shieldSprites,1f - CharacterScript.CS.ls.shields.First(x => x.ID == CharacterScript.CS.latentShield.ID).strength / CharacterScript.CS.latentShield.max);
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

    /// <summary>
    /// Cost is positive, can be less than 4 long. Spends resources if possible.
    /// </summary>
    public bool CanAfford(int[] cost, bool invert = false, bool useResources = true)
    {
        if (invert)
        {
            for(int i = 0; i < cost.Length; i++)
            {
                cost[i] *= -1;
            }
        }
        int[] orbBuffer = new int[] { 0, 0, 0, 0 };
        for (int i = 0; i < 4; i++)
        {
            if (i < cost.Length)
            {
                orbBuffer[i] =  orbs[i] - cost[i];
            }
        }
        if (Mathf.Min(orbBuffer) >= 0)
        {
            if (useResources)
            {
                orbs = orbBuffer;
                UpdateResourceUI();
            }
            return true;
        }
        else
        {
            return false;
        }
    }

    private void SubtractOrbs(int[] cost)
    {
        for(int i = 0; i < 4; i++)
        {
            orbs[i] -= cost[i];
        }
        UpdateResourceUI();
    }

    public void OneOrb(int index)
    {
        if(index >= 0)
        {
            orbs[index] += 1;
        }
        else
        {
            orbs[-index] -= 1;
        }
    }
   
    public Transform FindPlayer()
    {
        return player.transform;
    }


    public bool HasRoom(int index)
    {
        if (held[index] < maxHeld[index])
        {
            held[index] += 1;
            if (held[index] == maxHeld[index])
            {
                OrbScript.canAttract[index] = false;
            }
            UpdateResourceUI();
            return true;
        }
        else
        {
            OrbScript.canAttract[index] = false;
        }
        return false;
    }

    private void ResetHeld(int[] info)
    {
        held = info;
        for(int i = 0; i < 4; i++)
        {
            if (held[i] >= maxHeld[i])
            {
                OrbScript.canAttract[i] = false;
            }
            else
            {
                OrbScript.canAttract[i] = true;
            }
        }
    }

    public void DropResources(int ind = -1, bool swapres = true) //childs orbs to throne unless beyond max resources
    {
        int[] info = new int[] {0,0,0,0};
        List<OrbScript> obuffer = new List<OrbScript>();
        foreach (OrbScript o in heldOrbs)
        {
            if(o.orbType != ind && ind != -1)
            {
                info[o.orbType]++;
                obuffer.Add(o);
                continue;
            }
            if(orbs[o.orbType] < orbCaps[o.orbType])
            {
                OneOrb(o.orbType);
                OrbMagnet om = GetNextPylon(o.orbType);
                om.DepositOrb(o);
            }
            else
            {
                info[o.orbType]++;
                obuffer.Add(o);
            }
        }
        heldOrbs.Clear();
        foreach(OrbScript o in obuffer)
        {
            heldOrbs.Add(o);
        }
        ResetHeld(info);
        UpdateResourceUI();
    }

    private OrbMagnet GetNextPylon(int typ)
    {
        foreach(OrbPylon o in pylons)
        {
            if(o.orbType != typ || o.mag.n == o.mag.capacity)
            {
                continue;
            }
            return o.mag;
        }
        return throneMags[typ];
    }

    public void DestroyResources()
    {
        foreach(OrbScript o in heldOrbs)
        {
            o.ReturnToPool();
        }
        heldOrbs.Clear();
        ResetHeld(new int[] {0,0,0,0});
        UpdateResourceUI();
    }


    public void ChangeMagnets(OrbMagnet omP, bool add)
    {
        if (add)
        {
            magnets.Add(omP);
        }
        else
        {
            magnets.Remove(omP);
        }
        IterateMagnets();
    }

    public void ChangePylons(OrbPylon opP, bool add)
    {
        if (add)
        {
            pylons.Add(opP);
        }
        else
        {
            pylons.Remove(opP);
        }
        IterateMagnets();
    }

    public void IterateMagnets()
    {
        foreach (OrbPylon op in pylons)
        {
            op.magnetsOfficial.Clear();
            op.cloneNecessary = true;
            foreach (OrbMagnet om in magnets)
            {
                if (om == op.mag)
                {
                    continue;
                }
                if (om.orbType == op.mag.orbType)
                {
                    if (!op.mag.initialMag)
                    {
                        if (Vector2.Distance(om.transform.position,op.transform.position) < op.radius)
                        {
                            op.magnetsOfficial.Add(om);
                        }
                    }
                    else if (Vector2.Distance(Vector3.zero, om.transform.position) < op.radius) //check in range of magnets
                    {
                        op.magnetsOfficial.Add(om);
                    }
                }
            }
        }

        foreach (OrbPylon op in pylons) //add magnets that are far from the initial one but still linked...
        {
            if (op.mag.initialMag)
            {
                foreach (OrbPylon ox in pylons)
                {
                    if (op.magnetsOfficial.Contains(ox.mag))
                    {
                        if (!ox.magnetsOfficial.Contains(op.mag))
                        {
                            ox.magnetsOfficial.Add(op.mag);
                        }
                    }
                }
            }
        }
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

    public void UpdateResourceUI()
    {
        for (int i = 0; i < 4; i++)
        {
            if (held[i] != 0)
            {
                resourceUIs[i].text = "(" + held[i].ToString() + " / " + maxHeld[i].ToString() + (orbs[i] >= 0 ? ") + " : ") - ") + Mathf.Abs(orbs[i]).ToString() + " / " + orbCaps[i].ToString();
            }
            else
            {
                resourceUIs[i].text = orbs[i].ToString() + " / " + orbCaps[i].ToString();
            }
        }
    }

    /// <summary>
    /// if cost is all zeros, instantly does the action. Non-only immediate always returns true
    /// </summary>
    public bool NewTask(GameObject g, int[] cost, Action act, bool onlyImmediate = true)
    {
        if (Mathf.Max(cost) == 0)
        {
            act.Invoke();
            return true;
        }
        if (CanAfford(cost,false,false) || !onlyImmediate)
        {
            SubtractOrbs(cost);
            List<OrbMagnet> oms = new List<OrbMagnet>();
            for (int i = 0; i < 4; i++)
            {
                if (cost[i] != 0)
                {
                    var om = g.AddComponent<OrbMagnet>();
                    om.orbType = i;
                    om.typ = OrbMagnet.OrbType.Task;
                    om.capacity = cost[i];
                    om.action = act;
                    oms.Add(om);
                }
            }
            foreach (OrbMagnet o1 in oms)
            {
                foreach (OrbMagnet o2 in oms)
                {
                    if (o1 != o2)
                    {
                        o1.siblingTs.Add(o2);
                    }
                }
            }
            return true;
        }
        else
        {
            return false;
        }
    }

    public OrbPylon FNP(Vector2 pos, int typ) //find nearest pylon
    {
        OrbPylon pylon = null;
        float dist = 10000f;
        foreach(OrbPylon p in pylons)
        {
            if(p.orbType == typ && Vector2.Distance(pos,p.transform.position) < dist)
            {
                pylon = p;
            }
        }
        return pylon;
    }

}
