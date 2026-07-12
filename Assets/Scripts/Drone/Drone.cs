using System.Collections;
using UnityEngine;

/// <summary>
/// Worker drone. Lives at a DroneDock (its resident home), carries one charge of energy per
/// day and spends it on its job: repairing buildings (no equipment), drilling ore (drill),
/// hauling loot (bag) or crewing a vehicle (pilot).
///
/// COLONY MODEL: drones are never clicked or ordered directly. The player expresses intent in
/// the world — forge kit at a workshop, mark ore for deconstruction (click-hold), fill telepad
/// request slots — and spare drones take the jobs themselves (TryDispatchWork is the job board).
///
/// Aggro rule: a drone only reads as an enemy target while it is HOLDING something —
/// dungeon-side that is opt-in via MinePathManager.RegisterAllyTarget, base-side it is
/// opt-out via BasePathManager.UntargetableAllies (the base seeds every allies-parent child).
/// Empty drones take evasive action instead: run to the player (dungeon) or (0,0) (base).
///
/// NOTE ON INHERITANCE: AllyAI's rally/group behaviour is deliberately neutralised — the
/// Update override skips AllyAI's NewTarget timer (home == null would NRE), OnClick does not
/// group-join, and OnDeath is re-implemented (AllyAI's dereferences home).
/// </summary>
public class Drone : AllyAI, IOnDeath
{
    public enum State
    {
        Docked, ReturningToDock, Evading,
        RepairSweep, TravelToPad, DeployedTravel, Drilling, Collecting, Fleeing, RallyFight,
        ShuttleHome, DumpLoot, TravelToStation, WaitingAtStation, BoardingVehicle, HealVehicle,
        Loitering, Chatting, MineBaseOre, Playing, FetchKit, PadPause,
    }

    [Header("Drone")]
    [Tooltip("Movement force per physics tick (scaled by DroneManager haste and actRate).")]
    public float moveForce = 2f;
    [Tooltip("Whole-drone animation frames (body/prop), cycled while powered.")]
    public Sprite[] frames;
    public float frameRate = 10f;

    [Tooltip("Repair speed in hp/second at haste 1 (energy billed per hp actually applied).")]
    public float repairRate = 5f;

    [HideInInspector] public State state = State.Docked;
    [HideInInspector] public DroneDock dock;
    [HideInInspector] public int dockSlot;
    [HideInInspector] public DroneEquipment equipment = DroneEquipment.None;
    [Tooltip("The BASE-side telepad this drone is deployed through — set by the dive-time slot fill, cleared on recall.")]
    [HideInInspector] public Telepad assignedPad;
    [HideInInspector] public PilotedVehicle pilotOf;
    Vector2 padPauseSpot;   // landing spot held during the post-return breather
    float padPauseT;
    FactoryPilotStation waitingStation;
    EquipmentWorkshop waitingWorkshop;
    // kit-swap errand: fly to the OLD kit's home workshop, hand it back, then continue to the
    // new kit's workshop — swaps never strand equipment on the ground
    EquipmentWorkshop pendingWorkshop;
    bool returningKit;

    [Header("Drilling")]
    [Tooltip("How far (in cells) a drill drone scans for its next wall.")]
    public int drillScanRadius = 8;

    Building repairTarget;
    float repairTickTimer;
    [HideInInspector] public DroneDrillBit drillBit;
    [HideInInspector] public SackWobble sack;

    // ---- cargo (bag drones) ----
    struct CargoEntry
    {
        public int kind;        // 0 chip, 1 orb, 2 payload item (battery / drone equipment)
        public int space;
        public int sizeClass;   // chips only
        public int element;     // chips (-1 plain) / orbs (0..3)
        public GameObject payload;
    }
    readonly System.Collections.Generic.List<CargoEntry> cargo = new System.Collections.Generic.List<CargoEntry>();
    int cargoSpaceUsed;
    OreChip chipTarget;
    OrbScript orbTarget;
    Battery batteryTarget;
    DroneEquipmentItem equipmentTarget;

    // per-drone excavation heading: seeded outward through the pad on first deploy, then only
    // nudged a few degrees per broken tile — lines stay roughly straight instead of scribbly
    float headingDeg;
    bool headingSeeded;
    /// <summary>Deploy-order spoke dealer — spreads drill headings across the map.</summary>
    static int spokeCounter;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSpokeCounter() => spokeCounter = 0;   // no-domain-reload: statics survive play-stop
    bool hasDrillTarget;
    Vector3Int drillTarget;
    Vector2 drillApproach;
    // approach watchdog: FindDrillTarget only proves a face is EXPOSED, not REACHABLE — a wall
    // seen across an unconnected cavity would otherwise be shoved at forever
    float drillBestDist;
    float drillStallTimer;
    float noTargetTimer;
    readonly System.Collections.Generic.List<(Vector3Int cell, float until)> drillBlacklist
        = new System.Collections.Generic.List<(Vector3Int cell, float until)>();
    float rallyBuffTimer;
    bool rallyBuffed;

    /// <summary>actRate for visual pacing (drill spin etc.) — floor keeps animations alive under slows.</summary>
    public float ActRateVisible => Mathf.Max(0.2f, actRate);

    /// <summary>0..1: one full charge is one day's work. Refilled only by the dock.</summary>
    [HideInInspector] public float energy = 1f;

    int cargoUnits;
    public int CargoUnits => cargoUnits;
    public bool HasCargo => cargoUnits > 0;

    float threatTimer;
    bool threatened;
    bool baseZoneHot;   // enemies within RallyLeash of the (0,0) base rally point (0.3s cadence)
    float repathTimer;
    Vector2 pathDir;
    bool pathValid;
    float frameTimer;
    int frameIndex;

    // ---- idle personality ----
    // Two dials rolled once per drone — how soon it gets bored at the dock, and how much it
    // seeks company. Stroll length and emote frequency lean on the same two numbers so each
    // drone reads as one consistent character rather than uniform noise.
    float restless, chatty;
    float idleTimer;
    float sleepMumbleT;
    Vector2 loiterPoint;
    float loiterPauseT;
    bool loiterSightseeing;
    [HideInInspector] public Drone chatPartner;
    bool chatInitiator;
    bool chatQuick;               // passing "hi" (5-10s) vs a proper seek-out natter
    Vector2 chatSpot;
    float chatBeat, chatEndT;
    int chatBeatCount;
    float greetReadyT;            // Time.time gate so the same pair doesn't re-hi forever
    float hiScanT;
    // cards: the host holds the table (players list, centre, turn clock); members only hold
    // a host ref + their seat angle, so any one drone dropping out never strands the rest
    Drone cardHost;
    System.Collections.Generic.List<Drone> cardPlayers;
    Vector2 cardCenter;
    float cardSeatAng;
    float cardEndT, cardBeatT;
    int cardTurn;
    DroneSpeechBubble bubble;

    // ---- self-assigned jobs (colony model) ----
    Ore oreTarget;                 // marked tile this drone is deconstructing
    float oreTickT;
    float orbScanT;
    DroneEquipmentItem fetchItem;  // ground kit this drone claimed

    public bool Charged => energy > 1e-3f;

    protected override void Start()
    {
        base.Start();
        // Neutralise AllyAI's rally wander: follow-mode never dereferences home, stopSkr blocks
        // the teleporting SkrSkr catch-up, and the huge resetTimer stops NewTarget re-runs.
        mode = Mode.follow;
        stopSkr = true;
        resetTimer = float.MaxValue;
        BasePathManager.UntargetableAllies.Add(transform);   // empty-handed = invisible to enemies
        // Speed lives on DroneManager — the prefab-serialized moveForce/maxVelocity are stale.
        moveForce = DroneManager.DroneMoveForce;
        if (AS != null) AS.maxVelocity = DroneManager.DroneMaxVelocity;
        // personality seeds + staggered first stroll so a fresh dock doesn't move in lockstep
        restless = Random.value;
        chatty = Random.value;
        idleTimer = NextIdleDelay() * Random.Range(0.3f, 1f);
        sleepMumbleT = Random.Range(8f, 20f);
        drillBit = GetComponentInChildren<DroneDrillBit>(true);
        sack = GetComponentInChildren<SackWobble>(true);
        // Capacity lives on DroneManager (derived from the drill economy) — the prefab's
        // serialized maxSpace is stale, same deal as moveForce/maxVelocity above.
        if (sack != null) sack.maxSpace = DroneManager.BagCapacity;
        SetEquipment(equipment);   // sync child visuals with whatever was authored/serialized
        RunPersistent(Brain);
    }

    /// <summary>Swap the drone's kit. Physical kit being replaced (drill/bag) drops as a world
    /// item where the drone stands — recoverable, haulable — unless the caller already banked
    /// it (workshop return: <paramref name="dropReplaced"/> false). Child visuals follow.</summary>
    public virtual void SetEquipment(DroneEquipment kind, bool dropReplaced = true)
    {
        if (kind != equipment && (equipment == DroneEquipment.Drill || equipment == DroneEquipment.Bag))
        {
            if (equipment == DroneEquipment.Bag && HasCargo)
                DumpCargoAt(transform.position);   // cargo can't exist without the bag holding it
            if (dropReplaced)
                DroneEquipmentItem.Spawn(equipment, transform.position + GS.RandCircle(0.2f, 0.5f));
        }
        equipment = kind;
        if (kind != DroneEquipment.Drill) ReleaseOre();   // mining needs the drill
        if (drillBit != null) drillBit.gameObject.SetActive(kind == DroneEquipment.Drill);
        if (sack != null) sack.gameObject.SetActive(kind == DroneEquipment.Bag);
    }

    /// <summary>A workshop hands over fresh kit; the drone takes the next job on the board.</summary>
    public void TakeEquipment(DroneEquipment kind)
    {
        waitingWorkshop = null;
        SetEquipment(kind);
        if (!TryDispatchWork()) GoLoiter();
    }

