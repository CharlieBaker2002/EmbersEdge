using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The Tube — a one-cell (0.25) box that keeps and moves ore chips. Boxes placed edge to edge
/// join into ONE shape (TubeCluster): the art auto-connects like a tile set (five authored
/// connection sprites + the straight the kit derives, spun by rotation — see
/// <see cref="Lookup"/>), the capacity adds up (<see cref="chipsPerStore"/> per box) and the
/// chips spread evenly through the whole shape. Chips get in through any box's intake ring
/// (drone dumps, the Hoover's spray), from a Collector beside a box, or off a Belt ending on
/// one; they get out to any hungry chip-eater whose own suction reaches a shelved chip
/// (TubeCluster.TickDeliver), onto a Belt tile touching a box that carries away from it
/// (TubeCluster.TickBeltOut), and to real consumers' drones,
/// which fetch off the shelf like any other source; the player's Hoover, aimed at the shape,
/// sucks chips straight off the shelf (TubeCluster.ReleaseTo).
/// Transport is FREE (no energy per chip in or out); the running cost is the per-chip bill at
/// each clear cycle below. Each box costs 2 ore, has 2 hp and no health bar: a destroyed box
/// drops the chips that sat in it, the rest of the shape re-clusters and re-spreads, and the
/// wreck rebuilds from ore like any building. Contrast the Belt: costs energy per chip per
/// tile but is invulnerable, free to keep, and locks its chips in until the end of the line.
///
/// Power: the cluster shows an era-coloured comet running round the inside of its contour
/// while any box touches a source with energy. At a clear cycle it SHIELDS — material radiance
/// up, a rippling fill over every box — while it pays the bill: <see cref="upkeepPerOre"/> per
/// chip, over <see cref="upkeepTick"/> per chip (100 chips = 2.5 energy in 2.5 s at defaults),
/// a flat 1 energy/s from those sources, due inside that window: a grid that can't keep up (too
/// slow or dry) loses one chip per 0.025 of shortfall when the window ends. The effects phase
/// while the supply falls short. No power = no line, no shield, no protection.
/// Prefab + scene wiring: Tools/Tube Kit.
/// </summary>
public class Tube : Building, IChipConsumer
{
    [Header("Tube")]
    [Tooltip("Connection sprites off Tube.png (Tools/Tube Kit): 0 closed, 1 open up, 2 open up+right, 3 open up+right+down, 4 open all round, 5 open up+down (the straight, derived by the kit).")]
    public Sprite[] frames;
    [Tooltip("Chips one box holds; a connected shape holds this × its boxes.")]
    public int chipsPerStore = 5;

    [Header("Upkeep (the bill at each clear cycle)")]
    [Tooltip("Energy per chip on the shelf, billed at each clear cycle (0.025 × X).")]
    public float upkeepPerOre = 0.025f;
    [Tooltip("Seconds the bill takes per chip (0.025 × X): the draw rate is upkeepPerOre / upkeepTick = a flat 1 energy/s, so 100 chips = 2.5 energy over 2.5 s, due INSIDE that window — a grid that can't keep up (too slow, or dry) loses one chip per upkeepPerOre of shortfall.")]
    public float upkeepTick = 0.025f;
    [Tooltip("The shield LOOK holds at least this long even when a small shelf's bill is settled in a blink.")]
    public float shieldSeconds = 0.6f;

    [Header("Intake")]
    [Tooltip("Settled loose chips this close to any box stream into the shape.")]
    public float intakeRadius = 0.45f;
    [Tooltip("Seconds between intake scans (everything found is taken as fast as the grid pays for it).")]
    public float intakeInterval = 0.12f;
    [Tooltip("Energy drawn from the connected sources for every chip taken in. 0 (user rule 2026-09-14): a tube TRANSPORTS for free — its only running cost is the per-chip bill each clear cycle. A connected source with energy is still needed for the intake to work at all.")]
    public float intakePerChip = 0f;
    [Tooltip("A belt tile touching a box and carrying away from it takes shelved chips lying within this reach of the tile (belts have no suction of their own) — never chips from the far end of the shape.")]
    public float beltReach = 1f;

