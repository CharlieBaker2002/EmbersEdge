using System.Collections.Generic;
using UnityEngine;
using System;
using System.Collections;
using TMPro;
using UnityEngine.Events;
using Random = UnityEngine.Random;

public class Building : MonoBehaviour, IOnDeath, IClickable //functionality for rebuilding, and clicking.
{
    public List<Behaviour> buildingBehaviours = new List<Behaviour>();
    public SpriteRenderer sr;
    public GameObject groundEdit;
    public List<BaseTile> tiles = new List<BaseTile>();
    public static List<Building> buildings = new List<Building>();
    [HideInInspector]
    public GameObject UIParent;
    public Action OnClose;
    public Action OnOpen;
    public bool canOpen = true;
    public bool hasExtraParent = false;
    public Sprite icon;
    [Header("Size.X for circle physic DIAMETER")]
    public Vector2 size = Vector2.one;
    [Tooltip("Pathfinding: a chewable WALL (HP-priced obstacle, never a target) instead of a normal building (impassable AND a first-class target).")]
    public bool isWall = false;
    [Tooltip("Placement: hold the place button and SWEEP to stamp many copies (walls). Each stamp charges the tile's orb cost. Pair with builtBlasts = 0 for orb-only construction (no ember needed).")]
    public bool multiDrag = false;
    [Tooltip("Offered by the build menu while in the DUNGEON (placed on excavated mine cells). Requires builtBlasts = 0 — dungeon builds complete on purchase, no ember/pylons exist there.")]
    public bool dungeonBuildable = false;
    public LifeScript physic;
    // pathfinding footprint bookkeeping — what we registered, so unregistration is exact
    private bool footprintRegistered;
    private Vector2Int regAnchor, regSize;
    // authored (prefab-nested) physic collider dims, reapplied to runtime-instantiated physics
    private Vector2 authoredBoxSize, authoredBoxOffset;
    private float authoredCircleRadius;
    private bool hasAuthoredCollider;
    private bool regAsWall;
    private LifeScript regWallLs;
    private bool box = true;
    [Header("Times & Costs")]
    public int builtBlasts = 2;
    [SerializeField] public List<EEIcon> icons = new();
    private bool subscribed = false;
    [HideInInspector]
    public int prevN;
    public int numIconsTrue = 0;
    private TextMeshPro numText;
    public float maxHealth = 10f;
    
    [Header("For init buildings set true")]
    public bool builtYet = false;

    // Start() populates spriterenderers/UIParent/etc. A freshly-instantiated building's orb magnet
    // can complete synchronously (ReceiveOrb finishes with zero yields) within the SAME call stack
    // as Instantiate/Commit, before Unity has invoked Start() on it — CompleteViaOrbs must not
    // touch Start-initialized state until that's happened.
    private bool startCalled;

    Action upgradeAction;
    
    [HideInInspector]public Vector2Int anchorCell;
    [HideInInspector] public Vector2Int gridSize = Vector2Int.one;

    private Action<int> numTextAction;

    private SpriteRenderer[] spriterenderers;

    private BuildingPower _power;
    /// <summary>Aggregate view onto adjacent EnergyPads. Use Power.Use/Add/Energy from consumer scripts.</summary>
    public BuildingPower Power => _power ??= new BuildingPower(this);

    /// <summary>True while the grid this building draws from still has any energy. Aiming towers gate
    /// their tracking rotation on this so a fully-drained tower goes dormant (stops moving/looking).</summary>
    public bool HasEnergy => Power.Energy > 1e-3f;

    public enum EnergyStatus { Powered, Throttled, Unpowered }
    [Header("Energy status overlay (power-consuming buildings)")]
    [Tooltip("World-space placement of the energy-status icon above this building.")]
    [SerializeField] protected Vector3 energyIconOffset = new Vector3(0f, 0f, 0f);
    [SerializeField] protected float energyIconScale = 1f;
    // Insufficient-icon timing (private, not inspector-tuned): min on-screen time, flash-out duration, blink period.
    private float energyMinShowTime = 5f;
    private float throttleLingerTime = 2f;
    private float energyFlashPeriod = 1.5f;
    private SpriteRenderer energyStatusSR;
    private EnergyStatus energyStatus = EnergyStatus.Powered;
    private Coroutine energyLingerCo;
    private Coroutine energyWatchCo;
    private float throttleShownAt;

