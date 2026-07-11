using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>Pathing body classes — the radius behind <see cref="Unit.size"/>: Small 0.4u,
/// Medium 0.8u, Large 1.6u.</summary>
public enum BodySize { Small = 0, Medium = 1, Large = 2 }

public class Unit : MonoBehaviour, IClickable
{
    //THIS SCRIPT IS A BASE CLASS FOR ALL UNITS. FIRSTLY IT CONTAINS A DESCRIPTION FIELD AND AN ICON FOR WHEN PRESSED, WHICH ALSO DETAILS THE HEALTH. 
    //SECONDARILY, IT HOLDS STATUS INFORMATION FOR VARIOUS EFFECTS AND CCS AND BUFFS. WHEN CC'D, THE ACTIONSCRIPT WILL COMMUNICATE WITH THE UNIT CLASS TO APPLY THE EFFECTS VISUALLY.
    //THIS SCRIPT CARRIES OUT ASSOCIATED STATUS BASED EFFECTS, THE STATUS CLASS IS FOR USED FOR DATA INIT PURPOSES AND VISUALISATION
    //FINALLY, IT CONTAINS A REFERENCE TO AN ANIMATOR AND AN FLOAT ACT RATE, THIS FACILITATES STUNNING / DISARMIMING / SLOWING / STIMMING EFFECTS. AN IENUMERATOR HELPS THIS EFFECT.
    public int enemyID = -1;
    public LifeScript ls;
    public ActionScript AS;
    public Animator anim;
    public SpriteRenderer sr;
    [SerializeField] protected Vector3 iconPlacer;
    
    public List<Status> stati = new List<Status>();
    
    [Tooltip("Status vulnerability effects duration stun, root, slow. It is a coefficient")]
    [SerializeField] public float statVulnerability = 1f;

    [Tooltip("Body size class for pathing: Small = 0.4u radius, Medium = 0.8u, Large = 1.6u. Two effects: (1) straight-line shortcuts are only taken when a corridor this wide is clear, so units stop grinding into cracks their bodies can't fit through; (2) it drives the wall-aversion reach of the shared padding rings — while any unit of a tier is actively pathing, routes price cells within that tier's radius of walls steeply. Fields are shared, so the largest tier CURRENTLY pathing sets the reach; it relaxes automatically when the big bodies die.")]
    public BodySize bodySize = BodySize.Small;
    /// <summary>Pathing body radius in world units, from <see cref="bodySize"/> (Small 0.4,
    /// Medium 0.8, Large 1.6).</summary>
    public float size => bodySize == BodySize.Large ? 1.6f : bodySize == BodySize.Medium ? 0.8f : 0.4f;
    [Tooltip("Wall-detour willingness at the base: detour when detourDist <= wander × throughDist. 1 = go around whenever any route exists, higher = detour further before resorting to chewing. Below 1 the chew route itself also straightens CONTINUOUSLY: chew time is discounted by this factor when routing, so ever-lower values ignore soft-wall shortcuts more, until 0 walks a dead-straight line and attacks the first wall geometrically in the way. Jittered per-instance into wanderEff.")]
    public float wander = 1.5f;
    [HideInInspector] public float wanderEff = 1.5f;
    [Tooltip("How much damaged walls attract this unit when walls are its goal (preferWalls): 0 = nearest wall regardless of damage, 1 = weigh remaining HP exactly like the chew router does, higher = detour further to hit nearly-dead walls.")]
    public float wallExploitIQ = 1f;
    [Tooltip("Anti-grouping influence: how strongly the crowding census repels this unit from targets already under attack. 0 = happily piles on regardless, 1 = standard, 10 = HATES attacking anything already swarmed.")]
    public float crowdAversion = 1f;
    [Tooltip("Target-class priority at the base (absolute): when set, this unit hunts the nearest member of its preferred class(es) and ignores closer non-preferred targets, falling back to nearest-anything only when nothing preferred is reachable. Multiple flags = nearest among those classes. Wander still applies: obstructed with a low wander → the wall on the route becomes the target regardless of preference.")]
    public bool preferCharacter = false;
    public bool preferBuildings = false;
    public bool preferWalls = false;
    // Set by the unit's own AI while it is committed to turning immaterial for its next move (the
    // windup before the status actually lands) — live immaterial counts automatically via
    // PhasesWalls. Scripts own this flag: set it when the phase window is decided, clear it when
    // the window ends.
    [HideInInspector] public bool phasing = false;
    /// <summary>Walk-through-walls pathing applies: immaterial right now, or declared about to be.
    /// Base pathfinding then steers the straight line through material walls and never hands back
    /// a wall as a chew target (preferWalls still hunts them); the Force Field's wall is itself
    /// immaterial, so it still blocks — sealed in by one, the unit routes like matter again.</summary>
    public bool PhasesWalls => phasing || (AS != null && AS.immaterial);
    [Tooltip("Current objective (runtime): written by MinePathManager.Decide every think tick. May be a wall's LifeScript transform when the decision is to chew. Null only when nothing is left to fight.")]
    public Transform target;
    //Lifescript max health sets the cap for dematerialise, leech, convert effects
    