    /// <summary>Fly to a workshop and queue for kit — it's handed over on arrival.</summary>
    public void WaitAt(EquipmentWorkshop workshop)
    {
        waitingWorkshop = workshop;
        returningKit = false;   // this is always the COLLECT leg (TryClaim entry)
        state = State.WaitingAtStation;
    }

    /// <summary>Arrived at the OLD kit's home workshop: bank the kit (cargo spills here first —
    /// it can't exist without the bag), then continue the errand to the new kit's workshop.</summary>
    void FinishKitReturn()
    {
        var home = waitingWorkshop;
        returningKit = false;
        waitingWorkshop = null;
        if (home != null && home.produces == equipment &&
            (equipment == DroneEquipment.Drill || equipment == DroneEquipment.Bag))
        {
            home.Restock();
            SetEquipment(DroneEquipment.None, dropReplaced: false);
        }
        var next = pendingWorkshop;
        pendingWorkshop = null;
        if (next != null) next.TryClaim(this);
        else if (!TryDispatchWork()) GoLoiter();
    }

    /// <summary>Detach from whatever job/station held this drone. Any queued kit-swap errand is
    /// forgotten — the newest situation always wins.</summary>
    protected void LeaveCurrentRole(bool keepPad = false)
    {
        BreakChat();
        LeaveCards();
        ReleaseFetch();
        if (waitingStation != null) { waitingStation.LeaveQueue(this); waitingStation = null; }
        if (waitingWorkshop != null) { waitingWorkshop.LeaveQueue(this); waitingWorkshop = null; }
        pendingWorkshop = null;
        returningKit = false;
        if (pilotOf != null) pilotOf.RemovePilot(this);
        if (!keepPad) assignedPad = null;
        if (!keepPad) ReleaseOre();
        if (equipment == DroneEquipment.Pilot) SetEquipment(DroneEquipment.None);
    }

    /// <summary>Queue at a station (factory with no free seat / workshop with no stock).</summary>
    public void WaitAt(FactoryPilotStation station)
    {
        waitingStation = station;
        state = State.WaitingAtStation;
    }

    protected override void Update()
    {
        // Deliberately NOT calling base.Update(): AllyAI's timer would call NewTarget (home is
        // null). Unit-level bookkeeping we still want is done directly.
        CarryOutShieldDisplay();
        PlaceStats();
        AnimateFrames();
    }

