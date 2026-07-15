using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pure-relay energy pylon. No storage — but it DOES own a surge pool (instabuffer of 1,
/// grown by attached CapacitorNodes) that lets bursts through it exceed the upstream rate
/// for a frame; the energy itself still drains from upstream stores. Forwards Use to its
/// upstream sources (adjacent pads/generators + cable-connected pylons or generators);
/// reports Energy/MaxEnergy by summing the same set.
///
/// Each outgoing cable is rate-capped at <see cref="perCableCap"/> e/sec so one pylon
/// can't act as a megabus — parallel pylons are the way to scale supply. A tier-1
/// pylon at full saturation (4 cables × 4 e/sec) tops out at 16 e/sec.
///
/// Three tiers via AddUpgradeSlot (mirrors SpreadTower):
///   tier 1 — Energy Pylon       1x1, 4 cables within 4 units
///   tier 2 — Long-Pylon        1.5x1.5, 4 cables within 8 units
///   tier 3 — Multi-Pylon        2x2, unlimited cables within 4 units
/// Adjacency-supplied sources don't count toward the cable cap.
///
/// Pylon→pylon chains work: each pylon's Use forwards to its own upstreams. Cycles are
/// guarded by a per-instance "resolving" bool so a Use/Energy query already in flight
/// short-circuits if the recursion comes back through.
///
/// Connection UX is delegated to the Connectable component: press = start drag,
/// release-without-drag = toggle UI, release on valid target = snap-and-wiggle cable.
/// </summary>
[RequireComponent(typeof(Connectable))]
public class EnergyPylon : Building, IEnergyAccumulator
{
    [Header("Pylon — upgrade sprites")]
    [SerializeField] private Sprite longPylonSprite;
    [SerializeField] private Sprite multiPylonSprite;
    [Tooltip("Slot 0 = Long-Pylon upgrade icon, Slot 1 = Multi-Pylon upgrade icon.")]
    [SerializeField] private Sprite[] tileSprites = new Sprite[2];

    [Header("Pylon — upgrade costs")]
    [SerializeField] private int[] tier2Cost = { 30, 0, 1, 0 };
    [SerializeField] private int[] tier3Cost = { 60, 0, 3, 0 };

    [Header("Pylon — rate cap")]
    [Tooltip("Maximum energy/sec a single cable can transmit. Total throughput = perCableCap × downstream-cable count.")]
    [SerializeField] private float perCableCap = 4f;

    [Header("Pylon — surge pool")]
    [Tooltip("The pylon's own instabuffer: burst credit shared across all its cables and adjacency draws. Capacitor nodes add theirs on top.")]
    [SerializeField] private float surgeMaxOwn = 1f;
    [Tooltip("Energy/sec the own pool recovers at when it wasn't debited this frame.")]
    [SerializeField] private float surgeRefillRate = 1f;

    // The surge pool holds REAL energy: it starts empty and is filled by ChargePools drawing
    // whatever spare supply consumers left unclaimed each frame (so charging can never push
    // total grid outflow past the generators' rated e/s). Bursts spend the pool (see Use).
    private float surge = 0f;
    // Surge fair-share: like EnergyStore's query counting, registering budget checks (one per
    // drawing consumer per frame, via BudgetThisFrame's non-peek path) are counted so each
    // demander is OFFERED surge/N — otherwise first-in-update-order captures the whole pool
    // every frame and later consumers starve.
    private int surgeQueriesThisFrame, surgeQueriesLastFrame;

    [Header("Pylon — cable interaction")]
    [Tooltip("Click-radius (world units) around a connected cable for selecting it to delete.")]
    [SerializeField] private float cableClickRadius = 0.15f;

    private float radius = 4f;
    private int maxCableConnections = 4;

    [SerializeField] private bool multi;
    [SerializeField] private bool longRange;

    public event Action<float> OnUpdate;
    public event Action OnUse;

    private class Connection
    {
        public Building target;
        public Vector2Int claimedCell;
        public LineRenderer lr;
        public PylonCable cable;
        // Pylon-pylon cables conduct BOTH ways (drag direction must not matter): `cable` feeds
        // the target from us, `reverse` feeds us from the target. Both live on the initiating
        // pylon's connection and are ticked by its Update.
        public PylonCable reverse;
    }
    private readonly List<Connection> downstreams = new();
    /// <summary>Upstreams added explicitly via cable (a generator/pylon dragged onto this pylon). Adjacency upstreams come from Power.Sources.</summary>
    private readonly List<IEnergyAccumulator> cableUpstreams = new();
    /// <summary>Pylon-pylon cables OTHER pylons initiated onto us (their Connection.cable). Tracked so
    /// incoming links count against maxCableConnections — they occupy this pylon's capacity too.</summary>
    private readonly List<PylonCable> incomingCables = new();

    /// <summary>Cable links occupying this pylon: outgoing + generator source cables + incoming pylon links.</summary>
    private int ConnectionCount => downstreams.Count + sourceCables.Count + incomingCables.Count;

    /// <summary>Does this pylon pull from <paramref name="src"/> through an explicit cable?
    /// Battery distribution uses this so a pad CABLED to a pylon counts as consumed-from
    /// exactly like a pad the pylon is standing next to.</summary>
    public bool DrawsFromViaCable(IEnergyAccumulator src) => src != null && cableUpstreams.Contains(src);

    // Death leaves the cable graph INTACT but inert: a dead pylon (SwitchMonos(false) disables
    // this Behaviour) reports zero energy/budget and refuses draws, so nothing flows through it —
    // and revival (drone repair -> SwitchMonos(true)) restores the network without rebuilding.
    private bool Dead => !enabled;

    /// <summary>
    /// Source cables this pylon pulls FROM — i.e. a generator the pylon was dragged onto. The
    /// pylon owns the cable visual (lr); the source itself is added to <see cref="cableUpstreams"/>
    /// (no relay wrapper — generators carry their own battery + rate).
    /// </summary>
    private class SourceCable
    {
        public Building target;
        public IEnergyAccumulator source;
        public LineRenderer lr;
    }
    private readonly List<SourceCable> sourceCables = new();

    /// <summary>Cable heat visuals for upstream SOURCE cables, keyed by the source they supply
    /// from — DrainUpstreams/DebitSurge/ChargePools look up where the energy physically came
    /// from and report the flow. Downstream cables report via PylonCable.flowTint instead.</summary>
    private readonly Dictionary<IEnergyAccumulator, CableFlowTint> sourceFlowTints = new();

    // NOTE: pool top-ups (ChargePools) report like any other flow — energy genuinely moves
    // through the cable to fill the buffers, and hiding it made capacitor grids look idle
    // while working. True idle stays grey anyway: full pools charge nothing.
    void ReportSourceFlow(IEnergyAccumulator src, float amount)
    {
        if (amount <= 0f || src == null) return;
        if (sourceFlowTints.TryGetValue(src, out var tint) && tint != null) tint.Report(amount);
    }

    // Cycle guard: while a recursive Energy/Use/DrawRate read is in flight on this pylon,
    // further entries return the zero/skip value so an A→B→A chain can't infinite-loop.
    private bool resolving;

    // Scratch buffers for the deduped upstream walk. Safe to reuse because the resolving
    // guard prevents re-entry on the same pylon mid-iteration.
    private readonly List<IEnergyAccumulator> scratchUpstreams = new();
    private readonly HashSet<IEnergyAccumulator> scratchSeen = new();
    // Capacitor nodes are PARTITIONED out of the upstream walk: they hold no energy, so they
    // must never join the Energy sums or the Use split — they only contribute surge credit.
    private readonly List<CapacitorNode> scratchCapacitors = new();

    private Connectable connectable;

    public override bool ShowsSurgeBar => true;

    bool IsDownstream(IEnergyAccumulator src)
    {
        if (src == null) return false;
        for (int i = 0; i < downstreams.Count; i++)
        {
            if (ReferenceEquals(downstreams[i].target, src)) return true;
        }
        return false;
    }

    /// <summary>
    /// Build the dedup'd upstream view: adjacency sources (skipping other pylons unless
    /// they're cable-supply, and skipping our own downstreams), then explicit cable
    /// upstreams. Result lives in scratchUpstreams; CapacitorNodes (adjacent OR tethered)
    /// are split into scratchCapacitors — surge credit only, never energy.
    /// </summary>
    void GatherUpstreams()
    {
        scratchUpstreams.Clear();
        scratchCapacitors.Clear();
        scratchSeen.Clear();
        var adj = Power.Sources;
        for (int i = 0; i < adj.Count; i++)
        {
            var src = adj[i];
            if (src == null) continue;
            if (src is CapacitorNode cn)
            {
                if (scratchSeen.Add(cn)) scratchCapacitors.Add(cn);
                continue;
            }
            // Adjacent pylons aren't auto-supply — a cable has to say so. Generators/pads
            // are the auto-supply path the user spec'd ("adjacency works for supply").
            if (src is EnergyPylon && !cableUpstreams.Contains(src)) continue;
            if (IsDownstream(src)) continue;
            if (!scratchSeen.Add(src)) continue;
            scratchUpstreams.Add(src);
        }
        for (int i = 0; i < cableUpstreams.Count; i++)
        {
            var src = cableUpstreams[i];
            if (src == null) continue;
            if (src is CapacitorNode tethered)
            {
                if (scratchSeen.Add(tethered)) scratchCapacitors.Add(tethered);
                continue;
            }
            if (!scratchSeen.Add(src)) continue;
            scratchUpstreams.Add(src);
        }
    }

    /// <summary>Σ capacitor credit over the CURRENT scratch partition — call after GatherUpstreams.</summary>
    float CapacitorCreditFromScratch()
    {
        float s = 0f;
        for (int i = 0; i < scratchCapacitors.Count; i++) s += scratchCapacitors[i].SurgeCredit;
        return s;
    }

    /// <summary>Per-demander slice of the combined surge pool (own + capacitors) — assumes the
    /// scratch partition is current. Divided by last frame's demander count so N consumers
    /// share the pool instead of the first-in-update-order taking all of it.</summary>
    float SurgeShareFromScratch()
    {
        return (surge + CapacitorCreditFromScratch()) / Mathf.Max(1, surgeQueriesLastFrame);
    }

    /// <summary>
    /// Burst credit this pylon offers ONE demander on top of the upstream rate budget: its
    /// fair share (surge pool ÷ demanders, capacitor nodes included). While a recursive walk
    /// is in flight (resolving) this returns the own-pool share only, without re-gathering —
    /// the scratch lists are mid-iteration on that path, and every caller min()s this against
    /// the guarded upstream budget anyway.
    /// </summary>
    public float SurgeAvailable
    {
        get
        {
            if (Dead) return 0f;
            if (resolving) return surge / Mathf.Max(1, surgeQueriesLastFrame);
            resolving = true;
            try
            {
                GatherUpstreams();
                return SurgeShareFromScratch();
            }
            finally { resolving = false; }
        }
    }

    public float Energy
    {
        get
        {
            if (resolving || Dead) return 0f;
            resolving = true;
            try
            {
                GatherUpstreams();
                // Surge pools hold real banked energy — they count, so consumers can still
                // spend a charged pool after the generator bank runs dry.
                float sum = surge + CapacitorCreditFromScratch();
                for (int i = 0; i < scratchUpstreams.Count; i++) sum += scratchUpstreams[i].Energy;
                return sum;
            }
            finally { resolving = false; }
        }
    }

    public float MaxEnergy
    {
        get
        {
            if (resolving || Dead) return 0f;
            resolving = true;
            try
            {
                GatherUpstreams();
                float sum = 0f;
                for (int i = 0; i < scratchUpstreams.Count; i++) sum += scratchUpstreams[i].MaxEnergy;
                return sum;
            }
            finally { resolving = false; }
        }
    }

    /// <summary>Per-cable cap. A downstream sees at most perCableCap e/sec from this pylon, regardless of upstream supply.</summary>
    public float DrawRate
    {
        get
        {
            if (resolving || Dead) return 0f;
            resolving = true;
            try
            {
                GatherUpstreams();
                float sum = 0f;
                for (int i = 0; i < scratchUpstreams.Count; i++) sum += scratchUpstreams[i].DrawRate;
                return Mathf.Min(sum, perCableCap);
            }
            finally { resolving = false; }
        }
    }

    /// <summary>Adjacency draws see the surge share too (the pylon now owns a pool), still capped by the per-cable rate.</summary>
    public float MaxDrawThisFrame(float dt)
    {
        if (resolving || Dead) return 0f;
        resolving = true;
        try
        {
            float budget = BudgetThisFrame(dt, peek: false);
            return Mathf.Min(budget, perCableCap * dt + SurgeShareFromScratch());
        }
        finally { resolving = false; }
    }

    /// <summary>Side-effect-free MaxDrawThisFrame (no fair-share query registration) — gauge bars only.</summary>
    public float PeekMaxDraw(float dt)
    {
        if (resolving || Dead) return 0f;
        resolving = true;
        try
        {
            float budget = BudgetThisFrame(dt, peek: true);
            return Mathf.Min(budget, perCableCap * dt + SurgeShareFromScratch());
        }
        finally { resolving = false; }
    }

    /// <summary>
    /// Combined per-frame budget of everything upstream (shares each generator's drawnThisFrame
    /// accounting) plus this pylon's surge credit — PylonCable calls this and applies its own
    /// rate cap on top, so bursts flow through cables while N cables off one generator genuinely
    /// share that generator's rated output.
    /// </summary>
    public float UpstreamMaxDrawThisFrame(float dt)
    {
        if (resolving || Dead) return 0f;
        resolving = true;
        try { return BudgetThisFrame(dt, peek: false); }
        finally { resolving = false; }
    }

    /// <summary>Peek variant of <see cref="UpstreamMaxDrawThisFrame"/> for PylonCable/bar reads.</summary>
    public float UpstreamPeekThisFrame(float dt)
    {
        if (resolving || Dead) return 0f;
        resolving = true;
        try { return BudgetThisFrame(dt, peek: true); }
        finally { resolving = false; }
    }

    /// <summary>
    /// Per-frame budget for ONE demander = Σ upstream frame budgets + its surge SHARE, CLAMPED
    /// by total stored energy (upstream banks + the surge pools, which hold real energy).
    /// The clamp is energy conservation: without it the water-fill would allocate energy that
    /// doesn't exist and Use would partial-drain-and-fail. An orphan pylon (no upstreams)
    /// still serves whatever its pools hold — and nothing more. Non-peek calls REGISTER as a
    /// demander (the surge fair-share count) — one per drawing consumer per frame, cascading
    /// through pylon chains.
    /// </summary>
    private float BudgetThisFrame(float dt, bool peek)
    {
        if (!peek) surgeQueriesThisFrame++;
        GatherUpstreams();
        float budget = 0f, energyAvail = 0f;
        for (int i = 0; i < scratchUpstreams.Count; i++)
        {
            var s = scratchUpstreams[i];
            budget += peek ? s.PeekMaxDraw(dt) : s.MaxDrawThisFrame(dt);
            energyAvail += s.Energy;
        }
        return Mathf.Min(budget + SurgeShareFromScratch(),
                         energyAvail + surge + CapacitorCreditFromScratch());
    }

    /// <summary>Spend surge (REAL banked energy) for the burst portion of a draw: own pool
    /// first, then capacitors (uses the scratch partition — caller must have gathered).
    /// Returns what was actually taken.</summary>
    private float DebitSurge(float amount)
    {
        if (amount <= 0f) return 0f;
        float taken = Mathf.Min(surge, amount);
        surge -= taken;
        for (int i = 0; i < scratchCapacitors.Count && amount - taken > 1e-6f; i++)
        {
            float fromCap = scratchCapacitors[i].DebitSurge(amount - taken);
            ReportSourceFlow(scratchCapacitors[i], fromCap);
            taken += fromCap;
        }
        return taken;
    }

    /// <summary>Equal-split drain of the upstream stores (assumes gathered + resolving held).
    /// Returns the amount actually drained.</summary>
    private float DrainUpstreams(float cost)
    {
        float remaining = cost;
        int safety = 8;
        while (remaining > 1e-5f && safety-- > 0)
        {
            int n = 0;
            for (int i = 0; i < scratchUpstreams.Count; i++) if (scratchUpstreams[i].Energy > 0f) n++;
            if (n == 0) break;
            float share = remaining / n;
            for (int i = 0; i < scratchUpstreams.Count; i++)
            {
                var s = scratchUpstreams[i];
                if (s == null || s.Energy <= 0f) continue;
                float draw = Mathf.Min(s.Energy, share);
                if (s.Use(draw))
                {
                    remaining -= draw;
                    ReportSourceFlow(s, draw);
                }
            }
        }
        return cost - remaining;
    }

    public bool Use(float cost)
    {
        if (cost <= 0f) return true;
        if (resolving || Dead) return false;
        resolving = true;
        try
        {
            GatherUpstreams();
            // The portion of this draw the upstream rate budgets can't cover is a BURST — it is
            // SERVED from the surge pools (real energy banked earlier from spare supply), and
            // only the remainder is forwarded to the upstream stores. Banks therefore never
            // drain faster than their rated e/s: bursts spend what the grid already saved up.
            // Peek for the cover check — bookkeeping, not a fair-share claim.
            float dt = Time.deltaTime, rateCover = 0f;
            for (int i = 0; i < scratchUpstreams.Count; i++) rateCover += scratchUpstreams[i].PeekMaxDraw(dt);
            float fromPools = cost > rateCover ? DebitSurge(cost - rateCover) : 0f;
            float drained = DrainUpstreams(cost - fromPools);
            OnUse?.Invoke();
            OnUpdate?.Invoke(Energy);
            return fromPools + drained >= cost - 1e-5f;
        }
        finally { resolving = false; }
    }

    /// <summary>Pylons don't store. Add is a no-op — energy can't be deposited into a relay.</summary>
    public void Add(float amount) { }

    public override void Start()
    {
        base.Start();
        connectable = GetComponent<Connectable>();
        WireConnectable();
        if(longRange) UpgradeToLong();
        if(multi) UpgradeToMulti();
        if (transform.parent != null && transform.parent.TryGetComponent<SpriteRenderer>(out var psr))
        {
            psr.color = Color.white;
        }
    }

    void WireConnectable()
    {
        if (connectable == null) return;
        connectable.Validate = ValidateTarget;
        connectable.OnConnected = OnConnected;
        connectable.OnClickWithoutDrag = ToggleUI;
        connectable.OnRejected = b => StartCoroutine(FlashRed(b != null ? b.sr : null));
    }

    protected override void BEnable()
    {
        EnergyManager.i?.RegisterSource(this, anchorCell, gridSize);
    }

    protected override void BDisable()
    {
        EnergyManager.i?.UnregisterSource(this, anchorCell, gridSize);
        // Deliberately NOT dropping cables here: BDisable also fires on DEATH, and the network
        // must survive death -> drone-repair revival. The Dead gate keeps a dead pylon from
        // relaying; real teardown happens in OnDestroy (demolition/removal).
    }

    public override void OnDestroy()
    {
        DropAllDownstreams();
        DropAllSourceCables();
        DropAllCableUpstreams();
        base.OnDestroy();
    }

    public override void OnClick()
    {
        // Press fires this; defer the UI toggle until release so Connectable can drag.
        if (!enabled) return;
        connectable?.BeginDrag();
    }

    void ToggleUI()
    {
        if (!canOpen) return;
        if (UIParent.activeInHierarchy) OnClose?.Invoke();
        else OnOpen?.Invoke();
    }

    void Update()
    {
        float dt = Time.deltaTime;
        // Surge pools charge FROM THE GRID: real spare energy — whatever consumers left of the
        // upstream frame budgets — is drained out of the banks and stored in the pools. Total
        // grid outflow (consumers + charging) therefore never exceeds the generators' rated
        // e/s; a fresh or orphaned pylon sits at 0, telling the player to hook up a generator,
        // and a combat-starved grid leaves the pools visibly empty.
        ChargePools(dt);
        // Roll the surge fair-share demander count (mirrors EnergyStore's query counters).
        surgeQueriesLastFrame = surgeQueriesThisFrame;
        surgeQueriesThisFrame = 0;
        // Reset every downstream cable's per-frame draw accounting.
        for (int i = 0; i < downstreams.Count; i++)
        {
            downstreams[i].cable?.TickFrame();
            downstreams[i].reverse?.TickFrame();
        }
        // Cull downstream entries whose target was destroyed (Unity's null sentinel).
        for (int i = downstreams.Count - 1; i >= 0; i--)
        {
            if (downstreams[i].target == null)
            {
                var c = downstreams[i];
                EnergyManager.i?.UnregisterSourceAt(c.cable, c.claimedCell);
                if (c.reverse != null) RemoveCableUpstream(c.reverse);
                if (c.lr != null) Destroy(c.lr.gameObject);
                downstreams.RemoveAt(i);
            }
        }
        // Cull cable upstreams whose underlying pylon is gone (PylonCable wraps a pylon).
        for (int i = cableUpstreams.Count - 1; i >= 0; i--)
        {
            var up = cableUpstreams[i];
            bool dead = up == null
                || (up is UnityEngine.Object obj && obj == null)
                || (up is PylonCable pc && (pc.pylon == null || (UnityEngine.Object)pc.pylon == null));
            if (dead) cableUpstreams.RemoveAt(i);
        }
        // Same cull for the incoming-link ledger (owner pylon destroyed without teardown).
        for (int i = incomingCables.Count - 1; i >= 0; i--)
        {
            var pc = incomingCables[i];
            if (pc == null || pc.pylon == null || (UnityEngine.Object)pc.pylon == null) incomingCables.RemoveAt(i);
        }
        // Cull source cables whose generator was destroyed.
        for (int i = sourceCables.Count - 1; i >= 0; i--)
        {
            var sc = sourceCables[i];
            if (sc.target == null || (sc.source is UnityEngine.Object o && o == null))
            {
                RemoveCableUpstream(sc.source);
                if (sc.source != null) sourceFlowTints.Remove(sc.source);
                if (sc.lr != null) Destroy(sc.lr.gameObject);
                sourceCables.RemoveAt(i);
            }
        }
    }

    bool ValidateTarget(Building target)
    {
        if (target == null || target == this) return false;
        // Pads/hubs ARE cable endpoints: a cabled pad/hub becomes an upstream source and
        // functions exactly like an adjacent one (OnConnected's source branch — same path
        // generators and CapacitorNode tethers take). Consumers (towers, factories) and
        // other pylons are downstream targets.
        // Already connected? Don't allow double cabling (either direction). Since pylon-pylon
        // cables now conduct both ways, a cable the TARGET initiated to us also counts.
        for (int i = 0; i < downstreams.Count; i++)
            if (downstreams[i].target == target) return false;
        for (int i = 0; i < sourceCables.Count; i++)
            if (sourceCables[i].target == target) return false;
        if (target is EnergyPylon tp)
            for (int i = 0; i < tp.downstreams.Count; i++)
                if (tp.downstreams[i].target == this) return false;
        // Range.
        if ((target.transform.position - transform.position).sqrMagnitude > radius * radius) return false;
        // Cap — outgoing cables, generator source-cables AND incoming pylon links all occupy a
        // pylon's connection budget, on BOTH ends of the new cable.
        if (ConnectionCount >= maxCableConnections) return false;
        if (target is EnergyPylon tpCap && tpCap.ConnectionCount >= tpCap.maxCableConnections) return false;
        return true;
    }

    void OnConnected(Building target, LineRenderer lr)
    {
        // Upstream source (a generator dragged onto): pull FROM it. It has its own battery and
        // rate, so add it straight to our cable upstreams — no PylonCable relay/cap wrapper.
        if (target is IEnergyAccumulator src && !(target is EnergyPylon))
        {
            AddCableUpstream(src);
            if (lr != null)
            {
                var sc = new SourceCable { target = target, source = src, lr = lr };
                sourceCables.Add(sc);
                var go = lr.gameObject;
                var link = go.GetComponent<CableLink>();
                if (link == null) link = go.AddComponent<CableLink>();
                link.Init(() => DeleteSourceCable(sc), icon, transform.position, target.transform.position, cableClickRadius);
                // Heat visual. Either endpoint dead (main script disabled, like buildings'
                // ghost state) or destroyed → the cable's glow details dim out.
                var tint = go.AddComponent<CableFlowTint>();
                tint.Init(lr, () => this == null || Dead || sc.target == null || !sc.target.enabled);
                sourceFlowTints[src] = tint;
            }
            return;
        }

        Vector2Int cell = ChooseClaimCell(target);
        var cable = new PylonCable(this, target, perCableCap);
        EnergyManager.i?.RegisterSourceAt(cable, cell);
        var conn = new Connection { target = target, claimedCell = cell, lr = lr, cable = cable };
        downstreams.Add(conn);

        // For pylon-to-pylon, each side sees a cable (not the pylon) in its upstreams — the
        // cable's per-frame rate cap mediates every hop. The link is BIDIRECTIONAL so drag
        // direction doesn't matter: the target also becomes an upstream of ours via `reverse`.
        // The resolving guards keep the two directions from double-counting (a budget/energy
        // query that loops back through the pylon it started from reads 0).
        if (target is EnergyPylon downstreamPylon)
        {
            downstreamPylon.AddCableUpstream(cable);
            downstreamPylon.incomingCables.Add(cable);   // occupies the target's connection cap too
            conn.reverse = new PylonCable(downstreamPylon, this, perCableCap);
            AddCableUpstream(conn.reverse);
        }

        // Make the finished cable clickable → "Delete Cable" tile → retract + tear-down.
        // (Clicking mid-cable, away from the buildings it overlaps, reliably selects the
        // cable; FocusRouter's pick at the endpoints may favour the building, which is fine.)
        if (lr != null)
        {
            var go = lr.gameObject;
            var link = go.GetComponent<CableLink>();
            if (link == null) link = go.AddComponent<CableLink>();
            link.Init(() => DeleteConnection(conn), icon, transform.position, target.transform.position, cableClickRadius);
            // Heat visual: one tint per cable; a bidirectional pylon-pylon link feeds it from
            // both directions so flow either way warms the same line. Either endpoint dead
            // (main script disabled, like buildings' ghost state) or destroyed → dim.
            var tint = go.AddComponent<CableFlowTint>();
            tint.Init(lr, () => this == null || Dead || conn.target == null || !conn.target.enabled);
            cable.flowTint = tint;
            if (conn.reverse != null) conn.reverse.flowTint = tint;
        }
    }

    /// <summary>Tear down a single downstream cable: drop the link, unregister its source cell, then retract the visual into the pylon.</summary>
    void DeleteConnection(Connection c)
    {
        if (c == null || !downstreams.Remove(c)) return;
        EnergyManager.i?.UnregisterSourceAt(c.cable, c.claimedCell);
        if (c.target is EnergyPylon dp) { dp.RemoveCableUpstream(c.cable); dp.incomingCables.Remove(c.cable); }
        if (c.reverse != null) RemoveCableUpstream(c.reverse);

        if (c.lr != null)
        {
            // Freeze interaction (also closes the delete tile via CableLink.OnDisable), then
            // play the retract-into-pylon animation, which destroys the cable when it finishes.
            if (c.lr.TryGetComponent<EdgeCollider2D>(out var ec)) ec.enabled = false;
            if (c.lr.TryGetComponent<CableLink>(out var cl)) cl.enabled = false;
            if (connectable != null) StartCoroutine(connectable.Retract(c.lr, transform.position));
            else Destroy(c.lr.gameObject);
        }
        OnUpdate?.Invoke(Energy);
    }

    void AddCableUpstream(IEnergyAccumulator source)
    {
        if (source == null || cableUpstreams.Contains(source)) return;
        cableUpstreams.Add(source);
        source.OnUpdate += ForwardUpstreamUpdate;
        // The new upstream changes our reported Energy/MaxEnergy/DrawRate — nudge
        // downstream consumers so they re-resolve instead of waiting for the next
        // upstream OnUpdate to cascade through.
        OnUpdate?.Invoke(Energy);
    }

    void RemoveCableUpstream(IEnergyAccumulator source)
    {
        if (!cableUpstreams.Remove(source)) return;
        source.OnUpdate -= ForwardUpstreamUpdate;
        OnUpdate?.Invoke(Energy);
    }

    // Re-entry guard: bidirectional pylon-pylon links make A and B each other's upstreams, so
    // without it A.OnUpdate -> B.Forward -> B.OnUpdate -> A.Forward recurses to a stack overflow.
    private bool forwarding;
    void ForwardUpstreamUpdate(float _)
    {
        if (forwarding) return;
        forwarding = true;
        try { OnUpdate?.Invoke(Energy); }
        finally { forwarding = false; }
    }

    Vector2Int ChooseClaimCell(Building target)
    {
        Vector2Int anchor = target.anchorCell;
        Vector2Int size = target.gridSize;
        if (size.x <= 0 || size.y <= 0) size = Vector2Int.one;
        Vector2 toPylon = (Vector2)(transform.position - target.transform.position);
        float cellSize = GridManager.i != null ? Mathf.Max(GridManager.i.cellSize, 0.01f) : 1f;

        if (Mathf.Abs(toPylon.x) >= Mathf.Abs(toPylon.y))
        {
            int x = toPylon.x > 0 ? anchor.x + size.x : anchor.x - 1;
            int yOffset = Mathf.Clamp(Mathf.RoundToInt(toPylon.y / cellSize) + size.y / 2, 0, Mathf.Max(size.y - 1, 0));
            return new Vector2Int(x, anchor.y + yOffset);
        }
        else
        {
            int y = toPylon.y > 0 ? anchor.y + size.y : anchor.y - 1;
            int xOffset = Mathf.Clamp(Mathf.RoundToInt(toPylon.x / cellSize) + size.x / 2, 0, Mathf.Max(size.x - 1, 0));
            return new Vector2Int(anchor.x + xOffset, y);
        }
    }

    IEnumerator FlashRed(SpriteRenderer s)
    {
        if (s == null) yield break;
        Color initial = s.color;
        const float dur = 0.18f;
        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            s.color = Color.Lerp(initial, Color.red, Mathf.PingPong(t * 2f / dur, 1f));
            yield return null;
        }
        s.color = initial;
    }

    void DropAllDownstreams()
    {
        for (int i = 0; i < downstreams.Count; i++)
        {
            var c = downstreams[i];
            EnergyManager.i?.UnregisterSourceAt(c.cable, c.claimedCell);
            if (c.target is EnergyPylon dp) { dp.RemoveCableUpstream(c.cable); dp.incomingCables.Remove(c.cable); }
            if (c.reverse != null) RemoveCableUpstream(c.reverse);
            if (c.lr != null) Destroy(c.lr.gameObject);
        }
        downstreams.Clear();
    }

    void DropAllCableUpstreams()
    {
        for (int i = cableUpstreams.Count - 1; i >= 0; i--)
        {
            RemoveCableUpstream(cableUpstreams[i]);
        }
    }

    /// <summary>Tear down a single upstream source cable (generator): drop the supply link, then retract the visual into the pylon.</summary>
    void DeleteSourceCable(SourceCable sc)
    {
        if (sc == null || !sourceCables.Remove(sc)) return;
        RemoveCableUpstream(sc.source);
        if (sc.source != null) sourceFlowTints.Remove(sc.source);
        if (sc.lr != null)
        {
            if (sc.lr.TryGetComponent<EdgeCollider2D>(out var ec)) ec.enabled = false;
            if (sc.lr.TryGetComponent<CableLink>(out var cl)) cl.enabled = false;
            if (connectable != null) StartCoroutine(connectable.Retract(sc.lr, transform.position));
            else Destroy(sc.lr.gameObject);
        }
        OnUpdate?.Invoke(Energy);
    }

    void DropAllSourceCables()
    {
        for (int i = 0; i < sourceCables.Count; i++)
        {
            RemoveCableUpstream(sourceCables[i].source);
            if (sourceCables[i].lr != null) Destroy(sourceCables[i].lr.gameObject);
        }
        sourceCables.Clear();
        sourceFlowTints.Clear();
    }

    /// <summary>
    /// Fill the surge pools from SPARE grid supply: peek what the upstream budgets still offer
    /// this frame (consumers' draws already netted out — charging feeds on scraps and never
    /// outranks a consumer), drain that energy for real, and bank it. Own pool first (capped
    /// at surgeRefillRate), then attached capacitors (capped at each node's SurgeChargeRate).
    /// Peek-only, so charging never registers in the fair-share demand counts.
    /// </summary>
    void ChargePools(float dt)
    {
        if (Dead || resolving) return;
        resolving = true;
        try
        {
            GatherUpstreams();
            float wantOwn = Mathf.Min(surgeMaxOwn - surge, surgeRefillRate * dt);
            float wantCaps = 0f;
            for (int i = 0; i < scratchCapacitors.Count; i++)
            {
                var cn = scratchCapacitors[i];
                wantCaps += Mathf.Min(cn.SurgeDeficit, cn.SurgeChargeRate * dt);
            }
            float want = Mathf.Max(0f, wantOwn) + wantCaps;
            if (want <= 1e-6f) return;

            float spare = 0f;
            for (int i = 0; i < scratchUpstreams.Count; i++) spare += scratchUpstreams[i].PeekMaxDraw(dt);
            float charge = Mathf.Min(want, spare);
            if (charge <= 1e-6f) return;

            float banked = DrainUpstreams(charge);
            float toOwn = Mathf.Min(banked, Mathf.Max(0f, wantOwn));
            surge = Mathf.Min(surgeMaxOwn, surge + toOwn);
            float rest = banked - toOwn;
            for (int i = 0; i < scratchCapacitors.Count && rest > 1e-6f; i++)
            {
                float put = scratchCapacitors[i].ChargeSurge(rest);
                ReportSourceFlow(scratchCapacitors[i], put);
                rest -= put;
            }
            // Float dust that fit nowhere tops the own pool rather than evaporating.
            if (rest > 1e-6f) surge = Mathf.Min(surgeMaxOwn, surge + rest);
        }
        finally { resolving = false; }
    }

    /// <summary>Surge-bar readout: current vs max aggregate pool (own + attached capacitors).</summary>
    public void GetSurgeState(out float now, out float max)
    {
        if (Dead || resolving) { now = 0f; max = 0f; return; }
        resolving = true;
        try
        {
            GatherUpstreams();
            now = surge;
            max = surgeMaxOwn;
            for (int i = 0; i < scratchCapacitors.Count; i++)
            {
                now += scratchCapacitors[i].SurgeCredit;
                max += scratchCapacitors[i].SurgeCreditMax;
            }
        }
        finally { resolving = false; }
    }

    // ---- upgrades ----

    void UpgradeToLong()
    {
        radius = 8f;
        maxCableConnections = 4;
    }

    void UpgradeToMulti()
    {
        radius = 4f;
        maxCableConnections = int.MaxValue;
    }
}
