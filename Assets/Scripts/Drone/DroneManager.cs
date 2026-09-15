using System.Collections;
using UnityEngine;

/// <summary>
/// Central tunables + orchestration for the drone workforce. One per scene (the drone kit
/// builder wires it into World.unity; Ensure() covers scenes without one). Everything drones
/// do is paced by <see cref="Haste"/> so the feel can be tuned from a single dial.
/// </summary>
public class DroneManager : MonoBehaviour
{
    public static DroneManager i;

    [Header("Feel")]
    [Tooltip("Master speed multiplier for everything drones do: move force, drill rate, repair rate. Raise to make the workforce snappier.")]
    public float haste = 1.5f;
    [Tooltip("Movement force per physics tick — code-set onto every drone at Start (the prefab-serialized value is stale).")]
    public float droneMoveForce = 5f;
    [Tooltip("Cruise speed cap (ActionScript maxVelocity) — code-set onto every drone at Start.")]
    public float droneMaxVelocity = 4.5f;
    [Tooltip("Max turn rate (deg/s) for all drone facing — the body sweeps around instead of snapping.")]
    public float droneTurnSpeed = 720f;

    [Header("Threat response")]
    [Tooltip("An enemy within this range makes a drone flee.")]
    public float threatRadius = 4f;
    [Tooltip("A fleeing drone relaxes once no enemy is within this range (hysteresis; keep > threatRadius).")]
    public float threatClearRadius = 6f;
    [Tooltip("How far a rally-fighting drill drone may stray from its rally point.")]
    public float rallyLeash = 7f;
    [Tooltip("Rally-fight drill contact damage per second, per enemy in reach (scaled by haste).")]
    public float drillCombatDps = 0.7f;
    [Tooltip("Base-side bag drones caught inside a hot rally zone run out to this radius from (0,0) and wait the fight out.")]
    public float bagRallyStandoff = 4f;

    [Header("Loot")]
    [Tooltip("Where returning bag drones dump their haul at base.")]
    public Vector2 scrapPoint = new Vector2(0f, -6f);
    [Tooltip("Seconds after a wave clears before battery logistics may dispatch — repairs and chip runs get first claim on the fleet.")]
    public float batteryWorkDelayAfterWave = 4f;

    [Header("Idle life")]
    [Tooltip("How far an off-duty drone patrols from its dock.")]
    public float idleWanderRadius = 8f;
    [Tooltip("Cruise multiplier while patrolling/chatting (work speed = 1) — idle drones drift, they don't commute.")]
    public float idleSpeedScale = 0.55f;
    [Tooltip("Seconds a drone sits docked before heading back out on patrol (each drone's restlessness skews this). Patrols are endless — this only matters after a recharge or a wave.")]
    public float idleCooldown = 6f;

    [Header("Base ore mining")]
    [Tooltip("Seconds of drill contact to eat one marked ore tile whole (yield is HALF its value — drones are half as ore-efficient as buildings).")]
    public float baseOreEatSeconds = 3f;
    [Tooltip("Energy (of a drone's 10) per ore-chip unit actually released when base-mining.")]
    [UnityEngine.Serialization.FormerlySerializedAs("baseOreCostPerOrb")]
    public float baseOreCostPerUnit = 0.5f;
    [Tooltip("Seconds of click-hold on a base ore tile to toggle its deconstruction mark.")]
    public float oreMarkHoldSeconds = 1f;

    [Header("Energy tariffs (a full drone charge = Drone.MaxEnergy = 10)")]
    [Tooltip("Energy per 1 hp healed on buildings/vehicles (0.25 => 40 hp per full charge).")]
    public float repairCostPerHp = 0.25f;
    public float drillCostRegular = 0.5f;
    public float drillCostHard = 1.5f;
    public float drillCostVeryHard = 3f;
    [Tooltip("Handling energy a BAG drone spends per chip it picks up, on top of the bag's space tariff (0.1 => 100 chips per full charge).")]
    public float chipMoveCost = 0.1f;
    [Tooltip("Energy a drone WITHOUT a bag (claws) spends per chip it picks up — its whole cost of moving that chip (0.5 => 20 chips per full charge).")]
    public float bareChipPickupCost = 0.5f;
    [Tooltip("Energy-worth of drilling one drill survives (block costs accrue; 10 = one full charge of blocks, across dives). The drill breaks when it's spent — drills and bags are single-use kit, forged again at their workshops.")]
    public float drillCapacity = 10f;
    [Header("Drone expression")]
    [Tooltip("Show the ASCII speech bubbles over emoting drones. OFF (user rule 2026-09-13): an emote only drives the body animation — the 0,1,2 / 2,1,0 sweep for the beat, the plain frame cycle otherwise.")]
    public bool speechBubbles = false;
    [Header("Chip clear cycles")]
    [Tooltip("Loose base-side chips fade out when a wave clears. Chips on a powered Tube shelf or inside a building's intake ring are spared. (Teleporting never fades base chip — only the dungeon's debris fades on the way home.)")]
    public bool clearBaseChipsOnWaveClear = true;
    [Tooltip("Seconds a chip takes to fade at a clear cycle.")]
    public float chipFadeSeconds = 0.45f;
    [Tooltip("Chips a REGULAR (no-kit) drone carries in its claws per trip, ANY size — construction hauling only (1 = one chip at a time).")]
    public int bareChipsPerTrip = 1;

