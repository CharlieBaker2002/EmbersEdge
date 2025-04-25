using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ScriptableObjects.Blueprints.BPScripts;
using UnityEngine;
using UnityEngine.InputSystem;
using Random = UnityEngine.Random;

public class BlueprintManager : MonoBehaviour
{
    public static List<Blueprint> toDiscover = new(); //near all of them..
    public static List<Blueprint> held = new(); //can be lost
    public static List<Blueprint> stashed = new(); //safe, research me
    public static List<Blueprint> researched = new(); //researched, for future use
    public Baron.B baronSelection;

    [Header("List of all non part blueprints (incl bonuses)")]
    public List<Blueprint> allbps; //list of all bps incl loot

    [Header("List of all blueprints acquired prior to scene start")]
    public List<Blueprint> prepared; 

    [Header("Default Buildings For The User")]
    public List<GameObject> defaultBuildings;
    
    [Header("Base Building Sciences")]
    public List<Blueprint> baseBuildingSciences;

    [Header("AllBarons")] public Baron[] barons;
 
    
    private static NewLoot[] lscriptcurrent;

    private static float maxRarity = 0;
    public static int lootChoices = 0;

    static int timeID;

    public static bool bonus = false;

    public static List<string> sciences;
    public static BlueprintManager i;

    public NewLoot loot;
    
    
    /// <summary>
    /// MAKE A LIST OF MECHANISMS TO BE ADDED EVERY TIME YOU SPAWN.
    /// MAKE THERE JUST ONE MECHA-SUIT
    /// REMOVE JUST THE NON-CONTINUAL MECHANISMS WHEN YOU DIE FIRST TIME (AS DETERMINED BY BARON).
    /// DUNGEON-BASE MECHA-CONTINUITY?
    /// </summary>
    
    private void Awake()
    {
        i = this;
        lootChoices = 0;
        held = new List<Blueprint>();
        stashed = new List<Blueprint>();
        researched = new List<Blueprint>();
        toDiscover = new List<Blueprint>();
        List<Blueprint> buf = new List<Blueprint>();

        Baron.choice = baronSelection;
        for(int x = 0; x < barons.Length; x++)
        {
            Baron b = barons[x];
            if (b.bar == baronSelection)
            {
                Baron.current = b;
                b.gameObject.SetActive(true);
            }
            else
            {
                b.Cleanup();
                x--;
            }
        }

        sciences = new List<string>();
        for (int x = 0; x < prepared.Count; x++)
        {
            if (prepared[x].classifier == Blueprint.Classifier.Science)
            {
                sciences.Add(prepared[x].name.ToLower());
                prepared.RemoveAt(x);
                x--;
            }
        }
        
        // for(int i = 0; i < allbps.Count; i++)
        // {
        //     allbps[i] = Instantiate(allbps[i]);
        // }
        // for(int i = 0; i < prepared.Count; i++)
        // {
        //     string nam = prepared[i].name;
        //     prepared[i] = Instantiate(prepared[i]);
        //     prepared[i].name = nam;
        // }
        foreach(Blueprint b in prepared)
        {
            foreach(Blueprint x in b.relevents)
            {
                AddDiscoverNoCopy(x);
            }
            if(MechanismSO.ns.ContainsKey(b.name))
            {
                MechanismSO.ns[b.name]++;
            }
            else
            {
                MechanismSO.ns.Add(b.name, 1);
                MechanismSO.ns[b.name] = 1;
                researched.Add(b);
            }
        }

        foreach (Blueprint bp in baseBuildingSciences) //set up researched and toDiscover and maxRarity
        {
            if (sciences.Contains(bp.name.ToLower()))
            {
                continue;
            }
            sciences.Add(bp.name.ToLower());
        }

        foreach (Blueprint bp in allbps) //set up researched and toDiscover and maxRarity
        {
            if (!buf.Contains(bp))
            {
                toDiscover.Add(bp);
                if (bp.shopCost > maxRarity)
                {
                    maxRarity = bp.shopCost;
                }
            }
        }
        //Debug.Log("max rarity: " + maxRarity.ToString());
    }

    public static bool Contains(Blueprint b, List<Blueprint> check)
    {
        foreach(Blueprint x in check)
        {
            if(x.name == b.name)
            {
                return true;
            }
        }
        return false;
    }

