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

    [Header("Idle life")]
    [Tooltip("How far an off-duty drone patrols from its dock.")]
    public float idleWanderRadius = 8f;
    [Tooltip("Cruise multiplier while patrolling/chatting (work speed = 1) — idle drones drift, they don't commute.")]
    public float idleSpeedScale = 0.55f;
    [Tooltip("Seconds a drone sits docked before heading back out on patrol (each drone's restlessness skews this). Patrols are endless — this only matters after a recharge or a wave.")]
    public float idleCooldown = 6f;

    [Header("Base ore mining")]
    [Tooltip("Seconds of drill contact to eat one marked ore tile whole (yield is HALF its orb value — drones are half as ore-efficient as buildings).")]
    public float baseOreEatSeconds = 3f;
    [Tooltip("Energy per orb actually released when base-mining.")]
    public float baseOreCostPerOrb = 0.05f;
    [Tooltip("Seconds of click-hold on a base ore tile to toggle its deconstruction mark.")]
    public float oreMarkHoldSeconds = 1f;

    [Header("Energy tariffs")]
    [Tooltip("Energy per 1 hp healed on buildings/vehicles (0.025 => 40 hp per full charge).")]
    public float repairCostPerHp = 0.025f;
    public float drillCostRegular = 0.05f;
    public float drillCostHard = 0.15f;
    public float drillCostVeryHard = 0.3f;

    [Header("Ore chips")]
    [Tooltip("Debris pieces per broken wall (random in range).")]
    public int chipsMinPerBreak = 1;
    public int chipsMaxPerBreak = 3;
    [Tooltip("Hard cap on live chips; oldest are culled first.")]
    public int maxChips = 300;
    [Tooltip("HDR multiplier on the Lit ore materials' `thecolor` for ORE chips — debris from an ore wall glows hotter than the wall overlay itself.")]
    public float oreChipGlow = 2.5f;

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
    public static float RepairCostPerHp => i != null ? i.repairCostPerHp : 0.025f;
    public static float IdleWanderRadius => i != null ? i.idleWanderRadius : 8f;
    public static float IdleSpeedScale => i != null ? i.idleSpeedScale : 0.55f;
    public static float IdleCooldown => i != null ? i.idleCooldown : 6f;
    public static float BaseOreEatSeconds => i != null ? i.baseOreEatSeconds : 3f;
    public static float BaseOreCostPerOrb => i != null ? i.baseOreCostPerOrb : 0.05f;

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
    }

    // ---- shared job-board caches: every idle drone polls the same questions every fixed tick,
    // so answer them once per tick (and once per 0.3s for the rally probe) for the whole fleet ----
    static float jobCacheTime = float.NaN;
    static bool cachedRepairAtBase, cachedRepairAway, cachedBaseMining;
    static float baseZoneHotAt = float.NegativeInfinity;
    static bool baseZoneHotVal;

    static void EnsureJobCaches()
    {
        if (Time.fixedTime == jobCacheTime) return;
        jobCacheTime = Time.fixedTime;
        cachedBaseMining = BaseMiningAllowed();
        cachedRepairAtBase = false;
        cachedRepairAway = false;
        var list = Building.buildings;
        for (int k = 0; k < list.Count; k++)
        {
            Building b = list[k];
            if (b == null || !b.gameObject.activeInHierarchy || !b.NeedsDroneRepair) continue;
            if (PathZone.AtBase(b.transform.position)) cachedRepairAtBase = true;
            else cachedRepairAway = true;
            if (cachedRepairAtBase && cachedRepairAway) break;
        }
    }

    /// <summary>Per-tick cached <see cref="BaseMiningAllowed"/> for job-board polling.</summary>
    public static bool BaseMiningAllowedCached()
    {
        EnsureJobCaches();
        return cachedBaseMining;
    }

    /// <summary>Is there ANY building needing drone repair on this side? Existence only —
    /// RepairSweep still runs its own nearest-target scan once dispatched.</summary>
    public static bool RepairWorkAvailable(bool atBase)
    {
        EnsureJobCaches();
        return atBase ? cachedRepairAtBase : cachedRepairAway;
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

    /// <summary>Wave cleared: vehicles power down, pilots pop out to heal their hulls and recharge.</summary>
    void OnWaveComplete()
    {
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

    static Ore OreAt(Vector2 w)
    {
        for (int t = 0; t < 4 && t < TilemapResource.m.Length; t++)
        {
            var map = TilemapResource.m[t];
            if (map == null) continue;
            var g = map.GetInstantiatedObject(map.WorldToCell(w));
            if (g == null) continue;
            var o = g.GetComponent<Ore>();
            if (o != null && !o.Depleted) return o;
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
        // drones already holding this reservation keep it; surplus (requests shrank) stand down
        for (int k = 0; k < AllyAI.allies.Count; k++)
        {
            if (AllyAI.allies[k] is not Drone d || d == null) continue;
            if (d.assignedPad != basePad || d.transform.InDungeon() || d.equipment != kind) continue;
            if (want > 0) want--;
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
    /// (mid-flight ones included — the frozen dimension must never strand a drone) and clears
    /// the chip debris ("chips last until you return to base").</summary>
    void OnTeleport(bool nowInDungeon)
    {
        if (nowInDungeon)
        {
            DeployAssigned();
        }
        else
        {
            RecallAllFromDungeon();
            DespawnAllChips();
        }
    }

    // ------------------------------------------------------------------ ore chips

    public static float DrillCost(CellType tier)
    {
        var m = i;
        if (tier == CellType.VeryHard) return m != null ? m.drillCostVeryHard : 0.3f;
        if (tier == CellType.Hard) return m != null ? m.drillCostHard : 0.15f;
        return m != null ? m.drillCostRegular : 0.05f;
    }

    Sprite[] chipSprites;
    GameObject chipPrefab;
    readonly Material[] chipOreMats = new Material[4];   // glow-boosted runtime copies, built lazily
    static readonly string[] OreMatNames = { "LitWhite", "LitGreen", "LitBlue", "LitRed" };
    static readonly int ThecolorID = Shader.PropertyToID("thecolor");

    /// <summary>Called from MineField.BreakCell for EVERY broken wall (player or drone). Null-safe
    /// static: quietly no-ops when no manager exists.</summary>
    public static void SpawnChips(Vector3 pos, CellType tier, int oreElement)
    {
        if (i == null) return;
        int size = tier == CellType.VeryHard ? 2 : tier == CellType.Hard ? 1 : 0;
        int n = Random.Range(i.chipsMinPerBreak, i.chipsMaxPerBreak + 1);
        var mf = MineField.i;
        float pad = 0.06f + 0.035f * size;   // sprite half-extent (matches OreChip.WallPad)
        for (int k = 0; k < n; k++)
        {
            // stay inside the cavity: a scatter offset whose padded footprint touches rock
            // snaps back to the freshly-broken cell's centre (open by definition)
            Vector3 p = pos + GS.RandCircle(0.05f, 0.4f);
            if (mf != null && !FitsInCavity(mf, p, pad)) p = pos;
            i.SpawnChip(p, size, oreElement, pos);
        }
    }

    /// <summary>True when a chip-sized square (half-extent <paramref name="pad"/>) around
    /// <paramref name="p"/> touches no solid cell — i.e. the whole SPRITE sits in open cavity.</summary>
    static bool FitsInCavity(MineField mf, Vector2 p, float pad)
    {
        return !mf.IsSolidWorld(new Vector2(p.x - pad, p.y - pad))
            && !mf.IsSolidWorld(new Vector2(p.x + pad, p.y - pad))
            && !mf.IsSolidWorld(new Vector2(p.x - pad, p.y + pad))
            && !mf.IsSolidWorld(new Vector2(p.x + pad, p.y + pad));
    }

    void SpawnChip(Vector3 pos, int sizeClass, int element, Vector2 burstFrom)
    {
        // never cache a failed load — an empty result (asset pipeline mid-refresh) would
        // otherwise poison the whole session
        if (chipSprites == null || chipSprites.Length < 12)
            chipSprites = LoadStripNumeric("OreChips");
        if (chipPrefab == null)
            chipPrefab = Resources.Load<GameObject>("OreChip");
        if (chipSprites.Length < 12) return;

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
        if (chip == null) return;
        chip.sizeClass = sizeClass;
        chip.element = element;
        if (chip.sr == null) chip.sr = chip.GetComponent<SpriteRenderer>();
        if (chip.sr != null)
        {
            chip.sr.sprite = chipSprites[sizeClass * 4 + Random.Range(0, 4)];
            if (element >= 0 && element < 4)
            {
                // ore debris glows HOTTER than the wall overlay it fell out of: a runtime copy
                // of the element's Lit mat with its HDR `thecolor` boosted (the shared asset
                // keeps the calm wall-overlay glow)
                if (chipOreMats[element] == null)
                {
                    var src = Resources.Load<Material>("OreMats/" + OreMatNames[element]);
                    if (src != null)
                    {
                        var boosted = new Material(src);
                        boosted.SetColor(ThecolorID, src.GetColor(ThecolorID) * oreChipGlow);
                        chipOreMats[element] = boosted;
                    }
                }
                if (chipOreMats[element] != null) chip.sr.material = chipOreMats[element];
            }
            else if (SpawnManager.instance != null)
            {
                // plain rock reads as base-palette era material (lit — dungeon debris sits
                // under the dungeon light like everything else)
                chip.sr.material = GS.MatByEra(GS.era, lit: true);
            }
        }
        chip.Tumble(burstFrom);
    }

    /// <summary>Dumped scrap at the base (bag-drone haul) — same chip visuals, base-side, so it
    /// survives the return-home despawn.</summary>
    public static void SpawnScrap(Vector3 pos, int sizeClass, int element)
    {
        if (i == null) Ensure();
        // burstFrom == pos → degenerate direction, so each piece tumbles a random way (dump puff)
        i.SpawnChip(pos, sizeClass, element, pos);
    }

    static void DespawnAllChips()
    {
        // dungeon debris only — the scrap pile hauled home stays
        for (int k = OreChip.all.Count - 1; k >= 0; k--)
        {
            var chip = OreChip.all[k];
            if (chip != null && chip.transform.InDungeon()) Destroy(chip.gameObject);
        }
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