    private Action closeUIViaEscape;


    public virtual void Start()
    {
        UIParent = Instantiate(UIManager.i.empty, transform.position, Quaternion.identity, UIManager.i.buildingsUI);
        numText = Instantiate(UIManager.i.numText, transform.position, Quaternion.identity, transform);
        numTextAction = _ => numText.color = GS.ColFromEra() * 1.25f;
        GS.OnNewEra += numTextAction;
        numTextAction.Invoke(0);
        numText.gameObject.SetActive(false);
        buildings.Add(this);
        UIParent.SetActive(false);
        if (!buildingBehaviours.Contains(this)) buildingBehaviours.Add(this);
        closeUIViaEscape = () => OnClose?.Invoke();
        OnOpen += delegate
        {
            UIManager.CloseAllUIs();
            UpdateUI();
            UIParent.SetActive(true);
            EscapeRouter.i?.Push(closeUIViaEscape);
        };
        OnClose += delegate
        {
            UIParent.SetActive(false);
            EscapeRouter.i?.Remove(closeUIViaEscape);
        };

        spriterenderers = hasExtraParent ? transform.parent.GetComponentsInChildren<SpriteRenderer>(true) : GetComponentsInChildren<SpriteRenderer>(true);

        if (physic != null)
        {
            // Remember the AUTHORED collider dims: runtime builds replace this nested physic with
            // the generic Resources Physic (a standard 0.95×0.95 one-cell body), which would
            // silently override a prefab-tuned collider (e.g. the 0.5×0.5 Wall).
            var abc = physic.GetComponent<BoxCollider2D>();
            box = abc != null;
            if (box)
            {
                authoredBoxSize = abc.size;
                authoredBoxOffset = abc.offset;
                hasAuthoredCollider = true;
            }
            else
            {
                var acc = physic.GetComponent<CircleCollider2D>();
                if (acc != null)
                {
                    authoredCircleRadius = acc.radius;
                    authoredBoxOffset = acc.offset;
                    hasAuthoredCollider = true;
                }
            }
        }

        if (!builtYet)
        {
            if (physic != null)
            {
                Destroy(physic.gameObject);
            }

            BuildFirst();
        }
        else
        {
            if (physic != null)
            {
                physic.onDeaths.Add(this);
                physic.GetComponent<IClickableCarrier>().clickable = this;
            }
        }

        // Mark this building’s footprint on the grid if it already exists at game start.
        if (builtYet)
        {
            RegisterGridOccupancy();
            RegisterPathFootprint();   // pre-placed buildings block/chew from the start
            BEnable();
        }

        startCalled = true;
    }

    /// <summary>
    /// Tell the base pathfinding what this building blocks: a chewable wall footprint (keyed to the
    /// live physic's HP) or a solid impassable one. Called whenever the building becomes physically
    /// present (Start for pre-placed, SwitchMonos(true) after build/repair); idempotent.
    /// </summary>
    // The footprint's bottom-left cell, world-quantized STRAIGHT from the transform — deliberately
    // not via GridManager.GridToWorld(anchorCell): anchorCell is a grid-frame coordinate that goes
    // stale when the grid re-anchors its origin (map growth) or was computed against the pre-map
    // authored grid, and a footprint registered from a stale frame lands cells away from the
    // building — a phantom attack target the flow fields then route enemies to.
    Vector2Int CurrentWorldAnchor(out Vector2Int sizeCells)
    {
        float cs = GridManager.i != null ? GridManager.i.cellSize : 1f;
        sizeCells = new Vector2Int(
            Mathf.Max(1, Mathf.RoundToInt(size.x / cs)),
            Mathf.Max(1, Mathf.RoundToInt(size.y / cs)));
        // nearest-lattice bottom-left corner, so the quantized rect stays CENTRED on the building
        // (flooring the centre cell biased the whole footprint up to a full cell down-left:
        // blocked cells hung past the collider on one side, and anything drawn from the
        // registered rect sat visibly off-centre against the sprite)
        Vector2 bl = (Vector2)transform.position - 0.5f * cs * (Vector2)sizeCells;
        return new Vector2Int(Mathf.RoundToInt(bl.x / cs), Mathf.RoundToInt(bl.y / cs));
    }