    [Header("Field layout")]
    [Tooltip("Chips keep this far from every exterior wall of the shape.")]
    public float wallInset = 0.055f;
    [Tooltip("Seconds a chip takes to ease into its new spot after the pattern re-jigs.")]
    public float settleSeconds = 1f;
    [Tooltip("Top speed of a chip gliding through the shape (a chip crossing a big shape after a rebuild must not dawdle).")]
    public float chipMaxSpeed = 3.5f;
    [Tooltip("Scale chips settle at on the shelf (1 = full size, no shrink).")]
    [Range(0.2f, 1f)] public float storedChipScale = 1f;

    [Header("Contour line + shield (authored children — Tools/Tube Kit)")]
    [Tooltip("Authored child LineRenderer the cluster's contour comet runs on.")]
    public LineRenderer contour;
    [Tooltip("Authored child fill that ripples over the box while the shield is up.")]
    public SpriteRenderer shieldFill;
    public float lineWidth = 0.035f;
    [Range(0f, 1f)] public float lineAlpha = 0.9f;
    [Tooltip("World units per second the comet travels.")]
    public float cometSpeed = 1.2f;
    [Tooltip("How far inside the outline the line runs.")]
    public float contourInset = 0.03f;
    [Tooltip("Extra material radiance while shielding (thecolor × (1 + this)).")]
    [Range(0f, 4f)] public float shieldRadiance = 2.5f;
    [Range(0f, 1f)] public float shieldAlpha = 0.55f;

    // ------------------------------------------------------------------ registry / clusters

    static readonly List<Tube> live = new List<Tube>();
    static readonly Dictionary<Vector2Int, Tube> byCell = new Dictionary<Vector2Int, Tube>();
    static readonly List<TubeCluster> clusters = new List<TubeCluster>();
    static readonly List<(TubeCluster.Entry e, TubeCluster from)> migrate = new List<(TubeCluster.Entry, TubeCluster)>();
    static readonly List<Tube> dead = new List<Tube>();   // destroyed boxes awaiting their ore rebuild

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()   // no-domain-reload: statics survive play-stop
    {
        live.Clear();
        byCell.Clear();
        clusters.Clear();
        migrate.Clear();
        dead.Clear();
    }

    /// <summary>Every live store shape (their chips are fetchable by real consumers' drones).</summary>
    public static IReadOnlyList<TubeCluster> Clusters => clusters;

    /// <summary>Destroyed boxes still standing as rebuild ghosts — adjacent shapes donate chip to them.</summary>
    public static IReadOnlyList<Tube> DeadStores => dead;

    [HideInInspector] public Vector2Int cell;
    [HideInInspector] public TubeCluster cluster;

    // ------------------------------------------------------------------ keep priority
    public const int RankLow = 0, RankNormal = 1, RankHigh = 2;
    /// <summary>Keep priority of the SHAPE this box belongs to (all members hold the same value; see
    /// TubeCluster.Rank). Default normal = every shape ranks equally.</summary>
    [HideInInspector] public int rank = RankNormal;

    static readonly int[] Free = new int[4];   // zero cost shared by the setting tiles
    static readonly Color RankActiveCol = new Color(0.30f, 0.85f, 0.45f, 0.85f);
    readonly List<(BaseTile tile, int rank)> rankTiles = new List<(BaseTile, int)>();
    readonly Dictionary<BaseTile, Color> rankBaseCols = new Dictionary<BaseTile, Color>();

    void WireRankTiles()
    {
        AddRank("Store Priority: Low", TPIcons.Weakest, RankLow);
        AddRank("Store Priority: Medium", TPIcons.NoPref, RankNormal);
        AddRank("Store Priority: High", TPIcons.Strongest, RankHigh);
        OnOpen += RefreshRankTiles;   // the shape may have merged/split or been set from another box
        RefreshRankTiles();
    }