    public static void AddDiscoverNoCopy(Blueprint b)
    {
        if (researched.Contains(b)) return;
        toDiscover.Add(b);
    }
    //
    //
    // public static WeaponBP[] GetWeapons(List<Blueprint> check)
    // {
    //     List<WeaponBP> bps = new List<WeaponBP>();
    //     foreach(Blueprint b in check)
    //     {
    //         if (b.g.GetComponent<WeaponScript>() != null)
    //         {
    //             bps.Add((WeaponBP)b);
    //         }
    //     }
    //     return bps.ToArray();
    // }
    //
    // public static AutomationBP[] GetAutomations(List<Blueprint> check)
    // {
    //     List<AutomationBP> bps = new List<AutomationBP>();
    //     foreach (Blueprint b in check)
    //     {
    //         if (b.g.GetComponentInChildren<IRelic>() != null)
    //         {
    //             bps.Add((AutomationBP)b);
    //         }
    //     }
    //     return bps.ToArray();
    // }
    //
    // public static Blueprint[] GetBoosts(List<Blueprint> check)
    // {
    //     List<Blueprint> bps = new List<Blueprint>();
    //     foreach (Blueprint b in check)
    //     {
    //         if (b.g.GetComponent<IPotion>() != null)
    //         {
    //             bps.Add(b);
    //         }
    //     }
    //     return bps.ToArray();
    // }
    //
    // public static AbilityBP[] GetAbilities(List<Blueprint> check)
    // {
    //     List<AbilityBP> bps = new List<AbilityBP>();
    //     foreach (Blueprint b in check)
    //     {
    //         if (b.g.GetComponentInChildren<ISpell>() != null)
    //         {
    //             bps.Add((AbilityBP)b);
    //         }
    //     }
    //     return bps.ToArray();
    // }
    //
    public static Blueprint[] GetBuildings(List<Blueprint> check)
    {
        List<Blueprint> bps = new List<Blueprint>();
        foreach (Blueprint b in check)
        {
            if (b.classifier == Blueprint.Classifier.Building)
            {
                bps.Add(b);
            }
        }
        return bps.ToArray();
    }

    public static void PositionLoots(NewLoot[] loots)
    {
        if (loots == null) return;
        int n = loots.Length;
        int[] xs = new int[n];
        switch (n)
        {
            case 2:
                xs = new int[] { -500, 500 };
                break;
            case 3:
                xs = new int[] { -750, 0, 750 };
                break;
            case 4:
                xs = new int[] { -975, -325, 325, 975 };
                break;
            case 5:
                xs = new int[] { -1200, -600, 0, 600, 1200 };
                break;
        }
        for (int i = 0; i < xs.Length; i++)
        {
            loots[i].transform.localPosition += new Vector3(xs[i], 0, 0);
        }
    }

