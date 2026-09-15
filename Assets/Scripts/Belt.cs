using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The Belt — a one-cell conveyor tile. Tiles chain by DIRECTION (the way the chevrons point:
/// the tile's up; R spins it): a tile whose front cell holds another tile feeds it, and every
/// run of linked tiles is ONE line (<see cref="BeltLine"/>) from its head(s) — the tiles nothing
/// feeds — to its tail, the tile pointing at empty ground or a building. Each tile carries up
/// to <see cref="chipsPerTile"/> chips.
///
/// Chip gets on ONLY two ways (user rule 2026-09-14): a Collector beside a tile that carries
/// AWAY from it pushes its pile onto the belt, or a chip makes direct CONTACT — its centre lands
/// on a tile (the Hoover's spray, a chip nudged onto it). No suction ring, no drone deliveries:
/// belts are not chip consumers and never appear on the fleet's board. Once aboard a chip
/// belongs to the belt until it leaves the tail — it is OUT of the loose world (OreChip.Store):
/// no drone, no Hoover, no clear-cycle fade can touch it, and no building samples it.
///
/// The line moves as ONE: every chip aboard steps one tile forward together, each step costing
/// <see cref="energyPerStep"/> PER CHIP from the sources touching ANY tile of the line (a chip
/// riding 10 tiles pays 10 steps = 1 energy at the default 0.1; 10 chips over 10 tiles = 10),
/// and the tiles animate only while a step is in progress — a belt with nothing on it stands
/// still. At the tail a chip leaves only while the building at the exit cell is accepting chip
/// right now, and only as many as it currently wants — fed straight in. Otherwise (nothing
/// there, or a full / crushing / unpowered building) the chips stay aboard and stack up at the
/// end (costing nothing while stacked); a line facing into itself piles up on the tile closing the loop.
///
/// Belts are invulnerable (no body: nothing to hit, nothing to path around), cost no upkeep,
/// and come alive per tile from 1 ore. Contrast the Tube: free transport, but fragile and billed
/// per chip per day. Prefab + scene wiring: Tools/Belt Kit.
/// </summary>
public class Belt : Building
{
    [Header("Belt")]
    [Tooltip("The scrolling chevron frames off Belt.png (Tools/Belt Kit); the chevrons run toward the tile's up, its travel direction.")]
    public Sprite[] frames;
    [Tooltip("Frames per second while the line is stepping — frozen otherwise (a belt without chip on it never moves).")]
    public float fps = 12f;
    [Tooltip("Seconds one step takes: every chip on the line slides one tile forward together.")]
    public float stepSeconds = 0.35f;
    [Tooltip("Energy PER CHIP PER TILE MOVED, drawn from the sources touching any tile of the line (10 chips × 10 tiles = 10 energy at 0.1).")]
    public float energyPerStep = 0.1f;
    [Tooltip("Chips one tile carries at once (they ride as a group).")]
    public int chipsPerTile = 5;

    // ------------------------------------------------------------------ registry / lines

    static readonly List<Belt> live = new List<Belt>();
    static readonly Dictionary<Vector2Int, Belt> byCell = new Dictionary<Vector2Int, Belt>();
    static readonly List<BeltLine> lines = new List<BeltLine>();
    static readonly List<BeltLine.Entry> migrate = new List<BeltLine.Entry>();
    static readonly List<Belt> walk = new List<Belt>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()   // no-domain-reload: statics survive play-stop
    {
        live.Clear();
        byCell.Clear();
        lines.Clear();
        migrate.Clear();
        walk.Clear();
    }

    /// <summary>Every live belt line.</summary>
    public static IReadOnlyList<BeltLine> Lines => lines;

    /// <summary>The built belt tile on a world cell, if any.</summary>
    public static Belt At(Vector2Int cell) => byCell.TryGetValue(cell, out var b) && b != null ? b : null;

    [HideInInspector] public Vector2Int cell;
    [System.NonSerialized] public BeltLine line;
    /// <summary>The tile this one feeds (whatever belt sits in its front cell).</summary>
    [System.NonSerialized] public Belt next;
    /// <summary>The tiles feeding this one. Empty = a HEAD.</summary>
    [System.NonSerialized] public readonly List<Belt> prev = new List<Belt>();
    /// <summary>The chips on (or sliding onto) this tile.</summary>
    [System.NonSerialized] public readonly List<BeltLine.Entry> occupants = new List<BeltLine.Entry>();

    public override bool ShowsHealthBar => false;
    public override float PeakEnergyDemand => 0f;   // ONE gauge per line (BeltEnergyBar), not a ThrottleBar per tile

    /// <summary>Travel direction: the tile's up, snapped to an axis.</summary>
    public Vector2Int Dir => BaseCell.Dir(transform.up);
    public Vector2 Centre => BaseCell.Centre(cell);
    /// <summary>The cell this tile pushes into.</summary>
    public Vector2Int FrontCell => cell + Dir;
    /// <summary>A head: nothing feeds this tile.</summary>
    public bool IsHead => line != null && prev.Count == 0;
    /// <summary>Room for another chip right now (the count includes chips sliding onto this tile).</summary>
    public bool HasRoom => builtYet && line != null && occupants.Count < Mathf.Max(0, chipsPerTile);

    /// <summary>Does this tile carry AWAY from a point (a Collector beside it feeds such a tile)?</summary>
    public bool PointsAwayFrom(Vector2 p) => Vector2.Dot((Vector2)Dir, Centre - p) > 0.01f;

    /// <summary>Put a loose chip on this tile (a Collector beside it, a chip landing on it) while it has room.</summary>
    public bool TryLoad(OreChip chip) => line != null && line.TryLoad(chip, this);

    /// <summary>Where the chips of one tile ride: a tight cluster round the tile centre.</summary>
    public static Vector2 SlotOffset(int slot)
    {
        float o = BaseCell.Cs * 0.26f;
        switch (((slot % 5) + 5) % 5)
        {
            case 1: return new Vector2(o, o);
            case 2: return new Vector2(-o, -o);
            case 3: return new Vector2(-o, o);
            case 4: return new Vector2(o, -o);
            default: return Vector2.zero;
        }
    }

    public override void Start()
    {
        base.Start();
        BeltEnergyBar.Attach(this);   // shows only on the line's host tile, sized to the next step's bill
    }

    // ------------------------------------------------------------------ lifecycle

    protected override void BEnable()
    {
        cell = BaseCell.Of(transform.position);
        if (!live.Contains(this)) live.Add(this);
        byCell[cell] = this;
        if (sr != null && frames != null && frames.Length > 0 && sr.sprite == null) sr.sprite = frames[0];
        Rebuild();
    }

    protected override void BDisable()
    {
        live.Remove(this);
        if (byCell.TryGetValue(cell, out var b) && b == this) byCell.Remove(cell);
        ClearEnergyStatusImmediate();
        Rebuild();
    }

    /// <summary>R after placement: the tile now pushes another way — re-link the lines.</summary>
    protected override void OnRotated(Vector2Int oldAnchor, Vector2Int oldSize)
    {
        if (live.Contains(this)) Rebuild();
    }

    void Update()
    {
        if (!builtYet) return;
        if (line != null && line.host == this) line.Tick(Time.deltaTime);
    }

    /// <summary>Show a frame — the line drives every tile of a run in step.</summary>
    public void ShowFrame(int i)
    {
        if (sr == null || frames == null || frames.Length == 0) return;
        int n = frames.Length;
        sr.sprite = frames[((i % n) + n) % n];
    }

    /// <summary>Energy-status bookkeeping for the line, reported by the HOST tile for the whole
    /// run: the "no energy" sign whenever no source touching the line has energy, "insufficient"
    /// while a step's bill outruns the rate (user call 2026-09-14). Judged by the line's POOLED
    /// sources, not this tile's own neighbours.</summary>
    public void ReportDraw(float rate) => ReportEnergyDraw(rate);
    public void ClearDraw() => ClearEnergyStatus();
    /// <summary>The line re-linked and this tile is no longer its host — drop the sign at once.</summary>
    public void ClearDrawImmediate() => ClearEnergyStatusImmediate();
    public override bool ShowsEnergyIcons => true;
    protected override float StatusEnergy => line != null && line.power != null ? line.power.Energy() : Power.Energy;
    protected override float StatusDrawRate => line != null && line.power != null ? line.power.DrawRate() : Power.DrawRate;

    /// <summary>Re-link every live tile: each feeds whatever belt sits in its front cell, and
    /// every run of linked tiles becomes one line (a tile pointing into the side of another run
    /// merges into it; a line facing into itself ends on the tile closing its loop — nothing gets off). Riding chips
    /// go back onto the tile they were on, in whichever line owns it now; a chip whose tile is
    /// gone is set down where it is. Synchronous: cheap, and a demolished tile must let its
    /// chips go at once.</summary>
    static void Rebuild()
    {
        migrate.Clear();
        for (int i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            migrate.AddRange(l.chips);
            l.chips.Clear();
            l.Dispose();
        }
        lines.Clear();

        for (int i = 0; i < live.Count; i++)
        {
            var b = live[i];
            b.prev.Clear();
            b.occupants.Clear();
            b.line = null;
            b.next = null;
        }
        for (int i = 0; i < live.Count; i++)
        {
            var b = live[i];
            var n = At(b.FrontCell);
            if (n == null || n == b) continue;
            b.next = n;
            n.prev.Add(b);
        }
        for (int i = 0; i < live.Count; i++)
        {
            var b = live[i];
            if (b.line != null) continue;
            walk.Clear();
            BeltLine target = null;
            var cur = b;
            while (cur != null)
            {
                if (cur.line != null) { target = cur.line; break; }   // joins a run already grouped
                if (walk.Contains(cur)) break;                          // a closed ring
                walk.Add(cur);
                cur = cur.next;
            }
            if (target == null) { target = new BeltLine(); lines.Add(target); }
            for (int k = 0; k < walk.Count; k++)
            {
                walk[k].line = target;
                target.members.Add(walk[k]);
            }
        }
        walk.Clear();
        for (int i = 0; i < lines.Count; i++) lines[i].Finish();

        for (int i = 0; i < migrate.Count; i++)
        {
            var e = migrate[i];
            if (e.chip == null || e.chip.Absorbing || e.chip.Fading) continue;
            var t = e.tile;
            if (t != null && t.line != null && t.occupants.Count < t.chipsPerTile) t.line.Adopt(e);
            else BeltLine.SetDown(e);
        }
        migrate.Clear();
    }
}