    void AddRank(string nam, Sprite spr, int r)
    {
        AddSlot(Free, nam, spr, false, () => SetRank(r));
        BaseTile tile = tiles[^1];
        rankTiles.Add((tile, r));
        rankBaseCols[tile] = tile.init;
    }

    /// <summary>Set the keep priority of the whole shape (every member box, so it survives re-clustering).</summary>
    public void SetRank(int r)
    {
        r = Mathf.Clamp(r, RankLow, RankHigh);
        if (cluster != null) for (int k = 0; k < cluster.members.Count; k++) cluster.members[k].rank = r;
        else rank = r;
        RefreshRankTiles();
    }

    void RefreshRankTiles()
    {
        int cur = cluster != null ? cluster.Rank : rank;
        foreach ((BaseTile tile, int r) in rankTiles)
        {
            if (tile == null) continue;
            Color c = cur == r ? Color.Lerp(rankBaseCols[tile], RankActiveCol, 0.75f) : rankBaseCols[tile];
            tile.init = c;
            tile.background.color = c;
        }
    }

    Material radMat;
    Color baseCol = Color.white;
    float blip;
    static readonly int TheColorId = Shader.PropertyToID("thecolor");

    public override float PeakEnergyDemand => 0f;   // no ThrottleBar per box — ONE cluster gauge on the host (TubeEnergyBar)
    public override bool ShowsHealthBar => false;
    /// <summary>The one wreck you may put back the day it falls (user rule 2026-09-13): the
    /// shelf must stand again before the wave-clear fade eats the chips it dropped.</summary>
    public override bool RebuildsSameDay => true;

    public override void Start()
    {
        base.Start();
        if (rankTiles.Count == 0) WireRankTiles();
        TubeEnergyBar.Attach(this);   // shows only on the shape's host box, sized to the shelf's need
    }

    // ------------------------------------------------------------------ lifecycle

    protected override void BEnable()
    {
        cell = TubeCluster.CellOf(transform.position);
        dead.Remove(this);
        if (!live.Contains(this)) live.Add(this);
        byCell[cell] = this;
        ChipConsumers.Register(this);
        ApplyEraMaterial();
        Rebuild();
    }

    protected override void BDisable()
    {
        live.Remove(this);
        if (byCell.TryGetValue(cell, out var s) && s == this) byCell.Remove(cell);
        ChipConsumers.Unregister(this);
        HideShield();
        if (contour != null) contour.gameObject.SetActive(false);
        ClearEnergyStatusImmediate();
        Rebuild();
    }

    public override void OnDeath()
    {
        base.OnDeath();   // ghost + GhostIntake.BeginRebuild (BDisable ran: our chips dropped, the shape re-clustered)
        if (!dead.Contains(this)) dead.Add(this);
    }

    public override void OnDestroy()
    {
        base.OnDestroy();   // DoBDisable → BDisable → Rebuild drops our chips
        dead.Remove(this);
        if (radMat != null) Destroy(radMat);
    }

    void Update()
    {
        if (!builtYet) return;
        if (cluster != null && cluster.host == this) cluster.Tick(Time.deltaTime);
    }

    /// <summary>Re-cluster every live store: flood-fill 4-neighbours into clusters, refresh each
    /// box's connection sprite/rotation, then carry every shelved chip into whichever new cluster
    /// owns the cell it sits in — a chip whose box died (or whose new shape has no room) drops
    /// out as a loose chip. Synchronous: cheap, and a dying last box must still drop its chips.</summary>
    static void Rebuild()
    {
        migrate.Clear();
        for (int i = 0; i < clusters.Count; i++)
        {
            var cl = clusters[i];
            for (int k = 0; k < cl.chips.Count; k++) migrate.Add((cl.chips[k], cl));
            cl.chips.Clear();
            cl.Dispose();
        }
        clusters.Clear();

        var seen = new HashSet<Tube>();
        var stack = new Stack<Tube>();
        for (int i = 0; i < live.Count; i++)
        {
            var s = live[i];
            if (s == null || seen.Contains(s)) continue;
            var cl = new TubeCluster();
            stack.Push(s);
            seen.Add(s);
            while (stack.Count > 0)
            {
                var m = stack.Pop();
                cl.members.Add(m);
                for (int d = 0; d < 4; d++)
                {
                    var nc = m.cell + (d == 0 ? Vector2Int.up : d == 1 ? Vector2Int.right : d == 2 ? Vector2Int.down : Vector2Int.left);
                    if (byCell.TryGetValue(nc, out var nb) && nb != null && !seen.Contains(nb)) { seen.Add(nb); stack.Push(nb); }
                }
            }
            cl.Finish();
            clusters.Add(cl);
        }
        for (int i = 0; i < live.Count; i++) if (live[i] != null) live[i].RefreshConnections();

        for (int i = 0; i < migrate.Count; i++)
        {
            var (e, from) = migrate[i];
            if (e.chip == null || e.chip.Absorbing || e.chip.Fading) continue;
            var to = ClusterAt(TubeCluster.CellOf(e.pos));
            if (to != null && to.chips.Count < to.Capacity) { e.chip.stored = to; to.Adopt(e); }
            else from.Drop(e);
        }
        migrate.Clear();
    }