   /// <summary>
    /// luck 0 - 100. 75 Luck will allow possibility to unlock most rare item
    /// </summary>
    public static NewLoot[] GetLoot(float[] luck, int choices, bool lootbox = false,  Part.RingClassifier typ = Part.RingClassifier.Powered, bool bonu = false) {
        if (!lootbox)
        {
            PortalScript.i.NoPortal();
            PortalScript.i.Cancel();
        }
        if (lootChoices > 0)
        {
            return null;
        }
        if (IM.controller)
        {
            IM.i.CloseCursor();
        }

        GS.Stat(CharacterScript.CS, "Invulnerable", 2.5f);

        lootChoices = choices;
        timeID = SpawnManager.instance.NewTS(0.4f, 5f);

        toDiscover.Shuffle();
        
        var validBPs = new List<Blueprint>();
        foreach (var bp in toDiscover)
        {
            if (bp is MechanismSO z)
            {
                if (bonu) continue;
                if (Part.Ring(z.p.taip) == typ)
                {
                    validBPs.Add(bp);
                }
            }
            else if (bonu)
            {
                validBPs.Add(bp);
            }
        }
        if (validBPs.Count < choices)
        {
            foreach (var bp in toDiscover)
            {
                if (bp is not MechanismSO) validBPs.Add(bp); //fill with bonuses to fill the gap
                if(validBPs.Count >= choices) break;
            }
        }
        NewLoot[] loots = new NewLoot[luck.Length];
        int protect = 0;

        for (int i = 0; i < luck.Length; i++)
        {
            luck[i] = Mathf.Min(1, luck[i]);

            float rand = RandomManager.Rand(
                3, 
                new Vector2(Mathf.Max(0, -0.1f + luck[i]), Mathf.Min(1, 0.5f + 0.67f * luck[i]))
            );
            float result = maxRarity * rand;

            Blueprint current = validBPs[Random.Range(0, validBPs.Count)];

            if (protect < 20)
            {
                foreach (Blueprint bp in validBPs)
                {
                    if (Mathf.Abs(current.shopCost - result) <= 2f)
                    {
                        break;
                    }
                    if (Mathf.Abs(current.shopCost - result) > Mathf.Abs(bp.shopCost - result))
                    {
                        current = bp;
                    }
                }
            }

            bool cont = false;
            foreach (NewLoot x in loots)
            {
                if (x == null) 
                    break;
                if (current == x.bp)
                {
                    cont = true;
                    break;
                }
            }

            if (cont)
            {
                protect++;
                i--;
                continue;
            }

            var l = Instantiate(BlueprintManager.i.loot, UIManager.i.lootUI);
            
            l.bp = current;

            // if (l.bp.classifier == Blueprint.Classifier.Inner_Manifestor && Random.Range(0, 3) == 0)
            // {
            //     l.bp.classifier = Blueprint.Classifier.Core_Mechanism;
            // }
            // else if (l.bp.classifier == Blueprint.Classifier.Outer_Manifestor && Random.Range(0, 2) == 0)
            // {
            //     l.bp.classifier = Blueprint.Classifier.Outer_Mechanism;
            // }

            loots[i] = l;
        }

        lscriptcurrent = loots;
        PositionLoots(loots);
        
        IM.i.OpenCursor();
        
        return loots;
    }
    public static void LootSafe(bool swapRes = true, bool onlyBPs = false)
    {
        if (BM.i.redBuilding != null && !onlyBPs)
        {
            return;
        }
      
        if (held.Count > 0)
        {
            foreach (Blueprint l in held)
            {
                foreach (Blueprint rel in l.relevents)
                {
                    if (rel.classifier == Blueprint.Classifier.Science) // no need for discovery of sciences that are already researched
                    {
                        if (sciences.Contains(rel.name.ToLower()))
                        {
                            continue;
                        }
                    }
                    AddDiscoverNoCopy(rel);
                }
                if(MechanismSO.ns.ContainsKey(l.name))
                {
                    MechanismSO.ns[l.name]++;
                }
                else
                {
                    if (l.classifier == Blueprint.Classifier.Science)
                    {
                        sciences.Add(l.name.ToLower());
                        continue;
                    }
                    MechanismSO.ns.Add(l.name, 1);
                    MechanismSO.ns[l.name] = 1;
                    researched.Add(l);
                }
            }
            held.Clear();
            UIManager.i.SaveBPImgs();
        }

        if (onlyBPs)
        {
            return;
        }

        ResourceManager.instance.DropResources(-1, swapRes);

        List<OrbScript> delOrbs = new List<OrbScript>();
        foreach (Transform t in SpawnManager.instance.orbParent)
        {
            if (t.transform.position.sqrMagnitude > 80000)
            {
                delOrbs.Add(t.GetComponent<OrbScript>());
            }
        }
        foreach (OrbScript o in delOrbs)
        {
            o.ReturnToPool();
        }
    }
    
    public IEnumerator DestroyLoot() //teleport if on death.
    {
        UIManager.i.DeleteBPImgs();
        foreach (var bp in held.Where(bp => bp.unique))
        {
            toDiscover.Add(bp);
        }
        held.Clear();
        ResourceManager.instance.DestroyResources();
        yield return null;
      
        CharacterScript.CS.ls.StopAllCoroutines();
        yield return new WaitForSeconds(1f);
        CharacterScript.CS.ls.hasDied = false;
    }
    

    public static void Chosen()
    {
        lootChoices--;
        if(lootChoices == 0)
        {
            foreach(NewLoot l in lscriptcurrent)
            {
                if (!l.chosen)
                {
                    l.StopAllCoroutines();
                    l.End();
                }
            }
            FXWormhole.i?.Complete();
            SpawnManager.instance.CancelTS(timeID);
            CharacterScript.CS.AS.canAct = true;
            if (bonus)
            {
                bonus = false;
                LootBox.i.QA(() => { LootSafe(false, true); Destroy(LootBox.i.gameObject,0.1f); },1.5f);
                return;
            }

            if (!RefreshManager.i.ARENAMODE)
            {
                DM.i.activeRoom.AfterLootSelect();
            }

            if (IM.controller)
            {
                IM.i.CloseCursor();
            }
        }
    }
}

// NO NEED TO INSTANTIATE COPIES OF SCHEMATICS AND SCIENCE BLUEPRINTS