    [Header("Ore chips")]
    [Tooltip("LEGACY (plain rock no longer drops chip — only ore blocks do). Kept for the inspector's sake.")]
    public int chipsMinPerBreak = 1;
    public int chipsMaxPerBreak = 3;
    [Tooltip("Ore INTENSITY → chip blend (index = tier: 0 low, 1 mid, 2 high). A broken dungeon ore block " +
             "drops chipsPerBlock chips (1–5 across the tiers); a base ore unit drops one — each chip's size " +
             "is rolled from the tier's small/medium/large weights (they needn't sum to 1).")]
    public OreTierYield[] oreTiers =
    {
        new OreTierYield("Low",  1, 3, 0.90f, 0.10f, 0.00f),
        new OreTierYield("Mid",  2, 4, 0.70f, 0.27f, 0.03f),
        new OreTierYield("High", 3, 5, 0.50f, 0.38f, 0.12f),
    };
    [Tooltip("Largest chip size class BASE ore may release (0 small / 1 medium / 2 large). Large chip is the dungeon's prize: base tiles and the Cell roll their tier's blend but a roll above this drops to it.")]
    [Range(0, 2)] public int baseOreMaxChipSize = 1;
    public static int BaseOreMaxChipSize => i != null ? i.baseOreMaxChipSize : 1;
    [Tooltip("Hard cap on live chips; oldest are culled first.")]
    public int maxChips = 300;
    [Tooltip("HDR multiplier on the era ore material's `thecolor` for chips, BY SIZE CLASS (small / medium / large) — a bigger chip glows hotter.")]
    public float[] oreChipGlowBySize = { 1.4f, 2.6f, 4.8f };
    float ChipGlow(int sizeClass)
    {
        if (oreChipGlowBySize == null || oreChipGlowBySize.Length == 0) return 2.5f;
        return oreChipGlowBySize[Mathf.Clamp(sizeClass, 0, oreChipGlowBySize.Length - 1)];
    }

    public static float Haste => i != null ? i.haste : 1.5f;
    public static float DroneMoveForce => i != null ? i.droneMoveForce : 5f;
    public static float DroneMaxVelocity => i != null ? i.droneMaxVelocity : 4.5f;
    public static float DroneTurnSpeed => i != null ? i.droneTurnSpeed : 720f;
    public static float DrillCombatDps => i != null ? i.drillCombatDps : 0.7f;
    public static float ThreatRadius => i != null ? i.threatRadius : 4f;
    public static float ThreatClearRadius => i != null ? i.threatClearRadius : 6f;
    public static float RallyLeash => i != null ? i.rallyLeash : 7f;
    public static float BagRallyStandoff => i != null ? i.bagRallyStandoff : 4f;
    public static Vector2 ScrapPoint => i != null ? i.scrapPoint : new Vector2(0f, -6f);
    public static float RepairCostPerHp => i != null ? i.repairCostPerHp : 0.25f;
    public static float ChipMoveCost => i != null ? i.chipMoveCost : 0.1f;
    public static float BareChipPickupCost => i != null ? i.bareChipPickupCost : 0.5f;
    public static float DrillCapacity => i != null ? Mathf.Max(0.01f, i.drillCapacity) : 10f;
    public static bool ClearBaseChipsOnWaveClear => i == null || i.clearBaseChipsOnWaveClear;
    public static bool SpeechBubbles => i != null && i.speechBubbles;
    public static float ChipFadeSeconds => i != null ? Mathf.Max(0.05f, i.chipFadeSeconds) : 0.45f;
    public static int BareChipsPerTrip => i != null ? Mathf.Max(1, i.bareChipsPerTrip) : 1;
    public static float IdleWanderRadius => i != null ? i.idleWanderRadius : 8f;
    public static float IdleSpeedScale => i != null ? i.idleSpeedScale : 0.55f;
    public static float IdleCooldown => i != null ? i.idleCooldown : 6f;
    public static float BaseOreEatSeconds => i != null ? i.baseOreEatSeconds : 3f;
    public static float BaseOreCostPerUnit => i != null ? i.baseOreCostPerUnit : 0.5f;

    /// <summary>Standing drill-drone demand across every base pad's request slots.</summary>
    public static int TelepadDrillDemand()
    {
        return TelepadNetwork.BaseDrillDemand();
    }

    static int DrillDroneCount()
    {
        int n = 0;
        for (int k = 0; k < AllyAI.allies.Count; k++)
            if (AllyAI.allies[k] is Drone d && d != null && d.equipment == DroneEquipment.Drill) n++;
        return n;
    }