    static TubeCluster ClusterAt(Vector2Int c)
        => byCell.TryGetValue(c, out var s) && s != null ? s.cluster : null;

    /// <summary>The live tube box on a world cell, if any.</summary>
    public static Tube At(Vector2Int c) => byCell.TryGetValue(c, out var s) && s != null ? s : null;

    /// <summary>A clear cycle: every shape starts paying for its shelf.</summary>
    public static void BeginShieldAll()
    {
        for (int i = 0; i < clusters.Count; i++) clusters[i].BeginShield();
    }

    // ------------------------------------------------------------------ connection art

    // Open-side sets of the authored tiles (N=1 E=2 S=4 W=8): 0 closed, 1 up, 2 up+right,
    // 3 up+right+down, 4 all, 5 up+down (the straight). Rotating a tile 90° CCW maps
    // N→W, E→N, S→E, W→S; the table holds (tile, z-degrees) for all 16 neighbour masks.
    static readonly (int idx, int rot)[] Lookup = BuildLookup();

    static (int, int)[] BuildLookup()
    {
        int[] open = { 0, 1, 1 | 2, 1 | 2 | 4, 15, 1 | 4 };
        var table = new (int, int)[16];
        var filled = new bool[16];
        for (int t = 0; t < open.Length; t++)
        {
            int mask = open[t];
            for (int r = 0; r < 4; r++)
            {
                if (!filled[mask]) { filled[mask] = true; table[mask] = (t, r * 90); }
                int rot = 0;
                if ((mask & 1) != 0) rot |= 8;   // N → W
                if ((mask & 2) != 0) rot |= 1;   // E → N
                if ((mask & 4) != 0) rot |= 2;   // S → E
                if ((mask & 8) != 0) rot |= 4;   // W → S
                mask = rot;
            }
        }
        return table;
    }

    void RefreshConnections()
    {
        int mask = 0;
        if (byCell.ContainsKey(cell + Vector2Int.up)) mask |= 1;
        if (byCell.ContainsKey(cell + Vector2Int.right)) mask |= 2;
        if (byCell.ContainsKey(cell + Vector2Int.down)) mask |= 4;
        if (byCell.ContainsKey(cell + Vector2Int.left)) mask |= 8;
        var (idx, rot) = Lookup[mask];
        if (sr != null && frames != null && frames.Length > 0)
            sr.sprite = frames[Mathf.Min(idx, frames.Length - 1)];   // strip not yet extended: the straight falls back
        transform.rotation = Quaternion.Euler(0f, 0f, rot);
    }

    // ------------------------------------------------------------------ visuals

    /// <summary>The era's ore material on a runtime copy so the shield can drive `thecolor`
    /// without touching the shared asset (Collector pattern); the era hue is a global, so it is cut once.</summary>
    void ApplyEraMaterial()
    {
        if (sr == null) return;
        if (radMat == null)
        {
            var src = GS.Glow(GlowLevel.Bright);
            if (src == null) return;
            if (radMat != null) Destroy(radMat);
            radMat = new Material(src);
            baseCol = radMat.HasProperty(TheColorId) ? radMat.GetColor(TheColorId) : Color.white;
        }
        sr.sharedMaterial = radMat;
    }