    void RegisterPathFootprint()
    {
        UnregisterPathFootprint();
        if (GridManager.i == null || !PathZone.AtBase(transform.position)) return;
        if (physic == null) return;   // no collider/life (pylons, plumbing) — blocks nothing, targeted by nothing
        regAnchor = CurrentWorldAnchor(out Vector2Int sizeCells);
        regSize = sizeCells;
        regAsWall = isWall && physic != null;
        if (regAsWall)
        {
            regWallLs = physic;
            BaseBlockMap.RegisterWallRect(physic, regAnchor, regSize);
        }
        else
        {
            BaseBlockMap.RegisterSolid(regAnchor, regSize);
        }
        footprintRegistered = true;
    }

    void UnregisterPathFootprint()
    {
        if (!footprintRegistered) return;
        footprintRegistered = false;
        if (regAsWall) BaseBlockMap.UnregisterWall(regWallLs);
        else BaseBlockMap.UnregisterSolid(regAnchor, regSize);
        regWallLs = null;
    }

    /// <summary>
    /// The registered pathfinding footprint of a live NON-WALL building (world-quantized cells —
    /// immune to GridManager growth re-anchoring, unlike <see cref="anchorCell"/>). This is what
    /// BasePathManager seeds as an attack target; false = not currently physically present, or a
    /// wall (walls become targets via gate lookup, never seeds).
    /// </summary>
    public bool TryGetPathFootprint(out Vector2Int worldCellAnchor, out Vector2Int cells)
    {
        worldCellAnchor = regAnchor; cells = regSize;
        return footprintRegistered && !regAsWall;
    }

    /// <summary>
    /// Self-heal the pathfinding registration: if the building has MOVED since it registered (or
    /// its registration came out of a stale grid frame), re-register from the live transform.
    /// Called by BasePathManager on its seeding cadence, so a footprint can never disagree with
    /// the building for more than a rebuild tick. Ghosts (unregistered) are left alone.
    /// </summary>
    public void EnsurePathFootprintCurrent()
    {
        if (!footprintRegistered)
        {
            // Registration can be MISSED entirely, not just stale: on scene load Building.Start
            // races GridManager sizing itself (a coroutine) — RegisterPathFootprint early-returns
            // and nothing retried, so every pre-placed wall was invisible to pathfinding for the
            // whole session. Heal here on the seeding cadence. Ghosts (inactive physic) stay out.
            if (builtYet && physic != null && !physic.hasDied && physic.gameObject.activeInHierarchy)
                RegisterPathFootprint();
            return;
        }
        if (CurrentWorldAnchor(out _) != regAnchor) { RegisterPathFootprint(); return; }
        // A registration can also be HOLLOW: every cell shadowed by a stale session's leaked
        // entries (domain reload is off), so we believe we're registered while the registry
        // answers "open ground". Dead incumbents are stealable now — re-register to reclaim.
        if (regAsWall && (!BaseBlockMap.TryGetWallCells(regWallLs, out List<Vector2Int> wcells, out _) || wcells.Count == 0))
            RegisterPathFootprint();
    }

    public void UpdateUI()
    {
        int alt = 0;
        for (int i = 0; i < tiles.Count; i++)
        {
            BaseTile t = tiles[i];
            if(t== null)
            {
                tiles.RemoveAt(i);
                i--;
                continue;
            }
            int ind = tiles.IndexOf(t);
            if (ind > 19) continue;
            t.transform.position = BM.i.UIspots[ind - alt].position;
            if(t.scienceRequisit)
            {
                if (BlueprintManager.sciences.Contains(t.txt.text.ToLower()))
                {
                    t.scienceRequisit = false; //Just been researched...
                    t.init = GS.ColourFromCost(t.cost);
                    t.background.color = t.init;
                }
            }
            if (t.showParam != null)
            {
                if (!t.showParam.Invoke())
                {
                    t.gameObject.SetActive(false);
                    alt++;
                    continue;
                }
            }
            t.gameObject.SetActive(true);
        }
    }

