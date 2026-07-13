using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

public class SoulHarvester : Building, IOnDeath
{
    public int race;
    [SerializeField] SpriteRenderer wall;
    [SerializeField] Sprite[] sprs;
    [SerializeField] Transform[] ts;
    [SerializeField] bool[] full;
    public static List<SoulHarvester>[] shs = new List<SoulHarvester>[4] {new List<SoulHarvester>(), new List<SoulHarvester>(), new List<SoulHarvester>(), new List<SoulHarvester>()};
    public List<OrbScript> os = new List<OrbScript>();
    private Action act;
    [HideInInspector]
    public bool upgraded = false;
    [SerializeField] Sprite upgradeSprite;
    bool canGiveResources = false;
    /// <summary>Bag-drone claim so two drones never fly for the same harvester's takings
    /// (mirrors OreChip.claimedBy). The drone-side scan treats a claimant that is no longer
    /// on the job as stale, so a yanked drone can't blacklist a harvester.</summary>
    [HideInInspector] public Drone claimedBy;
    /// <summary>Fleet-wide cool-off after a drone failed to reach this harvester (mirrors
    /// OreChip.unreachableUntil) — stops a claim/release thrash loop against blocked ground.</summary>
    [HideInInspector] public float droneRetryAt;
 
    public override void Start()
    {
        base.Start();
        AddUpgradeSlot(GS.CostArrayScaled(race, 45), "Reinforced Walls", upgradeSprite, true, UpgradeWall, 3);
        if (!upgraded)
        {
            wall.sprite = null;
        }
        else
        {
            wall.sprite = sprs[^1];
        }
        full = new bool[ts.Length];
        shs[race].Add(this);
        physic.onDamageDelegate += ctx =>
        {
            if (!upgraded) { return; }
            if (physic.hp <= 0) { wall.sprite = sprs[0]; return; }
            wall.sprite = sprs[Mathf.FloorToInt((physic.hp / physic.maxHp) * (sprs.Length - 1))];
        };
        foreach(OrbScript o in os)
        {
            if (o.state != OrbScript.OrbState.wild || !o.gameObject.activeInHierarchy)
            {
                continue;
            }
            o.transform.parent = GetSpace();
            if(o.transform.parent == null)
            {
                o.transform.parent = SpawnManager.instance.orbParent;
                break;
            }
            o.Harvest();
        }
        os = new List<OrbScript>();
        act = () => canGiveResources = true;
        SpawnManager.instance.OnNewDay += act;
    }

    private void UpgradeWall()
    {
        wall.sprite = sprs[^1];
        physic.maxHp *= 10;
        physic.Change(physic.maxHp,1);
        upgraded = true;
    }

    public Transform GetSpace()
    {
        for(int i = 0; i < full.Length; i++)
        {
            if (full[i] == false)
            {
                full[i] = true;
                return ts[i];
            }
        }
        return null;
    }

    public static Transform GetSpaceAll(int typ)
    {
        Transform t;
        foreach (SoulHarvester sh in shs[typ])
        {
            t = sh.GetSpace();
            if (t != null)
            {
                return t;
            }
        }
        return null;
    }

    /// <summary>Anything for a bag drone to lift? Same once-a-day gate as the walk-up sweep.</summary>
    public bool HasDroneCollectable
    {
        get
        {
            if (!canGiveResources || full == null) return false;
            for (int i = 0; i < full.Length; i++) if (full[i]) return true;
            return false;
        }
    }

    /// <summary>Hand ONE parked orb to a drone; its slot frees up for the generators. Taking
    /// the last full slot consumes the day's collection, exactly like the player's walk-up
    /// sweep — anything a drone leaves behind (bag full, pylons/stores full) stays claimable.</summary>
    public OrbScript TakeOrbForDrone()
    {
        for (int i = 0; i < full.Length; i++)
        {
            if (!full[i]) continue;
            full[i] = false;
            OrbScript o = ts[i].GetComponentInChildren<OrbScript>();
            if (o == null) continue;   // slot flag with no orb — keep scanning
            if (Array.IndexOf(full, true) == -1) canGiveResources = false;
            return o;
        }
        return null;
    }

    private void CollectOrbs()
    {
        OrbScript o;
        for (int i = 0; i < full.Length; i++)
        {
            if (full[i])
            {
                full[i] = false;
                o = ts[i].GetComponentInChildren<OrbScript>();
                o.state = OrbScript.OrbState.collect;
                o.transform.parent = SpawnManager.instance.orbParent;
            }
        }
    }

    private void OnTriggerStay2D(Collider2D collision)
    {
        if(collision.name == "Character" && canGiveResources)
        {
            if (Array.IndexOf(full, true) != -1)
            {
                canGiveResources = false;
                CollectOrbs();
            }
        }
    }

    public override void OnDeath()
    {
        foreach(OrbScript o in GetComponentsInChildren<OrbScript>())
        {
            if (upgraded)
            {
                o.timeLeft = 75f;
                o.state = OrbScript.OrbState.wild;
                o.wildKind = -1;   // fresh wild life — re-stamp its cohort on the next tick
                os.Add(o);
            }
            else
            {
                o.ReturnToPool();
            }
        }
        for(int i = 0; i < full.Length; i++)
        {
            full[i] = false;
        }
        SpawnManager.instance.OnNewDay -= act;
        base.OnDeath();
    }
}