    /// <summary>A chip just went in through this box.</summary>
    public void Blip() => blip = 1f;

    /// <summary>Per-frame from the cluster: radiance and the rippling shield fill.
    /// <paramref name="k"/> is the shield strength already scaled by power/phasing.</summary>
    public void TickVisual(float dt, float k, Color era, Vector2 centroid)
    {
        blip = Mathf.MoveTowards(blip, 0f, dt / 0.35f);
        if (radMat != null && radMat.HasProperty(TheColorId))
            radMat.SetColor(TheColorId, baseCol * (1f + shieldRadiance * k + 0.5f * blip));
        if (shieldFill == null) return;
        float a = shieldAlpha * k;
        if (a <= 0.01f)
        {
            if (shieldFill.gameObject.activeSelf) shieldFill.gameObject.SetActive(false);
            return;
        }
        if (!shieldFill.gameObject.activeSelf) shieldFill.gameObject.SetActive(true);
        float ripple = 0.55f + 0.45f * Mathf.Sin(Time.time * 7f - ((Vector2)transform.position - centroid).magnitude * 5f);
        Color c = era;
        c.a = a * ripple;
        shieldFill.color = c;
    }

    public void HideShield()
    {
        if (shieldFill != null && shieldFill.gameObject.activeSelf) shieldFill.gameObject.SetActive(false);
        if (radMat != null && radMat.HasProperty(TheColorId)) radMat.SetColor(TheColorId, baseCol);
    }

    /// <summary>Energy-status bookkeeping for the cluster's draw, reported by the HOST box for the
    /// whole shape: the "no energy" sign whenever no connected source has energy, "insufficient"
    /// while the shield's rate isn't met (user call 2026-09-14). Judged by the shape's POOLED
    /// sources, not the host's own neighbours.</summary>
    public void ReportDraw(float rate) => ReportEnergyDraw(rate);
    public void ClearDraw() => ClearEnergyStatus();
    /// <summary>The shape re-clustered and this box is no longer its host — drop the sign at once.</summary>
    public void ClearDrawImmediate() => ClearEnergyStatusImmediate();
    public override bool ShowsEnergyIcons => true;
    protected override float StatusEnergy => cluster != null ? cluster.Energy() : Power.Energy;
    protected override float StatusDrawRate => cluster != null && cluster.power != null ? cluster.power.DrawRate() : Power.DrawRate;

    // ------------------------------------------------------------------ IChipConsumer (the shape, via any box)

    public bool ChipIntakeActive => builtYet && enabled && !MarkedForDemolition && cluster != null && cluster.Free > 0 && cluster.Powered && !transform.InDungeon();
    public Vector2 ChipDropPoint => transform.position;
    public float ChipIntakeRadius => intakeRadius;
    /// <summary>Fallback food: the shelf only fills once no keener customer is hungry.</summary>
    public int ChipAppeal(int sizeClass, int element) => 0;
    /// <summary>Gross want in medium-chip space units, net of chips already lying in the ring
    /// (RoomChips) — so a fresh drop stops the next drone over-delivering before the sweep.</summary>
    public float ChipDemandSpace => cluster != null ? cluster.RoomChips * 2f : 0f;
    public int InboundChipSpace
    {
        get => cluster != null ? cluster.inbound : 0;
        set { if (cluster != null) cluster.inbound = Mathf.Max(0, value); }
    }
    public bool AcceptsChip(int sizeClass, int element) => true;
    /// <summary>A Belt ending on this box or a Collector beside it handing a chip straight in
    /// (loose, at rest, where it stands): shelved at once while the shape is powered and has room.</summary>
    public bool TakeDelivered(OreChip chip)
    {
        if (chip == null || chip.Absorbing || chip.Fading || chip.Stored || !ChipIntakeActive || cluster == null) return false;
        cluster.Take(chip);
        return true;
    }
}