    protected void SwitchMonos(bool mode, bool init = false)
    {
        foreach(Behaviour beh in buildingBehaviours)
        {
            if (beh != null)
            {
                beh.enabled = mode;
            }
        }
        if(mode == false && !init) BDisable();
        else if(!init) BEnable();
        foreach (SpriteRenderer s in spriterenderers)
        {
            if (s != null)
            {
                s.color = mode ? Color.white : GS.ColFromEra();
            }
        }
        if (mode && sr != null)
        {
            LeanTween.cancel(sr.gameObject);   // kill any lingering ghost/repair tint tween
            sr.color = Color.white;
        }
        if (mode)
        {
            physic = Instantiate(Resources.Load<GameObject>(box?"Physic":"PhysicCircle"), transform.position, Quaternion.Euler(0f,0f,Random.Range(0f,360f)), transform).GetComponent<LifeScript>();
            if (hasAuthoredCollider)
            {
                // restore the prefab-tuned collider dims over the generic one-cell body
                if (box)
                {
                    var bc = physic.GetComponent<BoxCollider2D>();
                    if (bc != null) { bc.size = authoredBoxSize; bc.offset = authoredBoxOffset; }
                }
                else
                {
                    var cc = physic.GetComponent<CircleCollider2D>();
                    if (cc != null) { cc.radius = authoredCircleRadius; cc.offset = authoredBoxOffset; }
                }
            }
            physic.maxHp = maxHealth;
            physic.hp = maxHealth;
            physic.onDeaths.Add(this);
            physic.GetComponent<IClickableCarrier>().clickable = this;
            physic.gameObject.SetActive(true);
            RegisterPathFootprint();   // collider is live again — block (or chew-price) the cells
        }
        else
        {
            UIParent.gameObject.SetActive(false);
            UnregisterPathFootprint(); // ghost building blocks nothing — enemies walk the footprint
        }
    }

    public virtual void OnDestroy()
    {
        if(GS.qutting) return;
        UnregisterPathFootprint();
        BDisable();
        // Dungeon-placed buildings (telepads) never registered with the base grid.
        if (GridManager.i != null && PathZone.AtBase(transform.position))
            GridManager.i.SetArea(anchorCell, gridSize, false);
        _power?.Detach();
    }

    public void AddSlot(int[] cost, string nam, Sprite spr, bool destroyOnUseP, Action act, bool science = false, Action instantAction = null, Func<bool> optionalParameter = null, Func<bool> showParameter = null, GameObject g = null)
    {
        if(g == null)
        {
            g = gameObject;
        }
        BaseTile tile = Instantiate(UIManager.i.baseTile, UIParent.transform).GetComponent<BaseTile>();
        tile.scienceRequisit = science;
        tile.Init(hasExtraParent? g.transform.parent.gameObject : g,cost, nam, spr, destroyOnUseP, act, instantAction, optionalParameter, showParameter);
        tiles.Add(tile);
    }

    //Same As AddSlot but makes it invoke upgrade() with n bursts necessary, and instantly invoke SwitchMonos(false) when clicked.
    public void AddUpgradeSlot(int[] cost, string nam, Sprite spr, bool destroyOnUseP, Action act, int n, bool science = false,
        Action instantAction = null, Func<bool> optionalParameter = null, Func<bool> showParameter = null,
        GameObject g = null)
    {
        if (g == null)
        {
            g = gameObject;
        }

        science = false;
        
        BaseTile tile = Instantiate(UIManager.i.baseTile, UIParent.transform).GetComponent<BaseTile>();
        tile.SetTextN(n);
        tile.scienceRequisit = science;
        if (instantAction == null)
        {
            instantAction = () =>
            {
                SwitchMonos(false);
            };
        }
        else
        {
            instantAction += () =>
            {
                SwitchMonos(false);
            };
        }
        tile.Init(hasExtraParent ? g.transform.parent.gameObject : g, cost, nam, spr, destroyOnUseP, () => {Upgrade(act,n);}, instantAction,
            optionalParameter, showParameter);
        tiles.Add(tile);
    }

