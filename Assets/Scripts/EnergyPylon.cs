using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pure-relay energy pylon. No storage. Forwards Use to its upstream sources (adjacent
/// pads/generators + cable-connected pylons or generators); reports Energy/MaxEnergy by
/// summing the same set.
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
    [SerializeField] private int tier2Bursts = 5;
    [SerializeField] private int tier3Bursts = 8;

    [Header("Pylon — rate cap")]
    [Tooltip("Maximum energy/sec a single cable can transmit. Total throughput = perCableCap × downstream-cable count.")]
    [SerializeField] private float perCableCap = 4f;

    [Header("Pylon — cable interaction")]
    [Tooltip("Click-radius (world units) around a connected cable for selecting it to delete.")]
    [SerializeField] private float cableClickRadius = 0.15f;

    private int level = 1;
    private float radius = 4f;
    private int maxCableConnections = 4;

    public event Action<float> OnUpdate;
    public event Action OnUse;

    private class Connection
    {
        public Building target;
        public Vector2Int claimedCell;
        public LineRenderer lr;
        public PylonCable cable;
    }
    private readonly List<Connection> downstreams = new();
    /// <summary>Upstreams added explicitly via cable (a generator/pylon dragged onto this pylon). Adjacency upstreams come from Power.Sources.</summary>
    private readonly List<IEnergyAccumulator> cableUpstreams = new();

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

    // Cycle guard: while a recursive Energy/Use/DrawRate read is in flight on this pylon,
    // further entries return the zero/skip value so an A→B→A chain can't infinite-loop.
    private bool resolving;

    // Scratch buffers for the deduped upstream walk. Safe to reuse because the resolving
    // guard prevents re-entry on the same pylon mid-iteration.
    private readonly List<IEnergyAccumulator> scratchUpstreams = new();
    private readonly HashSet<IEnergyAccumulator> scratchSeen = new();

    private Connectable connectable;

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
    /// upstreams. Result lives in scratchUpstreams.
    /// </summary>
    void GatherUpstreams()
    {
        scratchUpstreams.Clear();
        scratchSeen.Clear();
        var adj = Power.Sources;
        for (int i = 0; i < adj.Count; i++)
        {
            var src = adj[i];
            if (src == null) continue;
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
            if (!scratchSeen.Add(src)) continue;
            scratchUpstreams.Add(src);
        }
    }

    public float Energy
    {
        get
        {
            if (resolving) return 0f;
            resolving = true;
            try
            {
                GatherUpstreams();
                float sum = 0f;
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
            if (resolving) return 0f;
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
            if (resolving) return 0f;
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

    /// <summary>Adjacency-direct draws see no insta — only cable consumers do (via PylonCable).</summary>
    public float MaxDrawThisFrame(float dt)
    {
        if (resolving) return 0f;
        resolving = true;
        try
        {
            GatherUpstreams();
            float sum = 0f;
            for (int i = 0; i < scratchUpstreams.Count; i++) sum += scratchUpstreams[i].MaxDrawThisFrame(dt);
            return Mathf.Min(sum, perCableCap * dt);
        }
        finally { resolving = false; }
    }

    public bool Use(float cost)
    {
        if (cost <= 0f) return true;
        if (resolving) return false;
        resolving = true;
        try
        {
            GatherUpstreams();
            int safety = 8;
            while (cost > 1e-5f && safety-- > 0)
            {
                int n = 0;
                for (int i = 0; i < scratchUpstreams.Count; i++) if (scratchUpstreams[i].Energy > 0f) n++;
                if (n == 0) break;
                float share = cost / n;
                for (int i = 0; i < scratchUpstreams.Count; i++)
                {
                    var s = scratchUpstreams[i];
                    if (s == null || s.Energy <= 0f) continue;
                    float draw = Mathf.Min(s.Energy, share);
                    if (s.Use(draw)) cost -= draw;
                }
            }
            OnUse?.Invoke();
            OnUpdate?.Invoke(Energy);
            return cost <= 1e-5f;
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
        if (transform.parent != null && transform.parent.TryGetComponent<SpriteRenderer>(out var psr))
        {
            psr.color = Color.white;
        }
        AddUpgradeSlot(
            tier2Cost, "Long-Pylon Upgrade",
            tileSprites != null && tileSprites.Length > 0 && tileSprites[0] != null ? tileSprites[0] : icon,
            true, UpgradeToLong, tier2Bursts, false,
            longPylonSprite != null ? (Action)(() => GS.QuickMorphWithOrbs(gameObject, longPylonSprite, transform.parent)) : null);
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
        DropAllDownstreams();
        DropAllSourceCables();
        DropAllCableUpstreams();
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
        // Tick every downstream cable's instabuffer once per frame.
        for (int i = 0; i < downstreams.Count; i++)
        {
            downstreams[i].cable?.TickInstaBuffer(dt);
        }
        // Cull downstream entries whose target was destroyed (Unity's null sentinel).
        for (int i = downstreams.Count - 1; i >= 0; i--)
        {
            if (downstreams[i].target == null)
            {
                var c = downstreams[i];
                EnergyManager.i?.UnregisterSourceAt(c.cable, c.claimedCell);
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
        // Cull source cables whose generator was destroyed.
        for (int i = sourceCables.Count - 1; i >= 0; i--)
        {
            var sc = sourceCables[i];
            if (sc.target == null || (sc.source is UnityEngine.Object o && o == null))
            {
                RemoveCableUpstream(sc.source);
                if (sc.lr != null) Destroy(sc.lr.gameObject);
                sourceCables.RemoveAt(i);
            }
        }
    }

    bool ValidateTarget(Building target)
    {
        if (target == null || target == this) return false;
        // Pads/hubs aren't cable endpoints. Generators (IEnergyAccumulator, non-pylon, non-pad)
        // ARE allowed — they're treated as an upstream source we pull from (see OnConnected).
        // Consumers (towers, factories) and other pylons are downstream targets.
        if (target is EnergyPad) return false;
        // Already connected? Don't allow double cabling (either direction).
        for (int i = 0; i < downstreams.Count; i++)
            if (downstreams[i].target == target) return false;
        for (int i = 0; i < sourceCables.Count; i++)
            if (sourceCables[i].target == target) return false;
        // Range.
        if ((target.transform.position - transform.position).sqrMagnitude > radius * radius) return false;
        // Cap — downstream consumers and upstream source-cables share the connection budget.
        if (downstreams.Count + sourceCables.Count >= maxCableConnections) return false;
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
            }
            return;
        }

        Vector2Int cell = ChooseClaimCell(target);
        var cable = new PylonCable(this, target, perCableCap, 4f);
        EnergyManager.i?.RegisterSourceAt(cable, cell);
        var conn = new Connection { target = target, claimedCell = cell, lr = lr, cable = cable };
        downstreams.Add(conn);

        // For pylon-to-pylon, the downstream sees the cable (not the pylon) in its upstreams.
        // That way the cable's per-frame cap & insta mediate every hop.
        if (target is EnergyPylon downstreamPylon)
        {
            downstreamPylon.AddCableUpstream(cable);
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
        }
    }

    /// <summary>Tear down a single downstream cable: drop the link, unregister its source cell, then retract the visual into the pylon.</summary>
    void DeleteConnection(Connection c)
    {
        if (c == null || !downstreams.Remove(c)) return;
        EnergyManager.i?.UnregisterSourceAt(c.cable, c.claimedCell);
        if (c.target is EnergyPylon dp) dp.RemoveCableUpstream(c.cable);

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

    void ForwardUpstreamUpdate(float _) => OnUpdate?.Invoke(Energy);

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
            if (c.target is EnergyPylon dp) dp.RemoveCableUpstream(c.cable);
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
    }

    // ---- upgrades ----

    void UpgradeToLong()
    {
        level = 2;
        radius = 8f;
        maxCableConnections = 4;
        if (longPylonSprite != null) sr.sprite = longPylonSprite;
        ChangeFootprint(new Vector2(1.5f, 1.5f));
        AddUpgradeSlot(
            tier3Cost, "Multi-Pylon Upgrade",
            tileSprites != null && tileSprites.Length > 1 && tileSprites[1] != null ? tileSprites[1] : icon,
            true, UpgradeToMulti, tier3Bursts, false,
            multiPylonSprite != null ? (Action)(() => GS.QuickMorphWithOrbs(gameObject, multiPylonSprite, transform.parent)) : null);
    }

    void UpgradeToMulti()
    {
        level = 3;
        radius = 4f;
        maxCableConnections = int.MaxValue;
        if (multiPylonSprite != null) sr.sprite = multiPylonSprite;
        ChangeFootprint(new Vector2(2f, 2f));
    }

    void ChangeFootprint(Vector2 worldSize)
    {
        if (GridManager.i == null) { size = worldSize; return; }
        EnergyManager.i?.UnregisterSource(this, anchorCell, gridSize);
        GridManager.i.SetArea(anchorCell, gridSize, false);
        size = worldSize;
        int cellsX = Mathf.Max(1, Mathf.RoundToInt(size.x / GridManager.i.cellSize));
        int cellsY = Mathf.Max(1, Mathf.RoundToInt(size.y / GridManager.i.cellSize));
        gridSize = new Vector2Int(cellsX, cellsY);
        anchorCell = GridManager.i.WorldToGrid(transform.position) - new Vector2Int(cellsX / 2, cellsY / 2);
        GridManager.i.SetArea(anchorCell, gridSize, true);
        EnergyManager.i?.RegisterSource(this, anchorCell, gridSize);
    }
}