    /// <summary>The drone's work side (drill) hangs at local -Y, so facing something means
    /// pointing the underside at it; the bag on its back trails behind naturally. The turn is
    /// rate-capped (DroneManager.DroneTurnSpeed) so the body sweeps around instead of snapping.</summary>
    void FaceDir(Vector2 dir)
    {
        if (dir.sqrMagnitude < 1e-4f) return;
        Quaternion target = Quaternion.LookRotation(Vector3.forward, -dir);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, target,
            DroneManager.DroneTurnSpeed * Time.deltaTime);
    }

    void AnimateFrames()
    {
        if (frames == null || frames.Length == 0 || sr == null) return;
        if (!Charged)
        {
            sr.sprite = frames[0];
            return;
        }
        frameTimer += Time.deltaTime * frameRate * Mathf.Max(0.2f, actRate);
        if (frameTimer >= 1f)
        {
            frameTimer -= 1f;
            frameIndex = (frameIndex + 1) % frames.Length;
            sr.sprite = frames[frameIndex];
        }
    }

    // ------------------------------------------------------------------ cargo / aggro

    /// <summary>Single choke point for the aggro rule: holding anything makes the drone a valid
    /// enemy target in both dimensions; empty makes it invisible again.</summary>
    public void SetCargoUnits(int n)
    {
        if (n > 0 && cargoUnits == 0)
        {
            BasePathManager.UntargetableAllies.Remove(transform);
            if (transform.InDungeon()) MinePathManager.RegisterAllyTarget(transform, 2);
        }
        else if (n <= 0 && cargoUnits > 0)
        {
            BasePathManager.UntargetableAllies.Add(transform);
            MinePathManager.UnregisterAllyTarget(transform);
        }
        cargoUnits = Mathf.Max(0, n);
    }

    // ------------------------------------------------------------------ energy

    public void Recharge() => energy = 1f;

    public bool TrySpend(float amount)
    {
        if (energy < amount) return false;
        energy -= amount;
        return true;
    }

    // ------------------------------------------------------------------ brain

    IEnumerator Brain()
    {
        var wait = new WaitForFixedUpdate();
        while (true)
        {
            if (ls != null && ls.hasDied) yield break;
            UpdateThreat();
            // Buff cooldown ticks in every state — a fight left early must still spend the
            // window before the next engagement can stim/shield again.
            if (rallyBuffed && (rallyBuffTimer -= Time.fixedDeltaTime) <= 0f) rallyBuffed = false;
            switch (state)
            {
                case State.Docked:
                    if (threatened && !HasCargo) { state = State.Evading; break; }
                    // holding a pad reservation (e.g. back from an evade): return to the pad
                    if (assignedPad != null && Charged) { state = State.TravelToPad; break; }
                    if (TryDispatchWork()) break;
                    if (Charged && Peaceful() && !threatened)
                    {
                        // off duty and safe: back out on patrol shortly — the dock is a charger,
                        // not a home; drones only sit here flat or sheltering from a wave
                        idleTimer -= Time.fixedDeltaTime;
                        if (idleTimer <= 0f) { StartIdleJaunt(); break; }
                    }
                    else if (!Charged && (sleepMumbleT -= Time.fixedDeltaTime) <= 0f)
                    {
                        // flat battery: dead to the world, save the odd mumble
                        sleepMumbleT = Random.Range(10f, 25f);
                        Emote(DroneEmote.Sleepy, 2f);
                    }
                    HoldAt(DockPoint(), 0.25f);
                    break;

                case State.RepairSweep:
                    TickRepairSweep();
                    break;

                case State.TravelToPad:
                    // Wait at the pad for the player to dive (deployment rides the teleport).
                    if (threatened && !HasCargo) { state = State.Evading; break; }
                    // flat battery gives up the slot — the reservation tick refills it with a
                    // charged drone while this one recharges at the dock
                    if (!Charged) { assignedPad = null; state = State.ReturningToDock; break; }
                    if (assignedPad == null) { state = State.ReturningToDock; break; }
                    if (!MoveToward(assignedPad.transform.position, 0.5f)) break;
                    HoldAt(assignedPad.transform.position, 0.5f);
                    break;

                case State.DeployedTravel:
                    // Dungeon-side idle/travel hub; drill and bag work dispatch from here.
                    if (threatened) { state = State.Fleeing; break; }
                    // CanAffordDrill (not Charged): a drone whose dregs can't buy one regular
                    // wall must NOT bounce Drilling<->here every tick — that reads as frozen.
                    if (equipment == DroneEquipment.Drill && CanAffordDrill) { state = State.Drilling; break; }
                    // EffectiveSpaceLeft (not Charged): dregs that can't buy one space unit must
                    // not bounce Collecting<->here every tick either.
                    if (equipment == DroneEquipment.Bag && EffectiveSpaceLeft > 0) { state = State.Collecting; break; }
                    // Off-duty (flat battery included): PATH back to the pad — HoldAt's straight
                    // shove would pin the drone against the first wall between here and there.
                    MoveToward(PadRally(), 0.6f);
                    break;

                case State.Collecting:
                    TickCollecting();
                    break;

                case State.ShuttleHome:
                    TickShuttleHome();
                    break;

                case State.DumpLoot:
                    TickDumpLoot();
                    break;

                case State.Drilling:
                    TickDrilling();
                    break;

                case State.Fleeing:
                    bool atRally = MoveToward(RallySpot(), 0.55f);
                    if (atRally && equipment == DroneEquipment.Drill && threatened && Charged)
                    {
                        // touched the rally point under threat: turn and fight — the stim/shield
                        // only pops on actual engagement (TickRallyFight), not here. Works in
                        // both dimensions: base-side the rally point is the refuge at (0,0).
                        state = State.RallyFight;
                        break;
                    }
                    if (!threatened)
                        state = transform.InDungeon() ? State.DeployedTravel
                            : assignedPad != null ? State.TravelToPad : State.ReturningToDock;
                    break;

                case State.RallyFight:
                    TickRallyFight();
                    break;

                case State.WaitingAtStation:
                    if (threatened && !HasCargo) { state = State.Evading; break; }
                    if (waitingStation != null) HoldAt(waitingStation.WaitPoint, 0.8f);
                    else if (waitingWorkshop != null)
                    {
                        // Kit is handled in person: path to the workshop, then either hand the
                        // old kit back (swap's return leg) or keep asking for stock (covers
                        // queuing while the forge works).
                        if (MoveToward(waitingWorkshop.WaitPoint, 0.8f))
                        {
                            if (returningKit) FinishKitReturn();
                            else waitingWorkshop.TryHandOver(this);
                        }
                    }
                    else if (returningKit && pendingWorkshop != null)
                    {
                        // return-leg workshop died mid-flight — skip straight to collecting
                        // (the old kit falls at the drone's feet on handover, the old fallback)
                        returningKit = false;
                        var next = pendingWorkshop;
                        pendingWorkshop = null;
                        next.TryClaim(this);
                    }
                    else
                    {
                        returningKit = false;   // whole errand died — forget it cleanly
                        pendingWorkshop = null;
                        state = State.ReturningToDock;
                    }
                    break;

                case State.BoardingVehicle:
                    if (pilotOf == null)
                    {
                        SetEquipment(DroneEquipment.None);
                        state = State.ReturningToDock;
                        break;
                    }
                    if (!Charged) { HoldAt(DockPoint(), 0.5f); break; }   // flat pilot can't crew
                    if (MoveToward(pilotOf.transform.position, 0.45f)) pilotOf.Board(this);
                    break;

                case State.HealVehicle:
                    TickHealVehicle();
                    break;

                case State.Evading:
                    // Survival overrides the energy gate — an uncharged drone still flees.
                    bool atRefuge = MoveToward(RefugePoint());
                    if (atRefuge && equipment == DroneEquipment.Drill && threatened && Charged)
                    {
                        // drill drones don't cower at the refuge: same turn-and-fight as the
                        // dungeon rally, with (0,0) as the rally point base-side
                        state = State.RallyFight;
                        break;
                    }
                    if (!threatened)
                        state = assignedPad != null ? State.TravelToPad : State.ReturningToDock;
                    break;

                case State.ReturningToDock:
                    if (threatened && !HasCargo) { state = State.Evading; break; }
                    if (MoveToward(DockPoint(), 0.35f)) state = State.Docked;
                    break;

                case State.Loitering:
                    TickLoiter();
                    break;

                case State.Chatting:
                    TickChat();
                    break;

                case State.Playing:
                    TickPlaying();
                    break;

                case State.MineBaseOre:
                    TickMineBaseOre();
                    break;

                case State.FetchKit:
                    TickFetchKit();
                    break;

                case State.PadPause:
                    // Post-return breather on the landing pad; a threat cuts it short.
                    if (threatened && !HasCargo) { state = State.Evading; break; }
                    if ((padPauseT -= Time.fixedDeltaTime) <= 0f)
                    {
                        state = HasCargo ? State.DumpLoot : State.ReturningToDock;
                        break;
                    }
                    HoldAt(padPauseSpot, 0.35f);
                    break;

                default:
                    // States wired up by later systems fall back to going home safely.
                    state = State.ReturningToDock;
                    break;
            }
            yield return wait;
        }
    }

    Vector2 DockPoint() => dock != null ? dock.SlotPosition(dockSlot) : (Vector2)transform.position;

    Vector2 RefugePoint()
    {
        if (transform.InDungeon()) return GS.CS().position;
        // Bag drones EVACUATE a hot rally zone rather than joining it: hold a radial
        // standoff just outside the arena while the drill drones fight at (0,0).
        if (equipment == DroneEquipment.Bag && baseZoneHot)
        {
            Vector2 pos = transform.position;
            Vector2 dir = pos.sqrMagnitude > 0.04f ? pos.normalized : (Vector2)(-transform.up);
            return dir * (DroneManager.BagRallyStandoff + 0.75f);
        }
        return Vector2.zero;
    }

    /// <summary>Nearest safe spot for a working drone: its rally pad (the dungeon-side link) or
    /// the player, whichever is closer. Base-side falls back to the evasion refuge.</summary>
    protected Vector2 RallySpot()
    {
        if (!transform.InDungeon()) return RefugePoint();
        Vector2 player = GS.CS().position;
        Telepad pad = assignedPad != null ? (assignedPad.IsDungeonSide ? assignedPad : assignedPad.Linked) : null;
        if (pad == null || !pad.IsOperational) return player;
        Vector2 padPos = pad.RallyPoint;
        Vector2 pos = transform.position;
        return (padPos - pos).sqrMagnitude <= (player - pos).sqrMagnitude ? padPos : player;
    }

    /// <summary>Where an off-duty deployed drone waits: its dungeon-side pad (the telepad IS the
    /// rally point), falling back to RallySpot when the link is down.</summary>
    Vector2 PadRally()
    {
        Telepad pad = assignedPad != null ? (assignedPad.IsDungeonSide ? assignedPad : assignedPad.Linked) : null;
        return pad != null && pad.IsOperational ? pad.RallyPoint : RallySpot();
    }

    /// <summary>Enough energy left to buy at least one regular-tier wall.</summary>
    bool CanAffordDrill => energy >= DroneManager.DrillCost(CellType.Regular);

    // ------------------------------------------------------------------ deployment

    /// <summary>Standing telepad reservation: drop whatever else, fly to the base pad and wait
    /// there for the dive (deployment itself rides the player's teleport). Set by DroneManager's
    /// reservation tick so requested drones are already gathered at their pad.</summary>
    public void AssignToPad(Telepad basePad)
    {
        LeaveCurrentRole();
        assignedPad = basePad;
        state = State.TravelToPad;
    }

    /// <summary>Requests shrank (or cleared): a base-side drone holding a reservation stands
    /// down. Deployed drones keep their pad binding — recall clears it.</summary>
    public void ReleasePadReservation()
    {
        if (transform.InDungeon()) return;
        assignedPad = null;
        if (state == State.TravelToPad) state = State.ReturningToDock;
    }

    /// <summary>Teleport through the link into the dungeon (rides the player's dive).</summary>
    public void Deploy(Telepad dungeonPad)
    {
        if (dungeonPad == null) return;
        BreakChat();   // yanked mid-gossip/mid-game — release the others cleanly
        LeaveCards();
        if (!headingSeeded)
        {
            // Excavation heading: each drone takes its own spoke so the squad spreads over the
            // map instead of clumping on the pad's outward line. Pads away from the centre fan
            // around their outward direction (0°, ±70°, ±140°); central pads use golden-angle
            // spokes for even all-round coverage.
            headingSeeded = true;
            Vector2 outward = dungeonPad.transform.position;
            int k = spokeCounter++;
            if (outward.sqrMagnitude > 1f)
            {
                float fan = ((k + 1) / 2) * 70f * (k % 2 == 0 ? 1f : -1f);
                headingDeg = Mathf.Atan2(outward.y, outward.x) * Mathf.Rad2Deg + fan + Random.Range(-15f, 15f);
            }
            else
            {
                headingDeg = k * 137.508f + Random.Range(-15f, 15f);
            }
        }
        transform.position = dungeonPad.transform.position + GS.RandCircle(0.2f, 0.6f);
        if (AS != null && AS.rb != null) AS.rb.linearVelocity = Vector2.zero;
        // Bag drones obey the ore like any dungeon body (collision is code-based — kinematic
        // bodies ignore physics colliders, so only registered rbs get depenetrated). Drill
        // drones stay unregistered: pressing INTO the wall face is how they grind.
        if (equipment == DroneEquipment.Bag && MineField.i != null && AS != null && AS.rb != null)
            MineField.i.Register(AS.rb);
        inDungeon = true;
        repairTarget = null;
        threatened = false;
        if (HasCargo) MinePathManager.RegisterAllyTarget(transform, 2);   // dimension changed — re-register
        state = State.DeployedTravel;
    }

    /// <summary>Forced ride home when the player leaves the dungeon (or a full bag shuttles back).
    /// Lands at the base-side pad, falling back to the dock, then (0,0).</summary>
    public void RecallHome()
    {
        Vector2 landing;
        Telepad basePad = assignedPad != null && !assignedPad.IsDungeonSide ? assignedPad : assignedPad?.Linked;
        if (basePad != null) landing = basePad.transform.position;
        else if (dock != null) landing = dock.SlotPosition(dockSlot);
        else landing = Vector2.zero;
        BreakChat();
        LeaveCards();
        assignedPad = null;   // deployment binding ends at recall — the next dive re-fills slots
        transform.position = (Vector3)landing + GS.RandCircle(0.2f, 0.5f);
        if (AS != null && AS.rb != null) AS.rb.linearVelocity = Vector2.zero;
        if (MineField.i != null && AS != null && AS.rb != null) MineField.i.Unregister(AS.rb);
        inDungeon = false;
        repairTarget = null;
        ReleaseCollectTarget();   // dungeon claims don't follow home
        hasDrillTarget = false;
        threatened = false;
        MinePathManager.UnregisterAllyTarget(transform);   // dungeon aggro seat doesn't follow home
        // Fresh off the pad: hold on the landing spot a beat before the next errand, so
        // returns read as an arrival rather than an instant scatter.
        padPauseSpot = transform.position;
        padPauseT = 3f;
        state = State.PadPause;
    }

    /// <summary>Repairs are round's-end work only: after a wave clears or on a quiet day.</summary>
    static bool Peaceful()
    {
        var sm = SpawnManager.instance;
        if (sm == null) return true;
        return !SpawnManager.eeactive && (sm.waveCompleted || sm.dayState == SpawnManager.DayState.Day);
    }

    // ------------------------------------------------------------------ repair duty

    Building FindRepairTarget()
    {
        Building best = null;
        float bestSqr = float.MaxValue;
        Vector2 pos = transform.position;
        var list = Building.buildings;
        for (int k = 0; k < list.Count; k++)
        {
            Building b = list[k];
            if (b == null || !b.gameObject.activeInHierarchy || !b.NeedsDroneRepair) continue;
            if (PathZone.AtBase(pos) != PathZone.AtBase(b.transform.position)) continue;   // same dimension only
            float d = ((Vector2)b.transform.position - pos).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; best = b; }
        }
        return best;
    }

    /// <summary>Between waves a pilot patches its own hull (same tariff as building repair),
    /// then goes home to recharge. Re-boards the moment a wave threatens.</summary>
    void TickHealVehicle()
    {
        if (pilotOf == null)
        {
            SetEquipment(DroneEquipment.None);
            state = State.ReturningToDock;
            return;
        }
        if (SpawnManager.eeactive || (SpawnManager.instance != null && SpawnManager.instance.dayState == SpawnManager.DayState.PreAttack))
        {
            state = State.BoardingVehicle;
            return;
        }
        var vls = pilotOf.Ls;
        bool fullyHealed = vls == null || vls.hasDied || vls.hp >= vls.maxHp - 0.01f;
        if (fullyHealed || !Charged) { state = State.ReturningToDock; return; }
        if (!MoveToward(pilotOf.transform.position, 0.7f)) return;

        repairTickTimer -= Time.fixedDeltaTime;
        if (repairTickTimer > 0f) return;
        float tick = 0.25f;
        repairTickTimer = tick;
        float costPerHp = DroneManager.RepairCostPerHp;
        float hpBudget = repairRate * DroneManager.Haste * ActRateVisible * tick;
        if (costPerHp > 0f) hpBudget = Mathf.Min(hpBudget, energy / costPerHp);
        float applied = Mathf.Min(hpBudget, vls.maxHp - vls.hp);
        if (applied <= 0f) return;
        vls.Change(applied, -1, false);
        energy = Mathf.Max(0f, energy - applied * costPerHp);
    }

    void TickRepairSweep()
    {
        if (threatened && !HasCargo) { state = State.Evading; repairTarget = null; return; }
        if (!Peaceful() || !Charged) { state = State.ReturningToDock; repairTarget = null; return; }
        if (repairTarget == null || !repairTarget.NeedsDroneRepair)
        {
            repairTarget = FindRepairTarget();
            if (repairTarget == null) { GoLoiter(); return; }   // all patched — back on patrol
        }
        if (!MoveToward(repairTarget.transform.position, 0.7f)) return;

        // Channel in coarse ticks so heal FX/numbers don't spam every physics frame.
        repairTickTimer -= Time.fixedDeltaTime;
        if (repairTickTimer > 0f) return;
        float tick = 0.25f;
        repairTickTimer = tick;
        float costPerHp = DroneManager.RepairCostPerHp;
        float hpBudget = repairRate * DroneManager.Haste * Mathf.Max(0.2f, actRate) * tick;
        if (costPerHp > 0f) hpBudget = Mathf.Min(hpBudget, energy / costPerHp);
        if (hpBudget <= 0f) { state = State.ReturningToDock; return; }
        float applied = repairTarget.RepairTick(hpBudget);
        energy = Mathf.Max(0f, energy - applied * costPerHp);
    }

    void UpdateThreat()
    {
        threatTimer -= Time.fixedDeltaTime;
        if (threatTimer > 0f) return;
        threatTimer = 0.3f;
        bool was = threatened;
        float radius = threatened ? DroneManager.ThreatClearRadius : DroneManager.ThreatRadius;
        var foes = GS.FindEnemies(tag, transform.position, radius, false, false);
        if (!transform.InDungeon())
        {
            // (0,0) is the base RALLY POINT — is anything pressing it right now?
            baseZoneHot = DroneManager.BaseZoneHot(tag); //shared fleet-wide probe, 0.3s TTL
            threatened = foes.Count > 0;
            if (!threatened && baseZoneHot)
            {
                // A charged drill drone treats a hot rally zone as its own threat, so it
                // rallies in and fights (Evading carries it to the refuge, then turn-and-
                // fight) instead of only reacting to enemies near itself. A bag drone does
                // the OPPOSITE: caught inside the arena, it runs out to the standoff ring
                // (see RefugePoint) and leaves the fighting to the drills.
                if (equipment == DroneEquipment.Drill && Charged)
                    threatened = true;
                else if (equipment == DroneEquipment.Bag &&
                         ((Vector2)transform.position).sqrMagnitude
                             < DroneManager.BagRallyStandoff * DroneManager.BagRallyStandoff)
                    threatened = true;
            }
        }
        else
        {
            // Dungeon: rock blocks threat. FindEnemies is a bare radius query — an enemy 4u away
            // THROUGH A WALL would lock the drone into flee/rally-fight forever (it can never be
            // reached, so 'threatened' never clears and no wall ever gets drilled again).
            threatened = false;
            for (int k = 0; k < foes.Count; k++)
            {
                if (foes[k] == null) continue;
                if (ThreatVisible(transform.position, foes[k].position)) { threatened = true; break; }
            }
        }
        if (threatened && !was) Emote(DroneEmote.Startled, 1f);
    }

    /// <summary>Straight rock-free line between two dungeon points? Sub-cell sampling against the
    /// mine grid — cheap, and honest enough for threat checks (enemies can't hit through walls).</summary>
    static bool ThreatVisible(Vector2 from, Vector2 to)
    {
        var mf = MineField.i;
        if (mf == null) return true;
        Vector2 d = to - from;
        float len = d.magnitude;
        if (len < 1e-3f) return true;
        int steps = Mathf.CeilToInt(len / (mf.cellSize * 0.45f));   // < half a cell per step
        Vector2 stepV = d / steps;
        Vector2 p = from;
        for (int k = 1; k < steps; k++)
        {
            p += stepV;
            if (mf.IsSolid(mf.WorldToCell(p))) return false;
        }
        return true;
    }

    // ------------------------------------------------------------------ drilling

    Vector2 HeadingDir() => new Vector2(Mathf.Cos(headingDeg * Mathf.Deg2Rad), Mathf.Sin(headingDeg * Mathf.Deg2Rad));

    static readonly Vector3Int[] FaceOffsets =
    {
        new Vector3Int(1, 0, 0), new Vector3Int(-1, 0, 0), new Vector3Int(0, 1, 0), new Vector3Int(0, -1, 0),
    };

    void TickDrilling()
    {
        if (threatened) { StopDrillVisual(); state = State.Fleeing; return; }
        var mf = MineField.i;
        if (mf == null || !transform.InDungeon()) { StopDrillVisual(); state = State.DeployedTravel; return; }
        if (!CanAffordDrill)
        {
            StopDrillVisual();
            state = State.DeployedTravel;   // flat — heads back to the pad until recall/recharge
            return;
        }

        if (!hasDrillTarget || !mf.DroneMineable(drillTarget))
        {
            StopDrillVisual();
            if (!FindDrillTarget(mf))
            {
                // nothing minable in scan range — push outward along the heading and rescan;
                // if this heading has been dry for a while it's a dead end (open cavern /
                // unreachable rock): swing to a fresh spoke instead of nosing the same wall
                noTargetTimer += Time.fixedDeltaTime;
                if (noTargetTimer > 4f)
                {
                    noTargetTimer = 0f;
                    headingDeg += Random.Range(90f, 270f);
                }
                MoveToward((Vector2)transform.position + HeadingDir() * 3f, 0.4f);
                return;
            }
            noTargetTimer = 0f;
            drillBestDist = float.MaxValue;
            drillStallTimer = 0f;
        }

        Vector2 face = mf.CellCenterWorld(drillTarget);
        float dist = Vector2.Distance(transform.position, face);
        if (dist > 0.85f)
        {
            StopDrillVisual();
            // watchdog: exposed ≠ reachable — no closing progress for a while means the face
            // sits across a gap/unconnected cavity; ban it briefly and pick something else
            if (dist < drillBestDist - 0.05f)
            {
                drillBestDist = dist;
                drillStallTimer = 0f;
            }
            else if ((drillStallTimer += Time.fixedDeltaTime) > 3f)
            {
                drillBlacklist.Add((drillTarget, Time.time + 20f));
                hasDrillTarget = false;
                return;
            }
            // route to the exposed neighbour cell, then press straight into the wall face
            // (plain force — the A* would refuse to route into a solid cell)
            if (MoveToward(drillApproach, 0.45f))
            {
                Vector2 dir = (face - (Vector2)transform.position).normalized;
                AS.TryAddForce(moveForce * DroneManager.Haste * dir, true);
                FaceDir(dir);
            }
            return;
        }

        // in contact — grind (durability is contact-time in ms; haste makes drones bite fast)
        FaceDir(face - (Vector2)transform.position);
        if (drillBit != null) drillBit.SetActiveDrilling(true);
        int ms = Mathf.RoundToInt(Time.fixedDeltaTime * 1000f * DroneManager.Haste * ActRateVisible);
        if (mf.DroneChip(drillTarget, ms, out bool broke, out CellType tier) && broke)
        {
            TrySpend(DroneManager.DrillCost(tier));
            hasDrillTarget = false;
            headingDeg += Random.Range(-8f, 8f);   // organic drift, still roughly one direction
        }
    }

    /// <summary>Scan for the next wall to eat: exposed (standable face), pocket-safe, affordable.
    /// The heading is a SIDE of the map (bearing from the entry), not a push direction: staying in
    /// the wedge stops back-and-forth, the depth penalty grows the dig in even rings instead of
    /// one deep bore, and hardness only nudges (soft preferred, not law). Ore is the prize.</summary>
    bool FindDrillTarget(MineField mf)
    {
        Vector3Int myCell = mf.WorldToCell(transform.position);
        Vector2 hd = HeadingDir();
        Vector2 origin = mf.CellCenterWorld(new Vector3Int(0, 0, 0));   // entry cavity centre
        float bestScore = float.MaxValue;
        bool found = false;
        for (int k = drillBlacklist.Count - 1; k >= 0; k--)   // bans age out
            if (Time.time >= drillBlacklist[k].until) drillBlacklist.RemoveAt(k);
        int R = Mathf.Max(2, drillScanRadius);
        for (int dx = -R; dx <= R; dx++)
        {
            for (int dy = -R; dy <= R; dy++)
            {
                var c = new Vector3Int(myCell.x + dx, myCell.y + dy, 0);
                if (!mf.DroneMineable(c)) continue;
                bool banned = false;
                for (int k = 0; k < drillBlacklist.Count; k++)
                    if (drillBlacklist[k].cell == c) { banned = true; break; }
                if (banned) continue;
                bool exposed = false;
                Vector3Int approach = default;
                foreach (var f in FaceOffsets)
                {
                    var nc = c + f;
                    if (mf.IsExcavated(nc)) { approach = nc; exposed = true; break; }
                }
                if (!exposed) continue;
                CellType tier = mf.CellTypeAt(c);
                if (DroneManager.DrillCost(tier) > energy) continue;
                int ore = mf.OreAt(c);
                Vector2 cellW = (Vector2)mf.CellCenterWorld(c);
                float d = (cellW - (Vector2)transform.position).magnitude;
                Vector2 fromOrigin = cellW - origin;
                float depth = fromOrigin.magnitude;
                float sectorPen = depth > 1.5f ? (1f - Vector2.Dot(fromOrigin / depth, hd)) * 2f : 0f;
                float depthPen = 0.4f * depth;
                float tierPen = tier == CellType.VeryHard ? 3f : tier == CellType.Hard ? 1.5f : 0f;
                float oreBonus = ore >= 0 ? (tier == CellType.Regular ? 7f : tier == CellType.Hard ? 4f : 2f) : 0f;
                float score = d + sectorPen + depthPen + tierPen - oreBonus;
                if (score < bestScore)
                {
                    bestScore = score;
                    drillTarget = c;
                    drillApproach = mf.CellCenterWorld(approach);
                    found = true;
                }
            }
        }
        hasDrillTarget = found;
        return found;
    }

    void StopDrillVisual()
    {
        if (drillBit != null) drillBit.SetActiveDrilling(false);
    }

    // ------------------------------------------------------------------ collecting (bag drones)

    int SackMaxSpace => sack != null ? sack.maxSpace : DroneManager.BagCapacity;
    int SpaceLeft => SackMaxSpace - cargoSpaceUsed;
    // One full charge buys EXACTLY one full bag: every space unit swallowed costs 1/maxSpace
    // energy, so capacity is whichever runs out first — physical room or remaining charge.
    float CollectCostPerSpace => 1f / SackMaxSpace;
    int EffectiveSpaceLeft => Mathf.Min(SpaceLeft, Mathf.FloorToInt(energy * SackMaxSpace + 1e-3f));

    void TickCollecting()
    {
        if (threatened) { ReleaseCollectTarget(); state = State.Fleeing; return; }
        bool dungeonRun = transform.InDungeon();
        if (EffectiveSpaceLeft <= 0)
        {
            ReleaseCollectTarget();
            state = dungeonRun ? State.ShuttleHome : State.DumpLoot;   // base runs dump directly
            return;
        }

        if (!HasValidCollectTarget() && !FindCollectTarget())
        {
            if (!dungeonRun)
            {
                // base sweep over: bank whatever was grabbed, else back on patrol
                if (HasCargo) state = State.DumpLoot;
                else GoLoiter();
                return;
            }
            // nothing to grab right now: park at the pad full-ish, or loiter at the rally point
            // (pathfinding move — a straight HoldAt shove wedges the drone against walls)
            if (cargoSpaceUsed > 0 && EffectiveSpaceLeft <= 2) { state = State.ShuttleHome; return; }
            MoveToward(RallySpot(), 0.8f);
            return;
        }

        Vector2 tpos = chipTarget != null ? (Vector2)chipTarget.transform.position
            : orbTarget != null ? (Vector2)orbTarget.transform.position
            : equipmentTarget != null ? (Vector2)equipmentTarget.transform.position
            : (Vector2)batteryTarget.transform.position;
        // chips are collected by TOUCH: close to actual contact, present the front, swallow
        float reach = chipTarget != null ? 0.32f : 0.45f;
        if (MoveToward(tpos, reach))
        {
            FaceDir(tpos - (Vector2)transform.position);
            PickupTarget();
        }
    }

    bool HasValidCollectTarget()
    {
        if (chipTarget != null && chipTarget.claimedBy == this && chipTarget.SpaceCost <= EffectiveSpaceLeft) return true;
        chipTarget = null;
        if (orbTarget != null && orbTarget.gameObject.activeInHierarchy && orbTarget.state == OrbScript.OrbState.wild
            && orbTarget.transform.InDungeon() == transform.InDungeon()) return true;
        orbTarget = null;
        if (equipmentTarget != null && equipmentTarget.transform.InDungeon()) return true;
        equipmentTarget = null;
        if (batteryTarget != null && batteryTarget.transform.InDungeon()) return true;
        batteryTarget = null;
        return false;
    }

    void ReleaseCollectTarget()
    {
        if (chipTarget != null && chipTarget.claimedBy == this) chipTarget.claimedBy = null;
        chipTarget = null;
        orbTarget = null;
        batteryTarget = null;
        equipmentTarget = null;
    }

    bool FindCollectTarget()
    {
        Vector2 pos = transform.position;
        int spaceLeft = EffectiveSpaceLeft;

        // Base-side sweeps take ORBS ONLY — the ore that base-mining drills shake loose. Chips
        // and dropped kit at base are dump-pile products; hauling those would just loop them.
        if (!transform.InDungeon())
        {
            var baseOrb = NearestBaseOrb(pos, spaceLeft);
            if (baseOrb != null) { orbTarget = baseOrb; return true; }
            return false;
        }

        // 1) chips — the bag drone's bread and butter
        float bestSqr = float.MaxValue;
        OreChip bestChip = null;
        for (int k = 0; k < OreChip.all.Count; k++)
        {
            var chip = OreChip.all[k];
            if (chip == null || !chip.transform.InDungeon()) continue;
            if (chip.Age < OreChip.SettleSeconds) continue;   // let debris visibly land first
            if (chip.claimedBy != null && chip.claimedBy != this) continue;
            if (chip.SpaceCost > spaceLeft) continue;
            float d = ((Vector2)chip.transform.position - pos).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; bestChip = chip; }
        }
        if (bestChip != null)
        {
            bestChip.claimedBy = this;
            chipTarget = bestChip;
            return true;
        }

        // 2) loose wild orbs
        int orbSpace = sack != null ? sack.orbSpace : 1;
        if (orbSpace <= spaceLeft && OrbManager.allOrbs != null)
        {
            bestSqr = float.MaxValue;
            OrbScript bestOrb = null;
            for (int k = 0; k < OrbManager.allOrbs.Count; k++)
            {
                var o = OrbManager.allOrbs[k];
                if (o == null || !o.gameObject.activeInHierarchy) continue;
                if (o.state != OrbScript.OrbState.wild || !o.transform.InDungeon()) continue;
                float d = ((Vector2)o.transform.position - pos).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; bestOrb = o; }
            }
            if (bestOrb != null) { orbTarget = bestOrb; return true; }
        }

        // 3) dropped drone kit (dead drill drones leave their drills behind)
        int equipmentSpace = sack != null ? sack.equipmentSpace : 3;
        if (equipmentSpace <= spaceLeft)
        {
            bestSqr = float.MaxValue;
            DroneEquipmentItem bestItem = null;
            for (int k = 0; k < DroneEquipmentItem.all.Count; k++)
            {
                var it = DroneEquipmentItem.all[k];
                if (it == null || !it.transform.InDungeon()) continue;
                float d = ((Vector2)it.transform.position - pos).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; bestItem = it; }
            }
            if (bestItem != null) { equipmentTarget = bestItem; return true; }
        }

        // 4) rarer finds: loose batteries (registry walk, no scene scan)
        int batterySpace = sack != null ? sack.batterySpace : 4;
        if (batterySpace <= spaceLeft)
        {
            bestSqr = float.MaxValue;
            Battery bestBat = null;
            for (int k = 0; k < Battery.all.Count; k++)
            {
                var bat = Battery.all[k];
                if (bat == null || !bat.transform.InDungeon()) continue;
                if (bat.pad != null || bat == Battery.held) continue;   // slotted/held ones aren't loot
                float d = ((Vector2)bat.transform.position - pos).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; bestBat = bat; }
            }
            if (bestBat != null) { batteryTarget = bestBat; return true; }
        }
        return false;
    }

    /// <summary>Wild base-side orb worth hauling: outside the scrap-pile exclusion ring (freshly
    /// dumped orbs must never be re-collected in a loop) and near enough to bother.</summary>
    OrbScript NearestBaseOrb(Vector2 pos, int spaceLeft)
    {
        int orbSpace = sack != null ? sack.orbSpace : 1;
        if (orbSpace > spaceLeft || OrbManager.allOrbs == null) return null;
        float bestSqr = 30f * 30f;
        OrbScript best = null;
        for (int k = 0; k < OrbManager.allOrbs.Count; k++)
        {
            var o = OrbManager.allOrbs[k];
            if (o == null || !o.gameObject.activeInHierarchy) continue;
            if (o.state != OrbScript.OrbState.wild || o.transform.InDungeon()) continue;
            Vector2 op = o.transform.position;
            if ((op - DroneManager.ScrapPoint).sqrMagnitude < 2.5f * 2.5f) continue;
            float d = (op - pos).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; best = o; }
        }
        return best;
    }

    /// <summary>Docked-bag dispatch check, throttled — a parked drone shouldn't walk the whole
    /// orb registry every physics tick.</summary>
    bool BaseOrbAvailable()
    {
        if ((orbScanT -= Time.fixedDeltaTime) > 0f) return false;
        orbScanT = 0.5f;
        return NearestBaseOrb(transform.position, EffectiveSpaceLeft) != null;
    }

    void PickupTarget()
    {
        if (chipTarget != null)
        {
            AddCargo(new CargoEntry { kind = 0, space = chipTarget.SpaceCost, sizeClass = chipTarget.sizeClass, element = chipTarget.element });
            chipTarget.AbsorbInto(transform);   // visible swallow: ease-out shrink into the front
            chipTarget = null;
        }
        else if (orbTarget != null)
        {
            if (orbTarget.state == OrbScript.OrbState.wild)
            {
                AddCargo(new CargoEntry { kind = 1, space = sack != null ? sack.orbSpace : 1, element = orbTarget.orbType });
                orbTarget.ReturnToPool();
            }
            orbTarget = null;
        }
        else if (equipmentTarget != null)
        {
            var go = equipmentTarget.gameObject;
            AddCargo(new CargoEntry { kind = 2, space = sack != null ? sack.equipmentSpace : 3, payload = go });
            go.transform.SetParent(transform);
            go.SetActive(false);
            equipmentTarget = null;
        }
        else if (batteryTarget != null)
        {
            var go = batteryTarget.gameObject;
            AddCargo(new CargoEntry { kind = 2, space = sack != null ? sack.batterySpace : 4, payload = go });
            go.transform.SetParent(transform);
            go.SetActive(false);
            batteryTarget = null;
        }
    }

    void AddCargo(CargoEntry e)
    {
        cargo.Add(e);
        cargoSpaceUsed += e.space;
        // the bag's tariff: space swallowed is charge spent, so one full charge = one full bag
        energy = Mathf.Max(0f, energy - e.space * CollectCostPerSpace);
        SetCargoUnits(cargoSpaceUsed);
        if (sack != null) sack.SetFill(cargoSpaceUsed / (float)SackMaxSpace);
    }

    void TickShuttleHome()
    {
        // Run over (bag full or charge spent): park at the dungeon pad and WAIT — drones never
        // ride the link on their own. The only ways home are the player's teleport (DroneManager's
        // recall sweep) — pads coming online for the first time mid-dive only deploy, never recall.
        if (!transform.InDungeon()) { state = HasCargo ? State.DumpLoot : State.ReturningToDock; return; }
        Telepad pad = assignedPad != null ? (assignedPad.IsDungeonSide ? assignedPad : assignedPad.Linked) : null;
        Vector2 park = pad != null && pad.IsOperational ? (Vector2)pad.RallyPoint : RallySpot();
        MoveToward(park, 0.5f);
    }

    void TickDumpLoot()
    {
        if (!HasCargo) { state = State.ReturningToDock; return; }
        if (!MoveToward(DroneManager.ScrapPoint, 0.6f)) return;
        DumpCargoAt(DroneManager.ScrapPoint);
        // No solo hop back down — the next deployment rides the player's dive (or a freshly
        // built pad's TryDeployNow). More work if there is any, else patrol; a spent charge
        // routes home by itself (the loiter tick sends flat drones to the charger).
        if (!TryDispatchWork()) GoLoiter();
    }

    /// <summary>Spill everything: chips re-scatter as scrap, orbs burst out wild, carried items
    /// (batteries/equipment) drop as real objects. Also the death-drop path.</summary>
    void DumpCargoAt(Vector2 p)
    {
        var orbCounts = new int[4];
        foreach (var e in cargo)
        {
            if (e.kind == 0)
            {
                DroneManager.SpawnScrap((Vector3)p + GS.RandCircle(0.1f, 0.9f), e.sizeClass, e.element);
            }
            else if (e.kind == 1)
            {
                if (e.element >= 0 && e.element < 4) orbCounts[e.element]++;
            }
            else if (e.payload != null)
            {
                e.payload.transform.SetParent(GS.FindParent(GS.Parent.loot));
                e.payload.transform.position = (Vector3)p + GS.RandCircle(0.2f, 0.8f);
                e.payload.SetActive(true);
            }
        }
        if (orbCounts[0] + orbCounts[1] + orbCounts[2] + orbCounts[3] > 0)
            GS.CallSpawnOrbs(p, orbCounts);
        cargo.Clear();
        cargoSpaceUsed = 0;
        SetCargoUnits(0);
        if (sack != null) sack.SetFill(0f);
    }

    // ------------------------------------------------------------------ rally fight

    void ApplyRallyBuff()
    {
        if (rallyBuffed) return;
        rallyBuffed = true;
        rallyBuffTimer = 10f;   // matches the shield's full life: 6s hold + 4s ramp-down
        // Self-expiring: instant at full strength, holds 6s, then ramps to zero over 4s — no
        // manual removal, so a recall/state hijack mid-fight can't strand a permanent shield.
        ShieldUtility.DecayingShield(this, 5f, 6f, 4f);
        // 7s, not the shield's nominal 10: the shield READS as spent early in its ramp-down,
        // so a full-length stim visibly outlived it — this ends the pair together to the eye.
        GS.Stat(this, "stim", 7f, 1.35f);
    }

    void TickRallyFight()
    {
        // Both dimensions: dungeon rallies at the pad/player, base rallies at the (0,0) refuge.
        bool dungeon = transform.InDungeon();
        Vector2 rallyCenter = RallySpot();
        float leash = DroneManager.RallyLeash;
        if (!Charged) { EndRallyFight(dungeon ? State.DeployedTravel : State.ReturningToDock); return; }
        if (!threatened)
        {
            EndRallyFight(!dungeon ? State.ReturningToDock
                : equipment == DroneEquipment.Drill ? State.Drilling : State.DeployedTravel);
            return;
        }

        if (((Vector2)transform.position - rallyCenter).sqrMagnitude > leash * leash)
        {
            StopDrillVisual();
            MoveToward(rallyCenter, 1f);   // never adventure beyond the leash
            return;
        }

        Transform e = null;
        if (dungeon)
        {
            if (MinePathManager.TryNearestEnemy(transform.position, out Transform t, out _)) e = t;
        }
        else
        {
            // Base-side the refuge sits on a building footprint — a BLOCKED cell in the base
            // flow grid, where the path query reads UNREACHED and names nobody (drones hovered
            // at (0,0) without ever engaging). The arena is open ground: a plain radius scan
            // around the rally point targets fine.
            var arena = GS.FindEnemies(tag, rallyCenter, leash, false, false);
            float best = float.MaxValue;
            for (int k = 0; k < arena.Count; k++)
            {
                if (arena[k] == null) continue;
                float d2 = ((Vector2)arena[k].position - (Vector2)transform.position).sqrMagnitude;
                if (d2 < best) { best = d2; e = arena[k]; }
            }
        }
        if (e != null && ((Vector2)e.position - rallyCenter).sqrMagnitude <= leash * leash)
        {
            FaceDir((Vector2)e.position - (Vector2)transform.position);
            MoveToward(e.position, 0.45f);
            // stim + shield only once the drone actually closes on its target, not en route
            if (!rallyBuffed && ((Vector2)e.position - (Vector2)transform.position).sqrMagnitude < 1.2f * 1.2f)
                ApplyRallyBuff();
            if (drillBit != null)
            {
                drillBit.SetActiveDrilling(true);
                drillBit.CombatTick();
            }
        }
        else
        {
            StopDrillVisual();
            MoveToward(rallyCenter, 0.6f);
        }
    }

    void EndRallyFight(State next)
    {
        // The shield decays on its own schedule; rallyBuffed rides out its timer so hopping
        // out and back into a fight can't stack fresh shields.
        StopDrillVisual();
        state = next;
    }

    // ------------------------------------------------------------------ idle life

    /// <summary>Animation seam: every expressive beat routes through here. Today it pops the
    /// minimal speech bubble; real body animation can later hang off the same calls.</summary>
    public void Emote(DroneEmote e, float hold = 1.4f)
    {
        if (bubble == null) bubble = DroneSpeechBubble.Attach(transform, sr);
        bubble.Show(e, hold);
    }

    float NextIdleDelay()
        => DroneManager.IdleCooldown * Mathf.Lerp(1.6f, 0.5f, restless) * Random.Range(0.7f, 1.4f);

    /// <summary>THE COLONY JOB BOARD, in priority order — shared by the dock and every idle
    /// state, so a drone anywhere notices work without being ordered. Telepad requests are NOT
    /// here: those are filled at dive time by DroneManager, never chased by individuals. Sets
    /// state and returns true when a job took.</summary>
    bool TryDispatchWork()
    {
        if (!Charged) return false;
        // drill: deconstruct marked ore — unless the whole drill fleet is reserved by telepad
        // requests (they save their charge for the dive; see DroneManager.BaseMiningAllowed)
        if (equipment == DroneEquipment.Drill && !transform.InDungeon()
            && DroneManager.BaseMiningAllowedCached() && OreMarks.Any)
        {
            state = State.MineBaseOre;
            return true;
        }
        if (equipment == DroneEquipment.None && Peaceful()
            && DroneManager.RepairWorkAvailable(PathZone.AtBase(transform.position)))
        {
            state = State.RepairSweep;
            return true;
        }
        if (equipment == DroneEquipment.None && TryTakeKitJob()) return true;
        if (equipment == DroneEquipment.None && TryTakePilotSeat()) return true;
        if (equipment == DroneEquipment.Bag && Peaceful() && BaseOrbAvailable())
        {
            state = State.Collecting;
            return true;
        }
        return false;
    }

    /// <summary>Colony kit pickup: claim the nearest unclaimed ground kit, else fly to a
    /// workshop with unclaimed stock. One claimant per item / unit of stock.</summary>
    bool TryTakeKitJob()
    {
        Vector2 pos = transform.position;
        DroneEquipmentItem item = null;
        float bestSqr = float.MaxValue;
        for (int k = 0; k < DroneEquipmentItem.all.Count; k++)
        {
            var it = DroneEquipmentItem.all[k];
            if (it == null || it.transform.InDungeon()) continue;
            if (it.claimedBy != null && it.claimedBy != this) continue;
            float d2 = ((Vector2)it.transform.position - pos).sqrMagnitude;
            if (d2 < bestSqr) { bestSqr = d2; item = it; }
        }
        if (item != null)
        {
            item.claimedBy = this;
            fetchItem = item;
            state = State.FetchKit;
            return true;
        }
        EquipmentWorkshop ws = null;
        bestSqr = float.MaxValue;
        var list = Building.buildings;
        for (int k = 0; k < list.Count; k++)
        {
            if (list[k] is not EquipmentWorkshop w || !w.HasUnclaimedStock) continue;
            if (!PathZone.AtBase(w.transform.position)) continue;
            float d2 = ((Vector2)w.transform.position - pos).sqrMagnitude;
            if (d2 < bestSqr) { bestSqr = d2; ws = w; }
        }
        if (ws == null) return false;
        ws.TryClaim(this);
        return true;
    }

    /// <summary>Colony crewing: a base-side hull short of pilots takes any spare drone.</summary>
    bool TryTakePilotSeat()
    {
        for (int k = 0; k < PilotedVehicle.all.Count; k++)
        {
            var v = PilotedVehicle.all[k];
            if (v == null || v.transform.InDungeon() || !v.NeedsPilots) continue;
            v.AssignPilot(this);
            state = State.BoardingVehicle;
            return true;
        }
        return false;
    }

    void TickFetchKit()
    {
        if (threatened) { ReleaseFetch(); state = State.Evading; return; }
        var it = fetchItem;
        if (it == null || it.transform.InDungeon() || (it.claimedBy != null && it.claimedBy != this))
        {
            fetchItem = null;
            GoLoiter();
            return;
        }
        if (!Charged) { ReleaseFetch(); state = State.ReturningToDock; return; }
        if (!MoveToward(it.transform.position, 0.35f)) return;
        var kind = it.kind;
        fetchItem = null;
        Destroy(it.gameObject);
        SetEquipment(kind);
        Emote(DroneEmote.Happy, 1f);   // new kit day
        if (!TryDispatchWork()) GoLoiter();
    }

    void ReleaseFetch()
    {
        if (fetchItem != null && fetchItem.claimedBy == this) fetchItem.claimedBy = null;
        fetchItem = null;
    }

    /// <summary>Boredom struck at the dock: schmoozers look for company first, everyone else
    /// heads out on patrol. Nobody comes back until a wave, a job, or a flat battery.</summary>
    void StartIdleJaunt()
    {
        idleTimer = NextIdleDelay();
        if (Random.value < chatty * 0.6f && TryStartChat()) return;
        GoLoiter();
        if (Random.value < 0.35f) Emote(DroneEmote.Sing);
    }

    /// <summary>Resume the patrol from wherever we are — the off-duty ground state.</summary>
    void GoLoiter()
    {
        PickLoiterPoint();
        loiterPauseT = 0f;
        state = State.Loitering;
    }

    /// <summary>Somewhere to drift: half the time sightseeing at a base building, otherwise
    /// open air around home turf (the dock anchors the patrol so nobody wanders off the map).</summary>
    void PickLoiterPoint()
    {
        loiterSightseeing = false;
        if (Random.value < 0.5f)
        {
            Building b = RandomBaseSight();
            if (b != null)
            {
                loiterSightseeing = true;
                loiterPoint = (Vector2)b.transform.position + GS.RandCircleV2(0.7f, 1.3f);
                return;
            }
        }
        Vector2 anchor = dock != null ? (Vector2)dock.transform.position : Vector2.zero;
        loiterPoint = anchor + GS.RandCircleV2(1.5f, Mathf.Max(2f, DroneManager.IdleWanderRadius));
    }

    /// <summary>Reservoir-pick a built base-side building within patrol range.</summary>
    Building RandomBaseSight()
    {
        Building pick = null;
        int seen = 0;
        Vector2 pos = transform.position;
        var list = Building.buildings;
        for (int k = 0; k < list.Count; k++)
        {
            Building b = list[k];
            if (b == null || !b.gameObject.activeInHierarchy) continue;
            if (!PathZone.AtBase(b.transform.position)) continue;
            if (((Vector2)b.transform.position - pos).sqrMagnitude > 15f * 15f) continue;
            seen++;
            if (Random.Range(0, seen) == 0) pick = b;
        }
        return pick;
    }

    /// <summary>Free for social calls: off-duty base-side, charged, calm, not already paired
    /// up or seated at a card table.</summary>
    bool IdleFree(Drone d)
        => d != null && d != this && !d.transform.InDungeon()
        && (d.state == State.Docked || d.state == State.Loitering)
        && d.Charged && !d.threatened && d.chatPartner == null && d.cardHost == null;

    void TickLoiter()
    {
        if (threatened) { state = State.Evading; return; }
        // the only trips home: shelter from a wave, or a flat battery needing the charger
        if (!Peaceful() || !Charged) { state = State.ReturningToDock; return; }
        if (TryDispatchWork()) return;

        // passing hi: another idler close by → both stop for a quick o/ (5–10s)
        if (Time.time >= greetReadyT && (hiScanT -= Time.fixedDeltaTime) <= 0f)
        {
            hiScanT = 0.7f;
            Drone other = NearbyIdler(2.6f);
            if (other != null) { StartHi(other); return; }
        }

        if (!MoveToward(loiterPoint, 0.45f, DroneManager.IdleSpeedScale)) return;

        // arrived: wonder a moment (restless drones linger less), maybe say something
        if (loiterPauseT <= 0f)
        {
            loiterPauseT = Random.Range(0.8f, 4f) * Mathf.Lerp(1.3f, 0.6f, restless);
            if (Random.value < 0.3f + chatty * 0.3f)
                Emote(loiterSightseeing ? DroneEmote.Query
                    : Random.value < 0.5f ? DroneEmote.Sing : DroneEmote.Chat);
            return;
        }
        loiterPauseT -= Time.fixedDeltaTime;
        if (loiterPauseT > 0f) return;

        // endless patrol: pick the next thing — a card table, a natter, or another stop
        float roll = Random.value;
        if (roll < 0.1f && TryStartCards()) return;
        if (roll < 0.1f + chatty * 0.25f && TryStartChat()) return;
        PickLoiterPoint();
    }

    Drone NearbyIdler(float range)
    {
        for (int k = 0; k < allies.Count; k++)
        {
            if (allies[k] is not Drone d || !IdleFree(d)) continue;
            if (Time.time < d.greetReadyT) continue;
            if (((Vector2)d.transform.position - (Vector2)transform.position).sqrMagnitude <= range * range)
                return d;
        }
        return null;
    }

    /// <summary>Crossed paths with another idler: both stop for a quick wave and a couple of
    /// friendly beats (5–10s), then carry on. Cooldown-gated so a pair can't ping-pong hellos.</summary>
    void StartHi(Drone other)
    {
        LinkChat(other, quick: true, Random.Range(5f, 10f));
        greetReadyT = Time.time + Random.Range(25f, 45f);
        other.greetReadyT = Time.time + Random.Range(25f, 45f);
        Emote(DroneEmote.Greet, 1f);
        other.Emote(DroneEmote.Greet, 1f);
    }

    /// <summary>Find an off-duty base-side drone for a proper natter. Both parties fly to the
    /// midpoint so neither reads as summoned. Returns false when nobody's free.</summary>
    bool TryStartChat()
    {
        Drone mate = null;
        int seen = 0;
        Vector2 pos = transform.position;
        for (int k = 0; k < allies.Count; k++)
        {
            if (allies[k] is not Drone d || !IdleFree(d)) continue;
            if (((Vector2)d.transform.position - pos).sqrMagnitude > 12f * 12f) continue;
            seen++;
            if (Random.Range(0, seen) == 0) mate = d;
        }
        if (mate == null) return false;
        LinkChat(mate, quick: false, Random.Range(8f, 16f));
        Emote(DroneEmote.Greet, 1f);
        return true;
    }

    void LinkChat(Drone mate, bool quick, float duration)
    {
        Vector2 meet = ((Vector2)transform.position + (Vector2)mate.transform.position) * 0.5f
            + GS.RandCircleV2(0.1f, 0.5f);
        chatPartner = mate; mate.chatPartner = this;
        chatInitiator = true; mate.chatInitiator = false;
        chatQuick = quick; mate.chatQuick = quick;
        chatSpot = meet; mate.chatSpot = meet;
        chatEndT = Time.time + duration;
        chatBeat = quick ? 1.6f : 0.6f;
        chatBeatCount = 0;
        state = State.Chatting; mate.state = State.Chatting;
    }

    /// <summary>Sever the chat link both ways. The abandoned side resumes its patrol (and
    /// sometimes grumbles about it, unless the parting was <paramref name="amicable"/>).
    /// Safe to call in any state — no-op when not chatting.</summary>
    void BreakChat(bool amicable = false)
    {
        var mate = chatPartner;
        chatPartner = null;
        if (mate != null && mate.chatPartner == this)
        {
            mate.chatPartner = null;
            if (mate.state == State.Chatting)
            {
                mate.GoLoiter();
                if (!amicable && Random.value < 0.5f) mate.Emote(DroneEmote.Grumble, 1f);
            }
        }
    }

    void TickChat()
    {
        if (threatened) { BreakChat(); state = State.Evading; return; }
        if (!Peaceful() || !Charged) { BreakChat(); state = State.ReturningToDock; return; }
        if (TryDispatchWork()) { BreakChat(); return; }   // duty calls mid-natter
        var mate = chatPartner;
        if (mate == null || mate.chatPartner != this || mate.state != State.Chatting)
        {
            // stood up mid-sentence
            chatPartner = null;
            GoLoiter();
            return;
        }

        Vector2 toMate = (Vector2)mate.transform.position - (Vector2)transform.position;
        if (toMate.sqrMagnitude > 1.1f * 1.1f && !MoveToward(chatSpot, 0.4f, DroneManager.IdleSpeedScale))
            return;

        // in position: settle, face each other, trade lines strictly one at a time (the
        // initiator runs the alternating beat for both)
        AS.Decelerate(0.2f, 0.5f);
        FaceDir(toMate);
        if (!chatInitiator) return;
        if (Time.time >= chatEndT)
        {
            Emote(DroneEmote.Happy, 1.2f);
            mate.Emote(DroneEmote.Happy, 1.2f);
            BreakChat(amicable: true);   // sends the mate back on patrol
            GoLoiter();
            return;
        }
        chatBeat -= Time.fixedDeltaTime;
        if (chatBeat > 0f) return;
        chatBeat = chatQuick ? Random.Range(2f, 3f) : Random.Range(1.3f, 2.1f);
        Drone speaker = (chatBeatCount++ & 1) == 0 ? this : mate;
        float r = Random.value;
        if (chatQuick)
            speaker.Emote(r < 0.5f ? DroneEmote.Greet : r < 0.85f ? DroneEmote.Happy : DroneEmote.Chat, 1.1f);
        else
            speaker.Emote(r < 0.45f ? DroneEmote.Chat : r < 0.7f ? DroneEmote.Query
                : r < 0.9f ? DroneEmote.Sing : DroneEmote.Happy, 1.1f);
    }

    // ------------------------------------------------------------------ cards

    /// <summary>Deal in 3–5 nearby idlers (4–6 seats with the host) around a table point. The
    /// host runs the game like a chat initiator: turn rotation, reactions, the final pot.</summary>
    bool TryStartCards()
    {
        var found = new System.Collections.Generic.List<Drone>();
        Vector2 pos = transform.position;
        for (int k = 0; k < allies.Count; k++)
        {
            if (allies[k] is not Drone d || !IdleFree(d)) continue;
            if (((Vector2)d.transform.position - pos).sqrMagnitude > 13f * 13f) continue;
            found.Add(d);
        }
        if (found.Count < 3) return false;
        for (int k = found.Count - 1; k > 0; k--)   // shuffle so the table mix varies
        {
            int j = Random.Range(0, k + 1);
            (found[k], found[j]) = (found[j], found[k]);
        }
        var players = new System.Collections.Generic.List<Drone> { this };
        for (int k = 0; k < Mathf.Min(5, found.Count); k++) players.Add(found[k]);

        Vector2 c = Vector2.zero;
        foreach (var p in players) c += (Vector2)p.transform.position;
        c = c / players.Count + GS.RandCircleV2(0.1f, 0.6f);

        cardPlayers = players;
        cardCenter = c;
        cardEndT = Time.time + Random.Range(18f, 30f);
        cardBeatT = 2f;
        cardTurn = 0;
        for (int k = 0; k < players.Count; k++)
        {
            var p = players[k];
            p.cardHost = this;
            p.cardSeatAng = k * (360f / players.Count) + Random.Range(-8f, 8f);
            p.state = State.Playing;
        }
        Emote(DroneEmote.Greet, 1f);
        return true;
    }

    void TickPlaying()
    {
        if (threatened) { LeaveCards(); state = State.Evading; return; }
        if (!Peaceful() || !Charged) { LeaveCards(); state = State.ReturningToDock; return; }
        if (TryDispatchWork()) { LeaveCards(); return; }   // folds and clocks in
        var host = cardHost;
        if (host == null) { GoLoiter(); return; }
        if (host != this && (host.cardHost != host || host.state != State.Playing))
        {
            cardHost = null;
            GoLoiter();
            return;
        }

        Vector2 seat = host.cardCenter + new Vector2(
            Mathf.Cos(cardSeatAng * Mathf.Deg2Rad), Mathf.Sin(cardSeatAng * Mathf.Deg2Rad)) * 0.85f;
        if (!MoveToward(seat, 0.3f, DroneManager.IdleSpeedScale)) return;
        AS.Decelerate(0.2f, 0.5f);
        FaceDir(host.cardCenter - (Vector2)transform.position);
        if (host != this) return;

        // host runs the table: prune anyone who left, one card played at a time round the circle
        for (int k = cardPlayers.Count - 1; k >= 0; k--)
        {
            var p = cardPlayers[k];
            if (p == null || p.cardHost != this || p.state != State.Playing) cardPlayers.RemoveAt(k);
        }
        if (cardPlayers.Count < 2) { DisbandCards(finished: false); return; }
        if (Time.time >= cardEndT) { DisbandCards(finished: true); return; }
        cardBeatT -= Time.fixedDeltaTime;
        if (cardBeatT > 0f) return;
        cardBeatT = Random.Range(1.2f, 1.9f);
        cardTurn = (cardTurn + 1) % cardPlayers.Count;
        cardPlayers[cardTurn].Emote(DroneEmote.Card, 1.1f);
        if (Random.value < 0.25f)
        {
            // table talk: someone else reacts to the play
            int other = (cardTurn + Random.Range(1, cardPlayers.Count)) % cardPlayers.Count;
            cardPlayers[other].Emote(Random.value < 0.5f ? DroneEmote.Happy : DroneEmote.Grumble, 0.9f);
        }
    }

    /// <summary>Host ends the game: someone takes the pot, someone takes it badly, everyone
    /// drifts back onto patrol.</summary>
    void DisbandCards(bool finished)
    {
        var players = cardPlayers;
        cardPlayers = null;
        cardHost = null;
        if (players == null) { GoLoiter(); return; }
        int winner = finished ? Random.Range(0, players.Count) : -1;
        for (int k = 0; k < players.Count; k++)
        {
            var p = players[k];
            if (p == null) continue;
            p.cardHost = null;
            if (finished)
                p.Emote(k == winner ? DroneEmote.Happy
                    : Random.value < 0.35f ? DroneEmote.Grumble : DroneEmote.Chat, 1.3f);
            if (p != this && p.state == State.Playing) p.GoLoiter();
        }
        GoLoiter();
    }

    /// <summary>Drop out of a card game from either side of the table. A departing host folds
    /// the whole game; a departing member just leaves a seat for the host to prune.</summary>
    void LeaveCards()
    {
        if (cardPlayers != null)
        {
            var players = cardPlayers;
            cardPlayers = null;
            foreach (var p in players)
            {
                if (p == null || p == this || p.cardHost != this) continue;
                p.cardHost = null;
                if (p.state == State.Playing) p.GoLoiter();
            }
        }
        cardHost = null;
    }

    // ------------------------------------------------------------------ base ore mining

    /// <summary>Deconstruct player-marked ore tiles (OreMarks): each tile is eaten whole in
    /// ~3s of contact (DroneManager.BaseOreEatSeconds) for HALF its orb value — no debris
    /// chips, the yield is wild orbs at the tile, which bag drones sweep up. Self-assigned;
    /// re-checks the fleet reservation every tick so filling a telepad slot mid-grind makes
    /// the miners down tools and save their charge.</summary>
    void TickMineBaseOre()
    {
        if (threatened) { StopDrillVisual(); ReleaseOre(); state = State.Evading; return; }
        if (equipment != DroneEquipment.Drill || transform.InDungeon())
        {
            StopDrillVisual();
            ReleaseOre();
            GoLoiter();
            return;
        }
        if (!Charged)
        {
            StopDrillVisual();
            ReleaseOre();
            Emote(DroneEmote.Sleepy, 1.6f);
            state = State.ReturningToDock;
            return;
        }
        if (!DroneManager.BaseMiningAllowedCached())
        {
            StopDrillVisual();
            ReleaseOre();
            GoLoiter();   // reserved for the dive — save the charge
            return;
        }
        if (oreTarget == null || oreTarget.Depleted || !OreMarks.IsMarked(oreTarget))
        {
            StopDrillVisual();
            ReleaseOre();
            oreTarget = OreMarks.ClaimFor(this, transform.position);
            if (oreTarget == null)
            {
                Emote(DroneEmote.Happy, 1.4f);   // board clear — off duty on the spot
                GoLoiter();
                return;
            }
        }

        Vector2 p = oreTarget.transform.position;
        if (!MoveToward(p, 0.8f)) { StopDrillVisual(); return; }
        FaceDir(p - (Vector2)transform.position);
        if (drillBit != null) drillBit.SetActiveDrilling(true);

        // grind in coarse ticks so FX/orb spawns don't spam every physics frame
        oreTickT -= Time.fixedDeltaTime;
        if (oreTickT > 0f) return;
        const float tick = 0.25f;
        oreTickT = tick;
        int give = oreTarget.ChipDrone(tick, DroneManager.BaseOreEatSeconds);
        if (give > 0)
        {
            energy = Mathf.Max(0f, energy - give * DroneManager.BaseOreCostPerOrb);
            int[] counts = new int[4];
            counts[Mathf.Clamp(oreTarget.orbType, 0, 3)] = give;
            GS.CallSpawnOrbs(p, counts);
        }
    }

    void ReleaseOre()
    {
        if (oreTarget != null && oreTarget.miner == this) oreTarget.miner = null;
        oreTarget = null;
    }

    // ------------------------------------------------------------------ movement

    /// <summary>Physics-steered move with the ally A* (never chews walls, both dimensions).
    /// Returns true once within <paramref name="arrive"/> of the point. <paramref name="speedScale"/>
    /// under 1 gives the lazy off-duty drift (work moves stay at 1).</summary>
    protected bool MoveToward(Vector2 point, float arrive = 0.35f, float speedScale = 1f)
    {
        Vector2 offset = point - (Vector2)transform.position;
        if (offset.sqrMagnitude <= arrive * arrive)
        {
            AS.Decelerate(0.2f, 0.5f);
            return true;
        }
        repathTimer -= Time.fixedDeltaTime;
        if (repathTimer <= 0f)
        {
            repathTimer = 0.25f;   // cached A* cadence, same budget as ClawBot
            // bodyRadius > 0 → LOS-simplified path: diagonals fly straight instead of the
            // A* staircase zigzag
            pathValid = MinePath.StepToward(transform.position, point, out pathDir, 4096, 0.25f)
                && pathDir != Vector2.zero;
        }
        Vector2 dir = pathValid ? pathDir : offset.normalized;
        AS.TryAddForce(moveForce * speedScale * DroneManager.Haste * Mathf.Max(0.2f, actRate) * dir, true);
        FaceDir(dir);
        return false;
    }

    /// <summary>Gentle station-keeping around a point (docked hover).</summary>
    void HoldAt(Vector2 point, float slack)
    {
        Vector2 offset = point - (Vector2)transform.position;
        if (offset.sqrMagnitude > slack * slack)
            AS.TryAddForce(moveForce * 0.5f * offset.normalized, true);
    }

    // ------------------------------------------------------------------ interaction / death

    public override void OnClick()
    {
        // COLONY MODEL: units are not player-controlled — clicking a drone does nothing. The
        // override must stay (AllyAI's OnClick group-joins and dereferences a null home).
    }

    /// <summary>Re-implementation of IOnDeath — AllyAI.OnDeath is non-virtual and calls
    /// home.ReduceLive() on a null home. LifeScript dispatches through the interface map,
    /// which re-binds to this because the class re-lists IOnDeath.</summary>
    public new void OnDeath()
    {
        allies.Remove(this);
        ClearPathRegistrations();
        ReleaseCollectTarget();
        if (HasCargo) DumpCargoAt(transform.position);   // loot spills where the drone fell
        if (equipment == DroneEquipment.Drill || equipment == DroneEquipment.Bag)
            DroneEquipmentItem.Spawn(equipment, transform.position);   // kit survives its owner
        LeaveCurrentRole();
        if (dock != null) dock.NotifyResidentDied(this);
    }

    public override void OnDestroy()
    {
        ClearPathRegistrations();
        base.OnDestroy();
    }

    void ClearPathRegistrations()
    {
        BasePathManager.UntargetableAllies.Remove(transform);
        MinePathManager.UnregisterAllyTarget(transform);
    }
}