    protected Func<bool> Science(string scienceName)
    {
        return () => BlueprintManager.sciences.Contains(scienceName);
    }

    public virtual void OnDeath()
    {
        if (UIParent.activeInHierarchy)
        {
            OnClose.Invoke();
        }
        SwitchMonos(false);
        // Repairs are DRONE work now — no orbs, no ember. The ghost persists until repair drones
        // pump maxHealth worth of hp back in (RepairTick), which may span multiple drone-days.
        sr.LeanSRColor(new Color(1f, 0.5f, 0.5f, 0.5f), 0.2f).setEaseOutCubic();
        droneRepairGhost = true;
        repairHp = 0f;
    }

    // ------------------------------------------------------------------ drone repair

    // Ghost rebuild progress (hp equivalent). Persistent across days on purpose: SwitchMonos(true)
    // resets the live physic to full, so progress is banked HERE and only cashed in at completion.
    bool droneRepairGhost;
    float repairHp;

    public bool IsGhostAwaitingRepair => droneRepairGhost;

    /// <summary>True while a repair drone has something to do here: a destroyed ghost still being
    /// rebuilt, or a live building below max hp. Excludes unbuilt constructions and anything with
    /// ember icons in flight (initial build / upgrade — those flows own the physic).</summary>
    public bool NeedsDroneRepair
    {
        get
        {
            if (!builtYet || icons.Count > 0 || upgradeAction != null) return false;
            if (droneRepairGhost) return true;
            return physic != null && !physic.hasDied && physic.hp < maxHealth - 0.01f;
        }
    }

    /// <summary>Apply <paramref name="hp"/> of drone repair. Ghosts bank progress and reactivate
    /// at full maxHealth (SwitchMonos(true) then restores the physic at full); live buildings heal
    /// directly. Returns the hp actually applied so the drone can bill energy for real work only.
    /// Concurrent drones simply accumulate.</summary>
    public float RepairTick(float hp)
    {
        if (hp <= 0f) return 0f;
        if (droneRepairGhost)
        {
            float used = Mathf.Min(hp, maxHealth - repairHp);
            repairHp += used;
            if (sr != null)
                sr.color = Color.Lerp(new Color(1f, 0.5f, 0.5f, 0.5f), Color.white, repairHp / maxHealth);
            if (repairHp >= maxHealth - 0.001f)
            {
                droneRepairGhost = false;
                repairHp = 0f;
                SwitchMonos(true);
            }
            return used;
        }
        if (physic == null || physic.hasDied) return 0f;
        float applied = Mathf.Min(hp, maxHealth - physic.hp);
        if (applied <= 0f) return 0f;
        physic.Change(applied, -1, false);
        return applied;
    }

    void BuildFirst()
    {
        SwitchMonos(false,true);
        if (builtBlasts > 0)
        {
            LoadWithEEs(builtBlasts);
        }
        else
        {
            // 0-blast building: no ember icons — construction completes when its orb task fills
            // (BM wires CompleteViaOrbs into the orb magnets' action). Keep the registration
            // LoadWithEEs would have done.
            EnergyManager.i.AddBuilding(this);
        }
    }

    /// <summary>Completion path for builtBlasts == 0 buildings: the orb task filling IS the build —
    /// no ember blasts involved. Invoked from the orb magnets' completion action.</summary>
    public void CompleteViaOrbs()
    {
        if (builtYet) return;
        if (!startCalled)
        {
            // Start() hasn't run yet (fresh instance, synchronous orb-complete race) — retry next frame.
            this.QA(CompleteViaOrbs, 0f);
            return;
        }
        builtYet = true;
        SwitchMonos(true);
    }

    protected virtual void Refund()
    {
        GS.OnNewEra -= numTextAction;
    }

    /// <summary>
    /// USED TO INITIATE THE BUILDING WITH EEICONS & HOLD OFF REPAIRS WITH A SINGULAR HIDDEN ONE.
    /// </summary>
    protected void LoadWithEEs(int n, bool hidden = false)
    {
        for(int i = 0; i < n; i++)
        {
            icons.Add(Instantiate(SpawnManager.instance.EEIcon,transform.position + 0.25f * size.x * (Vector3)PositionRegularly(i,n), Quaternion.identity, transform));
            if (hidden)
            {
                icons[^1].gameObject.SetActive(false);
            }
        }
        
        if (!subscribed)
        {
            subscribed = true;
            if (!hidden)
            {
                numText.text = icons.Count.ToString();
                numText.gameObject.SetActive(true);
                prevN = icons.Count;
            }
        }

        numIconsTrue = n;
        EnergyManager.i.AddBuilding(this);
    }
    