    /// <summary>May drill drones spend charge on marked base ore? NO while the whole drill fleet
    /// is spoken for by telepad requests — those drones save their energy for the dive. The one
    /// exception: the player is ALREADY down in the dungeon, so a base-side drill drone missed
    /// the dive anyway; it mines now and recharges overnight like everyone else.</summary>
    public static bool BaseMiningAllowed()
    {
        if (PortalScript.i != null && PortalScript.i.inDungeon) return true;
        return TelepadDrillDemand() < DrillDroneCount();
    }

    /// <summary>Bag capacity in space units, derived from the drill economy so the two kits stay
    /// matched: almost (90% of) the chip space a drill drone's full charge produces on regular
    /// rock — breaks per charge × average chips per break × 1 space per regular chip. Code-set
    /// onto the sack at Start; the SackWobble prefab's serialized maxSpace is dead.</summary>
    public static int BagCapacity
    {
        get
        {
            float costReg = i != null ? i.drillCostRegular : 0.05f;
            float avgChips = i != null ? (i.chipsMinPerBreak + i.chipsMaxPerBreak) * 0.5f : 2f;
            return Mathf.Max(8, Mathf.RoundToInt(avgChips * 0.9f / Mathf.Max(0.01f, costReg)));
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        i = null;
        jobCacheTime = float.NaN;
        baseZoneHotAt = float.NegativeInfinity;
        batteryHoldUntil = float.NegativeInfinity;
    }

    // ---- post-wave battery grace ----
    static float batteryHoldUntil = float.NegativeInfinity;

    /// <summary>Battery logistics (station hauls, returns, swaps, distribution) hold off for a
    /// beat after each wave clears, so the fleet's first seconds go to repairs and loot instead
    /// of shuffling batteries. Chip runs are NOT gated — feeding the grinders is loot work.</summary>
    public static bool BatteryWorkAllowed => Time.time >= batteryHoldUntil;

    // ---- shared job-board caches: every idle drone polls the same questions every fixed tick,
    // so answer them once per tick (and once per 0.3s for the rally probe) for the whole fleet ----
    static float jobCacheTime = float.NaN;
    static bool cachedBaseMining;
    static int cachedRepairNeedBase, cachedRepairNeedAway;
    static float baseZoneHotAt = float.NegativeInfinity;
    static bool baseZoneHotVal;

    static void EnsureJobCaches()
    {
        if (Time.fixedTime == jobCacheTime) return;
        jobCacheTime = Time.fixedTime;
        cachedBaseMining = BaseMiningAllowed();
        cachedRepairNeedBase = 0;
        cachedRepairNeedAway = 0;
        var list = Building.buildings;
        for (int k = 0; k < list.Count; k++)
        {
            Building b = list[k];
            if (b == null || !b.gameObject.activeInHierarchy || !b.NeedsDroneRepair) continue;
            if (PathZone.AtBase(b.transform.position)) cachedRepairNeedBase++;
            else cachedRepairNeedAway++;
        }
    }

    /// <summary>Per-tick cached <see cref="BaseMiningAllowed"/> for job-board polling.</summary>
    public static bool BaseMiningAllowedCached()
    {
        EnsureJobCaches();
        return cachedBaseMining;
    }

    /// <summary>Is there repair work LEFT OVER for another drone on this side? One drone per
    /// needy building: the old bare existence check pulled the ENTIRE housekeeper fleet onto a
    /// single scratched wall, so battery logistics never saw a free drone after a wave. Extras
    /// now fall through to the rest of the job board; RepairSweep still runs its own
    /// nearest-target scan once dispatched.</summary>
    public static bool RepairWorkAvailable(bool atBase)
    {
        EnsureJobCaches();
        int need = atBase ? cachedRepairNeedBase : cachedRepairNeedAway;
        if (need <= 0) return false;
        for (int k = 0; k < AllyAI.allies.Count; k++)
        {
            if (AllyAI.allies[k] is not Drone d || d == null) continue;
            if (d.state != Drone.State.RepairSweep) continue;
            if (PathZone.AtBase(d.transform.position) != atBase) continue;
            if (--need <= 0) return false;
        }
        return true;
    }

    /// <summary>Anything pressing the base rally point (0,0) right now? One shared probe with a
    /// 0.3s TTL instead of one identical physics query per drone per threat tick.</summary>
    public static bool BaseZoneHot(string tag)
    {
        if (Time.time - baseZoneHotAt >= 0.3f)
        {
            baseZoneHotAt = Time.time;
            baseZoneHotVal = GS.FindEnemies(tag, Vector2.zero, RallyLeash, false, false).Count > 0;
        }
        return baseZoneHotVal;
    }

    public static DroneManager Ensure()
    {
        if (i == null) i = new GameObject("DroneManager").AddComponent<DroneManager>();
        return i;
    }

    void Awake()
    {
        if (i == null) i = this;
    }

    System.Action<bool> teleport;
    System.Action<int> newEra;
    System.Action waveComplete;
    SpawnManager.DayState lastDayState = SpawnManager.DayState.Day;

    void Start()
    {
        StartCoroutine(SubscribeWhenReady());
        newEra = _ => TearDownDungeonPads();
        GS.OnNewEra += newEra;
    }

    IEnumerator SubscribeWhenReady()
    {
        while (PortalScript.i == null || SpawnManager.instance == null) yield return null;
        teleport = OnTeleport;
        PortalScript.i.onTeleport += teleport;
        waveComplete = OnWaveComplete;
        SpawnManager.instance.onWaveComplete += waveComplete;
    }

    void OnDestroy()
    {
        if (PortalScript.i != null && teleport != null) PortalScript.i.onTeleport -= teleport;
        if (SpawnManager.instance != null && waveComplete != null) SpawnManager.instance.onWaveComplete -= waveComplete;
        if (newEra != null) GS.OnNewEra -= newEra;
        if (i == this) i = null;
    }

    void Update()
    {
        TickOreMarkInput();
        TickPadReservations();
        // No PreAttack event exists — watch the state machine: wave incoming ⇒ pilots board.
        // Vehicles fighting IN THE DUNGEON sit out the base wave cycle entirely.
        var sm = SpawnManager.instance;
        if (sm == null) return;
        if (sm.dayState != lastDayState)
        {
            if (sm.dayState == SpawnManager.DayState.PreAttack)
                for (int k = PilotedVehicle.all.Count - 1; k >= 0; k--)
                {
                    var v = PilotedVehicle.all[k];
                    if (v != null && !v.transform.InDungeon()) v.WaveStarting();
                }
            lastDayState = sm.dayState;
        }
    }

    /// <summary>Wave cleared: vehicles power down, pilots pop out to heal their hulls and
    /// recharge — the battery-logistics grace window starts (see BatteryWorkAllowed) and the
    /// base's loose chips were eaten here too, but that wipe now fires INSTANTLY at the wave clear
    /// (SpawnManager.NextDayFR → ChipClearCycle.OnWaveClear), 2 s before this hook.</summary>
    void OnWaveComplete()
    {
        batteryHoldUntil = Time.time + Mathf.Max(0f, batteryWorkDelayAfterWave);
        // (the chip despawn no longer rides this 2 s-delayed hook — SpawnManager.NextDayFR runs
        // ChipClearCycle.OnWaveClear the instant the wave clears, ahead of the new day's Cell batch)
        for (int k = PilotedVehicle.all.Count - 1; k >= 0; k--)
        {
            var v = PilotedVehicle.all[k];
            if (v != null && !v.transform.InDungeon()) v.WaveEnded();
        }
    }

    // ------------------------------------------------------------------ ore marking input

    // Click-hold on a base ore tile toggles its deconstruction mark (OreMarks). Ore tiles carry
    // no colliders, so this can't ride FocusRouter's raycast pipeline — a plain cursor poll over
    // the resource tilemaps does it. A press that starts on any collider (building, unit) is
    // ignored so the hold never fights a UI/drag interaction.
    Ore holdOre;
    float holdT;
    bool holdConsumed;

    void TickOreMarkInput()
    {
        if (IM.i == null || IM.i.pi == null) return;
        if (!IM.i.pi.Player.Interact.IsPressed())
        {
            holdOre = null;
            holdConsumed = false;
            return;
        }
        if (holdConsumed || FocusRouter.Suppressed) return;
        if (BM.i != null && BM.i.planting) return;
        if (PortalScript.i != null && PortalScript.i.inDungeon) return;

        Vector2 w = IM.controller ? (Vector2)IM.i.CWorldPoint() : IM.i.MousePosition();
        Ore o = OreAt(w);
        if (o != holdOre)
        {
            holdOre = null;
            if (o == null) return;
            // fresh hold — but only when the press isn't claimed by something clickable
            var mask = LayerMask.GetMask("Ally Buildings", "Ally Units", "CharOnly", "UI");
            if (Physics2D.Raycast(new Vector3(w.x, w.y, -100f), Vector3.forward, 1000f, mask)) return;
            holdOre = o;
            holdT = 0f;
            return;
        }
        if (holdOre == null) return;
        holdT += Time.deltaTime;
        if (holdT < Mathf.Max(0.15f, oreMarkHoldSeconds)) return;
        OreMarks.Toggle(holdOre);
        holdOre = null;
        holdConsumed = true;   // one toggle per press — release to mark another
    }

    // Clearance an ore tile needs from the map boundary before it can be hold-marked: half the
    // boundary line's drawn width (0.2) plus a small grace, so a tile partially under the ember's
    // edge never reads as clickable.
    const float edgeClearance = 0.15f;

    static Ore OreAt(Vector2 w)
    {
        var maps = TilemapResource.Maps;
        if (maps == null) return null;
        for (int t = 0; t < maps.Length; t++)
        {
            var map = maps[t];
            if (map == null) continue;
            var cell = map.WorldToCell(w);
            var g = map.GetInstantiatedObject(cell);
            var o = g != null ? g.GetComponent<Ore>() : null;
            if (o == null || o.Depleted) continue;
            // a tile partially obscured by the boundary line isn't selectable — every corner must
            // sit inside the map with clearance for the line's width
            Vector2 c = map.GetCellCenterWorld(cell);
            Vector2 h = 0.5f * (Vector2)map.cellSize;
            if (!MapManager.InsideBoundsWithClearance(new Vector2(c.x - h.x, c.y - h.y), edgeClearance) ||
                !MapManager.InsideBoundsWithClearance(new Vector2(c.x + h.x, c.y - h.y), edgeClearance) ||
                !MapManager.InsideBoundsWithClearance(new Vector2(c.x - h.x, c.y + h.y), edgeClearance) ||
                !MapManager.InsideBoundsWithClearance(new Vector2(c.x + h.x, c.y + h.y), edgeClearance))
                continue;
            return o;
        }
        return null;
    }

    // ------------------------------------------------------------------ pad reservations

    // Standing requests don't just fill at dive time — they PRE-POSITION the fleet: matching
    // charged drones are reserved (assignedPad) and sent to wait AT their base pad, so the
    // squad is already gathered when the player dives. Re-checked on a slow tick: request
    // edits, recharges and recalls all self-heal within half a second.
    float reserveT;

    void TickPadReservations()
    {
        if ((reserveT -= Time.deltaTime) > 0f) return;
        reserveT = 0.5f;
        if (PortalScript.i == null || PortalScript.i.inDungeon) return;   // base side only
        foreach (Building b in Building.buildings)
        {
            if (b is not Telepad pad || pad.IsDungeonSide || !pad.IsOperational) continue;
            ReserveDrones(pad, DroneEquipment.Drill, pad.reqDrill);
            ReserveDrones(pad, DroneEquipment.Bag, pad.reqBag);
        }
    }

    static void ReserveDrones(Telepad basePad, DroneEquipment kind, int want)
    {
        // drones already holding this reservation keep it; surplus (requests shrank) and FLAT
        // holders stand down — a flat drone docks to recharge, a charged one takes its slot
        for (int k = 0; k < AllyAI.allies.Count; k++)
        {
            if (AllyAI.allies[k] is not Drone d || d == null) continue;
            if (d.assignedPad != basePad || d.transform.InDungeon() || d.equipment != kind) continue;
            if (want > 0 && d.Charged) want--;
            else d.ReleasePadReservation();
        }
        Vector2 padPos = basePad.transform.position;
        while (want-- > 0)
        {
            Drone pick = null;
            float bestSqr = float.MaxValue;
            for (int k = 0; k < AllyAI.allies.Count; k++)
            {
                if (AllyAI.allies[k] is not Drone d || d == null) continue;
                if (d.assignedPad != null || d.transform.InDungeon() || !d.Charged || d.equipment != kind) continue;
                if (d.pilotOf != null || d.state == Drone.State.BoardingVehicle || d.HasCargo) continue;
                if (d.state == Drone.State.PadPause) continue;   // mid-breather after a return
                if (d.state == Drone.State.BatteryWork) continue;   // mid station-run: the battery/chip logistics finish first
                float d2 = ((Vector2)d.transform.position - padPos).sqrMagnitude;
                if (d2 < bestSqr) { bestSqr = d2; pick = d; }
            }
            if (pick == null) return;   // fleet exhausted — remaining requests stay standing
            pick.AssignToPad(basePad);
        }
    }

    // ------------------------------------------------------------------ deploy / recall

    /// <summary>Drones ride the player's teleport: dive deploys every assigned charged drone
    /// through its pad link; returning home force-recalls EVERY drone still in the dungeon
    /// (mid-flight ones included — the frozen dimension must never strand a drone) and runs
    /// the clear cycle ("chips last until you return to base" — dungeon debris fades, and so
    /// do loose base chips unless a powered Tube holds them; see ChipClearCycle).</summary>
    void OnTeleport(bool nowInDungeon)
    {
        if (nowInDungeon)
        {
            DeployAssigned();
        }
        else
        {
            RecallAllFromDungeon();
            ChipClearCycle.OnReturnHome();
            // home again — the pad-restock window opens: only now (until the next day tick) may
            // the fleet lift still-charged batteries off working pads for their station visit
            BatteryStation.StampHomecoming();
        }
    }

    // ------------------------------------------------------------------ ore chips

    public static float DrillCost(CellType tier)
    {
        var m = i;
        if (tier == CellType.VeryHard) return m != null ? m.drillCostVeryHard : 3f;
        if (tier == CellType.Hard) return m != null ? m.drillCostHard : 1.5f;
        return m != null ? m.drillCostRegular : 0.5f;
    }

    Sprite[] chipSprites;
    GameObject chipPrefab;
    readonly Material[] chipOreMats = new Material[3];   // glow-boosted runtime copies per size class, built lazily
    static readonly int ThecolorID = Shader.PropertyToID("thecolor");

    /// <summary>The blend table for an ore intensity tier (clamped; safe with an empty table).</summary>
    public static OreTierYield TierYield(int tier)
    {
        var t = i != null ? i.oreTiers : null;
        if (t == null || t.Length == 0) return OreTierYield.Default(tier);
        return t[Mathf.Clamp(tier, 0, t.Length - 1)] ?? OreTierYield.Default(tier);
    }

    /// <summary>Roll one chip's size class (0 small / 1 medium / 2 large) from a tier's blend.</summary>
    public static int RollChipSize(int tier) => TierYield(tier).RollSize();

    /// <summary>Called from MineField.BreakCell for EVERY broken wall (player or drone). Null-safe
    /// static: quietly no-ops when no manager exists. Chip comes ONLY out of ORE blocks — plain
    /// rock breaks to nothing — and how MUCH comes out is the block's INTENSITY tier
    /// (<paramref name="oreTier"/>: -1 none, 0 low, 1 mid, 2 high): a low block sheds a chip or
    /// two, mostly small; a high one two or three, mostly medium with the odd large.</summary>
    public static void SpawnChips(Vector3 pos, int oreTier)
    {
        if (i == null) return;
        if (oreTier < 0) return;   // plain rock: no debris
        var yield = TierYield(oreTier);
        int n = Random.Range(yield.chipsPerBlock.x, yield.chipsPerBlock.y + 1);
        var mf = MineField.i;
        for (int k = 0; k < n; k++)
        {
            int size = yield.RollSize();
            float pad = 0.06f + 0.035f * size;   // sprite half-extent (matches OreChip.WallPad)
            // stay inside the cavity: a scatter offset whose padded footprint touches rock
            // snaps back to the freshly-broken cell's centre (open by definition)
            Vector3 p = pos + GS.RandCircle(0.05f, 0.4f);
            if (mf != null && !FitsInCavity(mf, p, pad)) p = pos;
            i.SpawnChip(p, size, 0, pos);
        }
    }

    /// <summary>Base-side ore yield: <paramref name="units"/> chips beside <paramref name="pos"/>,
    /// each sized from the tile's intensity blend (a drill drone eating a marked tile, the Cell
    /// harvesting its neighbours or manifesting its own ore) — capped at
    /// <see cref="baseOreMaxChipSize"/>: the largest chip only comes out of the mines.</summary>
    public static void SpawnOreUnits(Vector3 pos, int tier, int units)
    {
        if (i == null) Ensure();
        for (int k = 0; k < units; k++)
            i.SpawnChip(pos + GS.RandCircle(0.1f, 0.5f), RollBaseChipSize(tier), 0, pos);
    }

    /// <summary>A base-ore roll: the tier's blend, clamped to <see cref="baseOreMaxChipSize"/>.</summary>
    public static int RollBaseChipSize(int tier) => Mathf.Min(RollChipSize(tier), BaseOreMaxChipSize);

    /// <summary>True when a chip-sized square (half-extent <paramref name="pad"/>) around
    /// <paramref name="p"/> touches no solid cell — i.e. the whole SPRITE sits in open cavity.</summary>
    static bool FitsInCavity(MineField mf, Vector2 p, float pad)
    {
        return !mf.IsSolidWorld(new Vector2(p.x - pad, p.y - pad))
            && !mf.IsSolidWorld(new Vector2(p.x + pad, p.y - pad))
            && !mf.IsSolidWorld(new Vector2(p.x - pad, p.y + pad))
            && !mf.IsSolidWorld(new Vector2(p.x + pad, p.y + pad));
    }

    OreChip SpawnChip(Vector3 pos, int sizeClass, int element, Vector2 burstFrom, float kick = 1f)
    {
        // never cache a failed load — an empty result (asset pipeline mid-refresh) would
        // otherwise poison the whole session
        if (chipSprites == null || chipSprites.Length < 12)
            chipSprites = LoadStripNumeric("OreChips");
        if (chipPrefab == null)
            chipPrefab = Resources.Load<GameObject>("OreChip");
        if (chipSprites.Length < 12) return null;

        // cap: cull the oldest chip
        if (OreChip.all.Count >= maxChips && OreChip.all.Count > 0)
        {
            var oldest = OreChip.all[0];
            if (oldest != null) Destroy(oldest.gameObject);
            else OreChip.all.RemoveAt(0);
        }

        OreChip chip;
        if (chipPrefab != null)
        {
            chip = Instantiate(chipPrefab, pos, Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)),
                GS.FindParent(GS.Parent.loot)).GetComponent<OreChip>();
        }
        else
        {
            // no prefab yet (kit builder not run) — build one from scratch
            var go = new GameObject("OreChip");
            go.transform.SetParent(GS.FindParent(GS.Parent.loot));
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)));
            chip = go.AddComponent<OreChip>();
            chip.sr = go.AddComponent<SpriteRenderer>();
        }
        if (chip == null) return null;
        chip.sizeClass = sizeClass;
        chip.element = element;
        if (chip.sr == null) chip.sr = chip.GetComponent<SpriteRenderer>();
        if (chip.sr != null)
        {
            chip.sr.sprite = chipSprites[sizeClass * 4 + Random.Range(0, 4)];
            if (element >= 0)
            {
                // ore debris glows HOTTER than the wall it fell out of, and the bigger the chip the
                // hotter: a runtime copy of the ore glow material ("Glow Bright" — the same one the
                // dungeon overlay and Ore_Base wear) per size class with its HDR `thecolor` scaled by
                // oreChipGlowBySize (the era hue is a global, so the copies are never rebuilt).
                int slot = Mathf.Clamp(sizeClass, 0, chipOreMats.Length - 1);
                if (chipOreMats[slot] == null)
                {
                    var src = OreSourceMaterial();
                    if (src != null)
                    {
                        var boosted = new Material(src);
                        if (boosted.HasProperty(ThecolorID))
                            boosted.SetColor(ThecolorID, src.GetColor(ThecolorID) * ChipGlow(slot));
                        chipOreMats[slot] = boosted;
                    }
                }
                if (chipOreMats[slot] != null) chip.sr.material = chipOreMats[slot];
            }
            else if (SpawnManager.instance != null)
            {
                // plain rock reads as base-palette era material (lit — dungeon debris sits
                // under the dungeon light like everything else)
                chip.sr.material = GS.Glow(GlowLevel.Lit);
            }
        }
        chip.Tumble(burstFrom, kick);
        return chip;
    }

    /// <summary>The ore material for an INTENSITY tier (GS.Glow — hue is the global era colour):
    /// tier 0 = Dim ("Glow Dim"), tier 1 = Bright ("Glow Bright"), tier 2 = Super ("Glow Super").
    /// The base's Ore_Base/Low|Mid|High tilemaps wear these; the dungeon
    /// overlay and the chips use tier 1 (intensity shows as vein count there). Null before
    /// SpawnManager exists (callers keep their authored material then).</summary>
    public static Material OreSourceMaterial(int tier = 1)
    {
        if (SpawnManager.instance == null) return null;
        return tier <= 0 ? GS.Glow(GlowLevel.Dim)
             : tier == 1 ? GS.Glow(GlowLevel.Bright)
             : GS.Glow(GlowLevel.Super);
    }

    /// <summary>Dumped scrap at the base (bag-drone haul) — same chip visuals, base-side, so it
    /// survives the return-home despawn.</summary>
    public static OreChip SpawnScrap(Vector3 pos, int sizeClass, int element)
    {
        if (i == null) Ensure();
        // burstFrom == pos → degenerate direction, so each piece tumbles a random way (dump puff)
        return i.SpawnChip(pos, sizeClass, element, pos);
    }

    /// <summary>Scrap with a DIRECTED burst (the player's Hoover spraying its load): the chip
    /// tumbles away from <paramref name="burstFrom"/>. Returns the chip so the caller can put
    /// its own speed on it; null when no chip could be made.</summary>
    public static OreChip SpawnScrap(Vector3 pos, int sizeClass, int element, Vector2 burstFrom, float kick = 1f)
    {
        if (i == null) Ensure();
        return i.SpawnChip(pos, sizeClass, element, burstFrom, kick);
    }

    /// <summary>Sliced sheet from Resources in NUMERIC slice order (a plain name sort puts _10
    /// before _2). Mirrors MineField.LoadStripNumeric; shared by the drone visual components.</summary>
    public static Sprite[] LoadStripNumeric(string res)
    {
        var loaded = Resources.LoadAll<Sprite>(res);
        var list = new System.Collections.Generic.List<(int n, Sprite s)>();
        string prefix = res + "_";
        foreach (var s in loaded)
            if (s.name.StartsWith(prefix) && int.TryParse(s.name.Substring(prefix.Length), out int n))
                list.Add((n, s));
        list.Sort((a, b) => a.n.CompareTo(b.n));
        var arr = new Sprite[list.Count];
        for (int k = 0; k < list.Count; k++) arr[k] = list[k].s;
        return arr;
    }

    /// <summary>Deploy hook for pads that come online MID-DIVE. Dungeon pads are built from the
    /// in-dungeon menu, so a fresh pad's first chance to serve is while the player is already
    /// down there — without this, requested units would only arrive after a full surface-and-
    /// redive. No-op unless the player is in the dungeon.</summary>
    public static void TryDeployNow()
    {
        if (PortalScript.i == null || !PortalScript.i.inDungeon) return;
        DeployAssigned();
    }

    /// <summary>Colony deployment: every base pad's request slots are filled from whatever
    /// matching units exist — nearest first, nobody pre-assigned. Runs on every dive (and when
    /// a dungeon pad comes online mid-dive; already-deployed units are skipped, so it's safe
    /// to run repeatedly).</summary>
    static void DeployAssigned()
    {
        foreach (Building b in Building.buildings)
        {
            if (b is not Telepad basePad || basePad.IsDungeonSide || !basePad.IsOperational) continue;
            Telepad link = basePad.Linked;
            if (link == null || !link.IsOperational) continue;
            FillDroneSlots(basePad, link, DroneEquipment.Drill, basePad.reqDrill);
            FillDroneSlots(basePad, link, DroneEquipment.Bag, basePad.reqBag);
            FillVehicleSlots(basePad, link, basePad.reqAttack);
        }
    }

    static void FillDroneSlots(Telepad basePad, Telepad dungeonPad, DroneEquipment kind, int want)
    {
        // requests already served this dive (TryDeployNow re-runs the whole fill) don't re-send
        for (int k = 0; k < AllyAI.allies.Count; k++)
            if (AllyAI.allies[k] is Drone dd && dd != null && dd.assignedPad == basePad
                && dd.transform.InDungeon() && dd.equipment == kind) want--;
        Vector2 padPos = basePad.transform.position;
        while (want-- > 0)
        {
            // reserved-for-this-pad drones (already waiting at it) board first, then free
            // drones; drones reserved by ANOTHER pad are the last resort
            Drone pick = null;
            int bestTier = int.MaxValue;
            float bestSqr = float.MaxValue;
            for (int k = 0; k < AllyAI.allies.Count; k++)
            {
                if (AllyAI.allies[k] is not Drone d || d == null) continue;
                if (d.transform.InDungeon() || !d.Charged || d.equipment != kind) continue;
                if (d.pilotOf != null || d.state == Drone.State.BoardingVehicle) continue;
                int tier = d.assignedPad == basePad ? 0 : d.assignedPad == null ? 1 : 2;
                float d2 = ((Vector2)d.transform.position - padPos).sqrMagnitude;
                if (tier < bestTier || (tier == bestTier && d2 < bestSqr))
                { bestTier = tier; bestSqr = d2; pick = d; }
            }
            if (pick == null) return;   // fleet exhausted — remaining requests stay standing
            pick.assignedPad = basePad;
            pick.Deploy(dungeonPad);
        }
    }

    static void FillVehicleSlots(Telepad basePad, Telepad dungeonPad, int want)
    {
        for (int k = 0; k < PilotedVehicle.all.Count; k++)
            if (PilotedVehicle.all[k] != null && PilotedVehicle.all[k].deployedVia == basePad
                && PilotedVehicle.all[k].transform.InDungeon()) want--;
        for (int k = 0; k < PilotedVehicle.all.Count && want > 0; k++)
        {
            var v = PilotedVehicle.all[k];
            if (v == null || v.transform.InDungeon() || !v.ReadyForDeploy) continue;
            v.DeployTo(basePad, dungeonPad.RallyPoint);
            want--;
        }
    }

    static void RecallAllFromDungeon()
    {
        for (int k = AllyAI.allies.Count - 1; k >= 0; k--)
        {
            if (AllyAI.allies[k] is not Drone d || d == null) continue;
            if (d.transform.InDungeon()) d.RecallHome();
        }
        for (int k = PilotedVehicle.all.Count - 1; k >= 0; k--)
        {
            var v = PilotedVehicle.all[k];
            if (v != null && v.transform.InDungeon()) v.RecallHome();
        }
    }

    /// <summary>Era change regenerates the whole dungeon — its pads go with it (their tombstoned
    /// slots free up for the next era's pads).</summary>
    void TearDownDungeonPads()
    {
        foreach (Telepad t in TelepadNetwork.DungeonPadsSnapshot())
            if (t != null) Destroy(t.gameObject);
    }
}

