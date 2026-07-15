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
    // Daily on-person quota: every held orb that leaves the player (banked at base, spent via a
    // drop) uses up carry capacity for the rest of the day — maxHeld is a per-day THROUGHPUT,
    // not just a pack size. The full quota comes back at the day tick.
    private int[] heldQuotaUsed = new int[] { 0, 0, 0, 0 };
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
    private float autoBankTimer = 0f;
    
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
        // starting resources ride in via the held pack — that must not eat day 1's carry quota
        ResetHeldQuota();
        SpawnManager.instance.OnNewDay += ResetHeldQuota;
        ResourceBarsUI.Attach(this);
    }

    private void Update()
    {
        // Auto-bank held orbs the moment the bank has room — no portal touch or fresh pickup
        // needed. Covers space that opens up while at base (spending on builds, a new pylon
        // raising capacity): held orbs on the player flow into the pylon on their own. Never while
        // diving, though — dungeon hauls ride on the player until the portal home.
        autoBankTimer -= Time.deltaTime;
        if (autoBankTimer <= 0f)
        {
            autoBankTimer = 0.25f;
            if (heldOrbs.Count > 0 && (PortalScript.i == null || !PortalScript.i.inDungeon))
            {
                for (int i = 0; i < 4; i++)
                {
                    if (held[i] > 0 && orbs[i] < orbCaps[i]) { DropResources(); break; }
                }
            }
        }

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

    /// <summary>
    /// Cost is positive, can be less than 4 long. Spends resources if possible.
    /// </summary>
    public bool CanAfford(int[] cost, bool invert = false, bool useResources = true)
    {
        // CHEATBUILD: everything is affordable and nothing is spent. Refund calls (invert) must
        // no-op too — under the cheat nothing was ever charged.
        if (RefreshManager.i != null && RefreshManager.i.CHEATBUILD) return true;
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


    /// <summary>What the player may still carry of an element TODAY — maxHeld minus the quota
    /// already spent banking/spending held orbs since the last day tick.</summary>
    public int MaxHeldToday(int index)
    {
        return Mathf.Max(0, maxHeld[index] - heldQuotaUsed[index]);
    }

    /// <summary>Fresh day (and game start): the full on-person quota is back, and attraction
    /// re-opens for anything that was quota-capped.</summary>
    private void ResetHeldQuota()
    {
        for (int i = 0; i < 4; i++)
        {
            heldQuotaUsed[i] = 0;
            OrbScript.canAttract[i] = held[i] < MaxHeldToday(i);
        }
        UpdateResourceUI();
    }

    public bool HasRoom(int index)
    {
        if (held[index] < MaxHeldToday(index))
        {
            held[index] += 1;
            if (held[index] >= MaxHeldToday(index))
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
            if (held[i] >= MaxHeldToday(i))
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
        // A refundable build allocation is outstanding (building picked from the menu, not yet
        // placed): freeze ALL held→pylon movement so cancelling restores the pool EXACTLY —
        // banking now would inflate the pylons past their caps on refund, and eat the day's
        // carry quota for a build that never happened. The auto-bank / pickup drops resume the
        // moment the building is placed or the pick is cancelled.
        if (BM.PlacementRefundPending) return;
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
                heldQuotaUsed[o.orbType] += 1;   // banked off the person — today's quota shrinks
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

    /// <summary>Bank ONE orb into the pylon network from an arbitrary world point (drone hauls).
    /// Same rules as the player's DropResources: gated on the pool cap, lands in a matching
    /// pylon (throne as fallback; pylons overflow into stores on their own). Returns false when
    /// that element's pool is full — the caller keeps its orb wild.</summary>
    public bool TryBankOrbFrom(int type, Vector3 from)
    {
        if (type < 0 || type > 3 || orbs[type] >= orbCaps[type]) return false;
        if (SpawnManager.instance == null) return false;
        var orb = SpawnManager.instance.orbPools[type].Get();
        var os = orb != null ? orb.GetComponent<OrbScript>() : null;
        if (os == null) return false;
        orb.transform.position = from;
        OneOrb(type);
        GetNextPylon(type).DepositOrb(os, from);
        UpdateResourceUI();
        return true;
    }

    /// <summary>Bank ONE orb into a SPECIFIC magnet — the pylon (or store) a drone flew its
    /// haul to. Same pool gate as <see cref="TryBankOrbFrom"/>; a destination that filled
    /// mid-flight falls back to the normal next-pylon routing so the orb is never lost.</summary>
    public bool TryBankOrbInto(OrbMagnet dest, int type, Vector3 from)
    {
        if (type < 0 || type > 3 || orbs[type] >= orbCaps[type]) return false;
        if (SpawnManager.instance == null) return false;
        var orb = SpawnManager.instance.orbPools[type].Get();
        var os = orb != null ? orb.GetComponent<OrbScript>() : null;
        if (os == null) return false;
        orb.transform.position = from;
        OneOrb(type);
        if (dest == null || dest.orbType != type || dest.n >= dest.capacity) dest = GetNextPylon(type);
        dest.DepositOrb(os, from);
        UpdateResourceUI();
        return true;
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
                    op.magnetsOfficial.Add(om);
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
                resourceUIs[i].text = "(" + held[i].ToString() + " / " + MaxHeldToday(i).ToString() + (orbs[i] >= 0 ? ") + " : ") - ") + Mathf.Abs(orbs[i]).ToString() + " / " + orbCaps[i].ToString();
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
        // CHEATBUILD: skip the orb task — the action fires now, for free. Null-action tasks
        // (crops absorbing orbs) keep the real flow: the magnet IS their mechanic.
        if (RefreshManager.i != null && RefreshManager.i.CHEATBUILD && act != null)
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