    public void RemoveIcon()
    {
        if (icons[0].gameObject.activeInHierarchy)
        {
            icons[0].StartCoroutine(icons[0].SetDone());
        }
        else
        {
            Destroy(icons[0].gameObject);
        }
        
        if (icons.Count == prevN) //This is to make only one coroutine, as this func is called multiple times
        {
            RefreshManager.i.StartCoroutine(CountDown());
        }
        
        icons.RemoveAt(0);
        if (icons.Count != 0) return;
        EmbersEdge.EEExplodeEvent -= RemoveIcon;
        subscribed = false;
        RefreshManager.i.QA(() =>
        {
            if (!builtYet)
            {
                builtYet = true;
                SwitchMonos(true);
            }
            else
            {
                if (upgradeAction != null)
                {
                    upgradeAction.Invoke();
                    upgradeAction = null;
                }
                else
                {
                    Debug.LogWarning("NO UPGRADE ACTION SET");
                }
                SwitchMonos(true);
            }
        }, 0.8f);
    }

    protected void RemoveIcons()
    {
        if (subscribed)
        {
            EmbersEdge.EEExplodeEvent -= RemoveIcon;
            subscribed = false;
            for (int i = 0; i < icons.Count; i++)
            {
                Destroy(icons[i].gameObject);
                icons.RemoveAt(i);
                i--;
            }
        }
    }

    protected virtual void BEnable()
    {
        
    }
    
    protected virtual void BDisable()
    {

    }

    /// <summary>
    /// Standardised energy reporting for any power-consuming building, surfaced as the overlay icon.
    /// Call this every frame the building is trying to draw, passing the energy/sec it consumes at
    /// full capacity:
    ///   • Powered   — the grid meets <paramref name="desiredRate"/> (full capacity),
    ///   • Throttled — running, but the grid can't sustain that rate (reduced capacity),
    ///   • Unpowered — no energy left to draw.
    /// The test is RATE-based, not "is a whole charge stored", so a source that keeps up at its rate
    /// reads Powered right down to empty and then flips straight to Unpowered (no spurious flicker).
    /// Use <see cref="ClearEnergyStatus"/> when the building doesn't need energy (idle / full).
    /// </summary>
    protected void ReportEnergyDraw(float desiredRate)
    {
        if (Power.Energy <= 1e-3f)
            SetEnergyStatus(EnergyStatus.Unpowered);
        else if (Power.DrawRate < desiredRate - 1e-3f)
            SetEnergyStatus(EnergyStatus.Throttled);
        else
            SetEnergyStatus(EnergyStatus.Powered);
    }

    /// <summary>Hide the energy overlay — the building isn't trying to draw (idle / full).</summary>
    protected void ClearEnergyStatus() => SetEnergyStatus(EnergyStatus.Powered);

    private void SetEnergyStatus(EnergyStatus status)
    {
        // "No energy" is sticky: once shown it stays until the grid actually has energy again, so it
        // survives the building going idle / firing / cooling down. Only a genuine return of power
        // (or powering down via ClearEnergyStatusImmediate) clears it.
        if (energyStatus == EnergyStatus.Unpowered)
        {
            if (status == EnergyStatus.Unpowered) return;     // already showing it
            if (Power.Energy <= 1e-3f) return;                // still empty — keep "no energy" up
            // energy is back: fall through and apply the requested status
        }

        if (status == energyStatus) return;
        EnergyStatus prev = energyStatus;
        energyStatus = status;

        if (status == EnergyStatus.Unpowered)
        {
            StopEnergyLinger();
            ApplyEnergyIcon(EnergyStatus.Unpowered);
            // Watch the grid so it clears itself once power returns, even if the building stops asking.
            if (energyWatchCo == null) energyWatchCo = StartCoroutine(WatchForPower());
            return;
        }

        StopEnergyWatch();   // not unpowered any more

        if (status == EnergyStatus.Throttled)
        {
            // (Re)appearing: restart the minimum-show clock and show it solid.
            StopEnergyLinger();
            throttleShownAt = Time.time;
            ApplyEnergyIcon(EnergyStatus.Throttled);
            return;
        }

        // Leaving "insufficient" for "all good" mid-combat: hold it for a minimum on-screen time,
        // then flash it out, so it's readable. Skip when combat has ended (turrets off / end of day).
        if (prev == EnergyStatus.Throttled && status == EnergyStatus.Powered && Finder.turretsOn)
        {
            if (energyLingerCo == null) energyLingerCo = StartCoroutine(WindDownInsufficient());
            return;   // the wind-down coroutine holds + flashes the icon, then hides it
        }

        StopEnergyLinger();
        ApplyEnergyIcon(status);
    }