    protected float defaultAnimSpeed = 1f;
    [HideInInspector] public float defaultActRate = 1f;
    [SerializeField] protected float actRate = 1f;

    public bool juggernaut = false;
    
    public bool staticEffectActivated = false;
    public bool converted = false; //use this for instantiation of projectiles and units to an appropritate tag.
    public float leechTime = -1f;
    
    [Space(20)]
    public string description;
 
    private static int[] bigBetter = { 2, 5, 8, 17, 18 }; //Decides if bigger values are better for status effects
    private bool updateShields;
    protected Status[] shieldStati = new Status[2]; //strong, weak
    protected bool inDungeon;

    [SerializeField] private bool isCharacter = false;
    
    /// <summary>
    ///  BASE IS USED TO SET UP ACT RATE BASED ON DIFFICULTY AND PREFAB SETTINGS
    /// </summary>
    protected virtual void Start()
    {
        inDungeon = transform.InDungeon();
        // per-instance jitter: units straddling the chew-vs-detour boundary split naturally,
        // which also diversifies wall breach points (dog-pile avoidance, layer 1)
        wanderEff = wander * UnityEngine.Random.Range(0.75f, 1.35f);
        if (anim != null)
        {
            defaultAnimSpeed = anim.speed;
        }
        actRate = 1f - 0.1f * (3f - SetM.difficulty); //1f,0.9f,0.8f
        defaultActRate = actRate;
        if (defaultActRate < 1f)
        {
            UpdateActRate();
        }

        if (enemyID != -1)
        {
            // Ensure the sub‑list exists
            while (enemyID >= EnemyTracker.enemies.Count)
            {
                EnemyTracker.enemies.Add(new List<Transform>());
            }
            // Add only once
            var list = EnemyTracker.enemies[enemyID];
            if (!list.Contains(transform))
            {
                list.Add(transform);
            }
        }
    }

    // Persistent behaviour loops (the enemy's "brain"). SetActive(false) — how the dungeon freeze
    // sleeps enemies while you're away — kills every running coroutine, and the object re-enables
    // WITHOUT re-running Start(). Any brain loop registered here is relaunched by OnThaw so frozen
    // patrollers resume instead of standing still. Use RunPersistent instead of StartCoroutine for
    // a loop that must survive a freeze; transient/animation-event coroutines keep using StartCoroutine.
    readonly List<Func<IEnumerator>> persistentRoutines = new List<Func<IEnumerator>>();

    protected Coroutine RunPersistent(Func<IEnumerator> routine)
    {
        persistentRoutines.Add(routine);
        return StartCoroutine(routine());
    }

    /// <summary>Relaunch every persistent brain loop. Called by the dungeon freeze system after it
    /// re-enables a previously-frozen enemy (whose coroutines were killed by SetActive(false)).</summary>
    public void OnThaw()
    {
        foreach (var r in persistentRoutines) StartCoroutine(r());
    }
    
   
    
    
    public virtual void UpdateActRate() //Called when a stim or a slow is applied. Allows simunltaneous stimming and slowing (take the average). Automatically applies to animator if exists.
    {
        if (staticEffectActivated)
        {
            actRate = 0f;
        }
        else
        {
            float val = 0f;
            int n = 0;
            foreach (Status s in stati)
            {
                if(s == null) continue;
                if (s.enabled == false)
                {
                    continue;
                }
                if (s.ind == 0)
                {
                    val = 0;
                    n = 1; //stunned, so act rate of 0
                    break;
                }

                if (s.ind is not (3 or 5)) continue;
                val += s.value2;
                n++;
            }
            val = n > 0 ? val / n : 1; //Usually have a cheeky divide by 1 here but gets job done
            actRate = val;
        }

        if (leechTime != -1f && actRate != 0f) //NOT COMPLETED YET
        {
            float prev = actRate;
            float t = 1f - 0.334f * (Time.time - leechTime);
            actRate = Mathf.Min(prev, actRate);
        }

        actRate *= defaultActRate;
        
        if (anim == null) return;
        anim.speed = defaultAnimSpeed * actRate;
    }
    