/// <summary>One ore intensity tier's chip blend (DroneManager.oreTiers). Weights needn't sum to 1.</summary>
[System.Serializable]
public class OreTierYield
{
    public string name = "Tier";
    [Tooltip("Chips a broken DUNGEON ore block of this tier sheds (x..y inclusive). Base ore drops one per unit.")]
    public Vector2Int chipsPerBlock = new Vector2Int(1, 2);
    [Tooltip("Relative chance of a SMALL chip.")]  public float small = 0.75f;
    [Tooltip("Relative chance of a MEDIUM chip.")] public float medium = 0.25f;
    [Tooltip("Relative chance of a LARGE chip.")]  public float large = 0f;

    public OreTierYield() { }
    public OreTierYield(string n, int minChips, int maxChips, float s, float m, float l)
    { name = n; chipsPerBlock = new Vector2Int(minChips, maxChips); small = s; medium = m; large = l; }

    public static OreTierYield Default(int tier)
        => tier <= 0 ? new OreTierYield("Low", 1, 3, 0.90f, 0.10f, 0f)
         : tier == 1 ? new OreTierYield("Mid", 2, 4, 0.70f, 0.27f, 0.03f)
         : new OreTierYield("High", 3, 5, 0.50f, 0.38f, 0.12f);

    /// <summary>Weighted roll → size class 0/1/2.</summary>
    public int RollSize()
    {
        float s = Mathf.Max(0f, small), m = Mathf.Max(0f, medium), l = Mathf.Max(0f, large);
        float total = s + m + l;
        if (total <= 0f) return 1;
        float r = Random.value * total;
        if (r < s) return 0;
        if (r < s + m) return 1;
        return 2;
    }
}