    // Keep "insufficient" up for at least energyMinShowTime total, then flash it for
    // throttleLingerTime before hiding. Bails (hides) early if combat ends mid-wind-down.
    IEnumerator WindDownInsufficient()
    {
        float hideTime = Mathf.Max(throttleShownAt + energyMinShowTime, Time.time + throttleLingerTime);
        float flashStart = hideTime - throttleLingerTime;
        while (Time.time < hideTime)
        {
            if (!Finder.turretsOn) break;   // end of day — drop it now
            if (energyStatusSR != null)
            {
                bool flashing = Time.time >= flashStart;
                energyStatusSR.enabled = !flashing || Mathf.Repeat(Time.time, energyFlashPeriod) < energyFlashPeriod * 0.5f;
            }
            yield return null;
        }
        energyLingerCo = null;
        if (energyStatus == EnergyStatus.Powered) ApplyEnergyIcon(EnergyStatus.Powered);
    }

    void StopEnergyLinger()
    {
        if (energyLingerCo != null) { StopCoroutine(energyLingerCo); energyLingerCo = null; }
    }

    // Holds the sticky "no energy" icon until the grid can supply again, even if the building stops
    // reporting (idle / between waves). Clears it the moment power returns.
    IEnumerator WatchForPower()
    {
        while (Power.Energy <= 1e-3f) yield return null;
        energyWatchCo = null;
        if (energyStatus == EnergyStatus.Unpowered)
        {
            energyStatus = EnergyStatus.Powered;
            ApplyEnergyIcon(EnergyStatus.Powered);   // the building's next report sets the real state
        }
    }

    void StopEnergyWatch()
    {
        if (energyWatchCo != null) { StopCoroutine(energyWatchCo); energyWatchCo = null; }
    }

    /// <summary>Hide the overlay at once, bypassing the throttle linger / sticky no-energy (building powering down / removed).</summary>
    protected void ClearEnergyStatusImmediate()
    {
        StopEnergyLinger();
        StopEnergyWatch();
        energyStatus = EnergyStatus.Powered;
        ApplyEnergyIcon(EnergyStatus.Powered);
    }

    // Lazily creates a world-space icon above the building. Icons live on the UIManager singleton so
    // every power-consuming building shares one assignment; no per-prefab setup.
    private void ApplyEnergyIcon(EnergyStatus status)
    {
        Sprite icon = null;
        if (UIManager.i != null)
        {
            icon = status switch
            {
                EnergyStatus.Unpowered => UIManager.i.noEnergyIcon,
                EnergyStatus.Throttled => UIManager.i.insufficientEnergyIcon,
                _ => null
            };
        }

        if (icon == null)
        {
            if (energyStatusSR != null) energyStatusSR.enabled = false;
            return;
        }

        if (energyStatusSR == null)
        {
            // Anchor to the non-rotating root: for hasExtraParent buildings the script lives on a
            // child that rotates to aim (e.g. the Mine Sprayer's turret), so use the parent instead.
            Transform anchor = (hasExtraParent && transform.parent != null) ? transform.parent : transform;
            var go = new GameObject("EnergyStatus");
            go.transform.SetParent(anchor, false);
            go.transform.localPosition = energyIconOffset;
            go.transform.localScale = Vector3.one * energyIconScale;
            energyStatusSR = go.AddComponent<SpriteRenderer>();
            // Draw above the building's own art.
            if (sr != null)
            {
                energyStatusSR.sortingLayerID = sr.sortingLayerID;
                energyStatusSR.sortingOrder = sr.sortingOrder + 50;
            }
            else
            {
                energyStatusSR.sortingOrder = 100;
            }
        }

        energyStatusSR.sprite = icon;
        energyStatusSR.enabled = true;
        energyStatusSR.color = new Color(1, 1f, 1f, 0.5f);
    }