    protected IEnumerator WaitForActSeconds(float t)
    {
        while (t > 0f)
        {
            t -= Time.deltaTime * actRate;
            yield return null;
        }
    }

    protected Coroutine WFAS(float t) //Faster To Type
    {
        return StartCoroutine(WaitForActSeconds(t));
    }
    
    public void ApplyStatus(Status s) //GS called -> Status Initialised OR AddToExistingStatus -> Apply Status Here
    {
        if (s.cancelWithDamage)
        {
            ls.onDamageDelegate += s.onDamageDelete;
        }
        switch (s.ind)
        {
            case 0: //stun
                for (int i = 0; i < stati.Count; i++) //Remove all stun-to-cancel status effects
                {
                    if (!stati[i].cancelWithStun) continue;
                    if(stati[i].dissapearing) continue;
                    stati[i].Dissapear();
                    StatusComplete(stati[i].ind);
                    i--;
                }
                AS.rb.linearVelocity *= 0.4f;
                AS.canAct = false;
                UpdateActRate();
                break;
            case 1: //root
                AS.Stop();
                AS.rooted = true;
                break;
            case 2: //push
                throw new NotImplementedException();
            case 3: //slow
                AS.rb.linearVelocity *= s.value2;
                UpdateActRate();
                break;
            case 4: //impenetrable
                throw new NotImplementedException();
            case 5: //stim
                UpdateActRate();
                break;
            case 6: //juggernaut
                juggernaut = true;
                AS.pushable = false;
                break;
            case 7: //shield
                UpdateLineColour(false);
                break;
            case 8: //heal
                // Because 'typ.decrease' means Status.value1 is stored as "1 / total_duration"
                // so the actual time is (1f / s.value1).
                float totalTime  = 1f / s.value1;
                float totalHeal  = s.value2;

                // We'll pick a "damage type" for healing. Let's say 4 or 5,
                // or you could just use 0 if you do not mind the color in UI.  
                // E.g. let's use 4 so that UI can show green "healing" text.
                ls.ChangeOverTime(totalHeal, totalTime, -1, false, s);

                // Mark outline color (if you want an immediate green outline). 
                // The rest can be done via UpdateLineColour if you added logic for case 8.
                UpdateLineColour(false);

                break;
            case 9: //invulnerable
                ls.invulnerable = true;
                break;
            case 10: //immaterial
                AS.immaterial = true;
                break;
            case 11: //reflect
                AS.convertProjectiles = true;
                break;
            case 12: //dodging
                AS.interactive = false;
                break;
            case <= 16:
                if (s.ind == 15)
                {
                    StatusManager.AddStaticUnit(this);
                    if (s.value2 == 1f) //IF SHOCKING, BUT THE LIGHTNING EFFECT HAPPENS TO HAVE TIMED OUT, SHOCK ANYWAY
                    {
                        staticEffectActivated = true;
                        s.value1 = 11;
                        s.sliderValue = 1f;
                        AS.prepared = false;
                        Instantiate(Resources.Load<GameObject>("StaticFX"), transform.position,Quaternion.Euler(90f,0f,0f), transform).GetComponent<Follower>().t = s.transform;
                        ls.ChangeOverTime(-0.4f * ls.maxHp,5f,2);
                        UpdateActRate();
                    }
                    s.value2 = 10f; //LIGHTNING HAS 10 AS THE CAP.
                }
                else
                {
                    s.value2 = ls.maxHp; //DEMATERIALISE, LEECH, CONVERT HAVE MAX HEALTH AS VALUE 2 CAP
                }
               
                if (s.value1 < s.value2) break;
                if (s.ind == 13)
                {
                    PlaceStats();
                    s.SetUpDamageDelegate(); //Apply On damage delegate only if suddenly activated
                    ls.onDamageDelegate += s.onDamageDelete;
                }
                CallSpecialStatusEffect(s.ind);
                break;
            case 17: //weak heal
                 // Because 'typ.decrease' means Status.value1 is stored as "1 / total_duration"
                // so the actual time is (1f / s.value1).
                float mtotalTime  = 1f / s.value1;
                float mtotalHeal  = s.value2;

                // We'll pick a "damage type" for healing. Let's say 4 or 5,
                // or you could just use 0 if you do not mind the color in UI.  
                // E.g. let's use 4 so that UI can show green "healing" text.
                ls.ChangeOverTime(mtotalHeal, mtotalTime, -1, false, s);

                // Mark outline color (if you want an immediate green outline). 
                // The rest can be done via UpdateLineColour if you added logic for case 8.
                UpdateLineColour(false);
                break;
            case 18: //weak shield
                UpdateLineColour(false);
               break;
        }
        
        UpdateLineColour(false);
    }

    public void UpdateLineColour(bool updateIfRemoving)
    {
        if (!AS.prepared)
        {
            ls.overrideCol = Color.clear;
        }
        else if (stati.Any(x => x.dissapearing == false && x.ind == 9)) //inv
        {
            ls.overrideCol = Color.yellow;
        }
        else if (stati.Any(x => x.dissapearing == false && x.ind == 12)) //dodging
        {
            ls.overrideCol = GS.ColFromEra();
            ls.overrideCol = new Color(ls.overrideCol.r, ls.overrideCol.g, ls.overrideCol.b, 0.1f);
        }
        else if (stati.Any(x => x.dissapearing == false && x.ind == 10)) //immaterial
        {
            ls.overrideCol = Color.cyan;
        }
        else if (!isCharacter && stati.Any(x =>x.dissapearing == false && x.ind is 7 or 18)) //shield
        {
            ls.overrideCol = Color.grey;
           
        }
        else if (stati.Any(x => x.dissapearing == false && x.ind is 17 or 8)) //healing
        {
            ls.overrideCol = Color.green;
        }
        else
        {
            ls.overrideCol = Color.clear;
        }

        if (ls.overrideCol != Color.clear)
        {
           for(int i = 0; i < ls.dmgsrs.Count; i++)
           {
               ls.dmgsrs[i].material.SetFloat(LifeScript.Thickness, ls.thicknesses[i]);
               ls.dmgsrs[i].material.SetColor(LifeScript.Outline,4f * ls.overrideCol);
           }
        }
        else if (updateIfRemoving)
        {
            ls.UpdateLineColour();
        }
    }
    

    public Status AddToExistingStatus(int ind, float value1, float value2)
    {
        Status s = stati.First(x => x.ind == ind);
        switch (s.taip)
        {
            case Status.typ.flat: //SHIELD CARRIED OUT IN UPDATESHIELDVALUES, NOT ADDTOEXISTINGSTATUS.
                //IMPENETRABLE AND REFLECTIVE (the other flat values) DON'T ADD, SO IGNORE.
                break;
            
            case Status.typ.decrease:
                if (s.ind is not (8 or 17))
                {
                    if (s.dissapearing || s.sliderValue < 0.1f)
                    {
                        stati.Remove(s);
                        s.gameObject.SetActive(false);
                        s.Dissapear();
                        return GS.Stat(this, Status.typs[ind], value1, value2);
                    }
                    value1 *= statVulnerability;
                    s.value1 = (1f / s.value1) - s.timer; //how long left
                    s.value1 = 1f/(s.value1 + value1); //add and convert back to rate
                    s.timer = 0f;
                    s.sliderValue = 1f;

                    if(bigBetter.Contains(ind))
                    {
                        if (value2 > s.value2)
                        {
                            s.value2 = value2; //Bigger values are better
                        }
                   
                    }
                    else if (ind == 3) //Smaller value better for slow
                    {
                        if (value2 < s.value2)
                        {
                            s.value2 = value2;
                        }
                    }
                    //OTHERWISE VALUE 2 NOT RELEVANT

                    if (ind is 0) //if stunned
                    {
                        AS.rb.linearVelocity *= 0.4f;
                    }
                    else if (ind is 3 or 5) //if slowed or stimmed, update act rate
                    {
                        UpdateActRate();
                        AS.rb.linearVelocity *= s.value2;
                    }
                }
                else //HEALS
                {
                    s.value1 = (1f / s.value1) - s.timer; //how long left
                    s.value1 = Mathf.Max(s.value1,value1); //add and convert back to rate
                    s.value1 = 1f / s.value1;
                    s.timer = 0f;
                    s.sliderValue = 1f;
                    ls.ChangeOverTime(value2, value1, -1, false, s);
                }
               
                break;
            case Status.typ.increaseThenDecrease:
                if (s.value1 >= s.value2) return s; //If already activated... laowwww it
                if (value2 == 1 && s.ind == 15) //IF LIGHTNING EFFECT
                {
                    staticEffectActivated = true;
                    s.value1 = 11; //Such that it activated in the status & visually shown as so.
                    s.sliderValue = 1f;
                    AS.prepared = false;
                    Instantiate(Resources.Load<GameObject>("StaticFX"), transform.position, Quaternion.Euler(90f,0f,0f), transform).GetComponent<Follower>().t = s.transform;
                    ls.ChangeOverTime(-0.4f * ls.maxHp,5f,2,false);
                    UpdateActRate();
                    return s;
                }

                if (ind == 15 && StatusManager.lightning) //jic many get lightninged at once over the verge.
                {
                    return s;
                }
                s.value1 += value1;
                s.sliderValue = Mathf.Clamp01(s.value1/s.value2);
                s.UpdateSlider();
                if (s.value1 >= s.value2)
                {
                    if (s.ind == 13)
                    {
                        s.SetUpDamageDelegate(); //Apply On damage delegate only if suddenly activated dematerialise
                        ls.onDamageDelegate += s.onDamageDelete;
                    }
                    CallSpecialStatusEffect(ind);
                }
                else
                {
                    s.timer = 0f; //reset timer to 0
                }
                break;
            
           
                
        }
        return s;
    }