    IEnumerator CountDown()
    {
        yield return new WaitForSeconds(0.2f);
        for(int i = prevN - 1; i >= icons.Count; i--)
        {
            for(float t = 0f; t < 1f; t += 3 * Time.deltaTime)
            {
                numText.color = new Color(numText.color.r,numText.color.g,numText.color.b,1f-t);
                yield return null;
            }
            numText.text = i.ToString();
            for(float t = 1f; t > 0f; t -=  3* Time.deltaTime)
            {
                numText.color = new Color(numText.color.r,numText.color.g,numText.color.b,1f-t);
                yield return null;
            }
            yield return new WaitForSeconds(0.1f);
        }
        prevN = icons.Count;
        if (prevN == 0)
        {
            numText.gameObject.SetActive(false);
        }
    }

    public virtual void OnClick()
    {
        if (enabled)
        {
            if (!UIParent.activeInHierarchy && canOpen)
            {
                OnOpen?.Invoke();
            }
            else
            {
                OnClose?.Invoke();
            }
        }
    }

    protected void Shut()
    {
        canOpen = false;
        OnClose.Invoke();
    }

    public static void CloseBuildingUIs()
    {
        foreach (Building b in buildings)
        {
            if (b.UIParent.activeInHierarchy)
            {
                b.UIParent.SetActive(false);
                b.OnClose?.Invoke();
            }
        }
    }

    public static bool CheckBuildingUIs()
    {
        foreach (Building b in buildings)
        {
            if (b.UIParent.activeInHierarchy)
            {
                return false;
            }
        }
        return true;
    }

    protected static Vector2 GetNearestEE(Transform pos)
    {
        Vector2 nearest = Vector2.zero;
        float dist = Mathf.Infinity;
        foreach (EmbersEdge ee in SpawnManager.instance.EEs)
        {
            float d = Vector2.SqrMagnitude(ee.transform.position-pos.position);
            if (!(d < dist)) continue;
            dist = d;
            nearest = ee.transform.position;
        }
        return nearest;
    }

    protected void Upgrade(Action upgradeAct, int n) //this is the action for the newtask when upgrading.
    {
        LoadWithEEs(n,false);
        if (physic != null)
        {
            Destroy(physic.gameObject);
        }
        upgradeAction = upgradeAct;
    }
    
    /// <summary>
    /// Marks the grid cells occupied by this building as filled in the GridManager.
    /// </summary>
    void RegisterGridOccupancy()
    {
        // The base build grid doesn't exist in the dungeon — dungeon placement (telepads) keeps
        // its own occupancy on the mine field instead.
        if (GridManager.i == null || !PathZone.AtBase(transform.position)) return;

        Vector2Int sizeCells = new Vector2Int(
            Mathf.Max(1, Mathf.RoundToInt(size.x / GridManager.i.cellSize)),
            Mathf.Max(1, Mathf.RoundToInt(size.y / GridManager.i.cellSize)));

        // Anchor at the bottom‑left grid cell so the footprint matches the placement logic.
        Vector2Int anchor = GridManager.i.WorldToGrid(transform.position)
                             - new Vector2Int(sizeCells.x / 2, sizeCells.y / 2);

        anchorCell = anchor;
        gridSize = sizeCells;
        GridManager.i.SetArea(anchor, sizeCells, true);
    }

    Vector2 PositionRegularly(int n, int max)
    {
        if (max == 1)
        {
            return Vector2.zero;
        }
        float angle = n * Mathf.PI * 2f / max;
        float y = Mathf.Cos(angle);
        float x = Mathf.Sin(angle);
        return new Vector2(x, y);
    }
}