    /// <summary>
    /// use me for Time.Time timers
    /// </summary>
    protected float InvActRate()
    {
        if (actRate == 0f)
        {
            return 999f;
        }

        return 1f / actRate;
    }
    
    public void StatusComplete(int ind) //destruction and removal of status taken care of in the status class
    {
        switch (ind)
        {
            case 0: //stun
                UpdateActRate();
                AS.canAct = true;
                break;
            case 1: //root
                AS.rooted = false;
                break;
            case 2: //push
                break;
            case 3: //slow
                UpdateActRate();
                break;
            case 4: //impenetrable
                throw new NotImplementedException();
            case 5: //stim
                UpdateActRate();
                break;
            case 6: //juggernaut
                juggernaut = false;
                AS.pushable = true;
                break;
            case 7: //shield
                break;
            case 8: //heal
                break;
            case 9: //invulnerable
                ls.invulnerable = false;
                break;
            case 10: //immaterialdd
                AS.immaterial = false;
                break;
            case 11: //reflect
                AS.convertProjectiles = false;
                break;
            case 12: //dodging
                AS.interactive = true;
                break;
            case 13: //Dematerialise
                StatusManager.StopDematerialise(this);
                break;
            case 14: //Leech
                StatusManager.RemoveLeech(this);
                leechTime = -1f;
                UpdateActRate();
                break;
            case 15: //Static
                StatusManager.RemoveStaticUnit(this);
                staticEffectActivated = false;
                AS.prepared = true;
                if (!ls.hasDied)
                {
                    ls.Change(-2f * GS.Era1(),2);
                    Instantiate(Resources.Load<GameObject>("StaticFXBurst"), transform.position, Quaternion.Euler(90f,45f,0f), GS.FindParent(GS.Parent.misc));
                    UpdateActRate();
                }
                break;
            case 16: //Convert
                Convert(false); //just call it again
                break;
            case 17: //weak heal
                break;
            case 18: //weak shield
                break;
        }
        UpdateLineColour(true);
    }
        
    /// <summary>
    /// Base used for positioning status effects
    /// </summary>
    protected virtual void Update()
    {
        if (leechTime != -1f)
        {
            UpdateActRate();
        }
        CarryOutShieldDisplay();
        PlaceStats();
    }
  
     protected void CarryOutShieldDisplay()   // protected: Drone.Update bypasses AllyAI.Update but still wants shield display
     {
         // Let the life script clamp each shield‐type to maxHp:
         if (updateShields)
         {
             ls.EnforceShieldCaps();  // <-- NEW
         
             float strongSum = 0f;
             float weakSum   = 0f;
             foreach (var shield in lsShields())
             {
                 if (shield.isWeak) weakSum += shield.strength;
                 else strongSum += shield.strength;
             }
             
             UpdateShieldDisplay(0,strongSum);
             UpdateShieldDisplay(1, weakSum);

             updateShields = false;
         }
     }

     private void UpdateShieldDisplay(int index, float amount)
     {
         if (amount <= 0f)
         {
             if(shieldStati[index] == null) return;
             if(shieldStati[index].dissapearing) return;
             // bail only when the slot holds something that ISN'T a shield status —
             // the old `!= 7 || != 18` was always true, so the icon never dissapeared
             if (shieldStati[index].ind != 7 && shieldStati[index].ind != 18) return;
             shieldStati[index].value1 = 0f;
             shieldStati[index].UpdateSlider();
             shieldStati[index]?.Dissapear();
             shieldStati[index] = null;
             return;
         }
         Status s = shieldStati[index];
         if (s == null)
         {
             shieldStati[index] = GS.Stat(this, index == 0 ? "shield" : "weak shield", amount);
         }
         else if(s.ind == 7 || s.ind == 18)
         {
             s.value1 = amount;                          // set the new total
             s.sliderValue = amount / ls.maxHp;          // fraction of max HP
             s.UpdateSlider();
             if (amount <= 0f)
             {
                 s.Dissapear();
             }
         }
     }

     private IEnumerable<LifeScript.Shield> lsShields()
     {
         return ls != null ? ls.shields : Enumerable.Empty<LifeScript.Shield>();
     }

    protected virtual void PlaceStats()
    {
        Vector3 b = transform.position + iconPlacer;
        for (int i = 0; i < stati.Count; i++)
        {
            stati[i].transform.position = b + Vector3.right * (i * 0.4f - (stati.Count - 1f) * 0.2f);
        }
    }

    public virtual void OnDestroy()
    {
        foreach (Status s in stati)
        {
            s.Dissapear();
            StatusComplete(s.ind);
        }
        if (enemyID != -1 && enemyID < EnemyTracker.enemies.Count)
        {
            EnemyTracker.enemies[enemyID].Remove(transform);
        }
    }

    void CallSpecialStatusEffect(int ind)
    {
        if (ls.hasDied) return;
        switch (ind)
        {
            case 13:
                StatusManager.StartDematerialise(this);
                break;
            case 14:
                StatusManager.i.StartLeech(this);
                break;
            case 15:
                StatusManager.StartLightning(this);
                UpdateActRate();
                break;
            case 16:
                Convert();
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    void Convert(bool on = true)
    {
        if (on)
        {
            name += "NoHeal";
            StatusManager.i.ConvertFX(this);
        }
        else
        {
            name = name.Replace("NoHeal", "");
            StatusManager.i.DeConvertFX(this);
        }
        converted = !converted;
        string s = transform.CompareTag("Allies") ? "Enemies" : "Allies"; //Switch tag
        bool allies = s == "Allies";
        foreach (Collider2D col in transform.GetComponentsInChildren<Collider2D>())
        {
            col.transform.tag = s;
            col.gameObject.layer = allies ? LayerMask.NameToLayer("Ally Units") : LayerMask.NameToLayer("Enemy Units");
        }
        transform.gameObject.layer = allies ? LayerMask.NameToLayer("Ally Units") : LayerMask.NameToLayer("Enemy Units");
        tag = s;
    }
    
    public virtual void OnClick()
    {
        //Debug.Log(description);
    }

    // Editor debugging: select an enemy in play mode (Scene-view Gizmos on) to see the route its
    // fields will walk it along and the aim point of its current objective.
    void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || !CompareTag("Enemies")) return;
        MinePathManager.DrawRouteGizmo(this);
    }

    protected float ActRateProjectileStrength()
    {
        return 10f * (actRate - 1f);
    }
    
    public int CreateShield(float maxStrength, bool startFull = true, bool isWeak = false)
    {
        int id = ls.CreateShield(maxStrength, startFull, isWeak);
        updateShields = true;
        return id;
    }
    
    public void RemoveShield(int shieldID)
    {
        ls.RemoveShield(shieldID);
        updateShields = true;
    }
    
    public float ModifyShieldStrength(int shieldID, float amount)
    {
        float newStrength = ls.ModifyShieldStrength(shieldID, amount);
        updateShields = true;
        return newStrength;
    }
}