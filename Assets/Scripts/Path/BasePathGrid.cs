using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The pathfinding walkability registry for the BASE. Deliberately independent of
/// GridManager.occupied — that flags PLACEMENT (and stays set for dead "ghost" buildings awaiting
/// repair, which block nothing physically). This map tracks what actually has a live collider:
///
///  - SOLID cells: live non-wall building footprints — impassable, routed around (but the buildings
///    themselves are seeded as targets, so enemies still path INTO contact with them).
///  - WALL cells: chewable obstacles (Wall buildings, EnergyWall spans) keyed to the LifeScript
///    whose HP prices crossing them. Never seeded as attractors; become targets via gate lookup.
///
/// Keys are WORLD-QUANTIZED cells — floor(world / cellSize), independent of GridManager's origin —
/// so the build grid growing (EnsureGridFitsMap, which re-anchors its origin) never invalidates a
/// registration.
/// </summary>
public static class BaseBlockMap
{
    static readonly Dictionary<Vector2Int, int> solid = new Dictionary<Vector2Int, int>();          // refcounted
    static readonly Dictionary<Vector2Int, ushort> wallIdOfCell = new Dictionary<Vector2Int, ushort>();
    static readonly List<LifeScript> wallOwners = new List<LifeScript>();                            // slot -> ls (null = free)
    static readonly List<float> wallMults = new List<float>();                                       // slot -> chew-cost multiplier
    static readonly Dictionary<LifeScript, (int id, List<Vector2Int> cells, Transform tf)> byWall =
        new Dictionary<LifeScript, (int, List<Vector2Int>, Transform)>();
    // wall lookup by the TRANSFORM handed out as a target (gate/acquisition) — so per-frame callers
    // resolve "is my target a wall, and where do I aim on it" without any GetComponent
    static readonly Dictionary<Transform, LifeScript> wallByTf = new Dictionary<Transform, LifeScript>();

    /// <summary>Bumped on every change — a cheap staleness hint for anything caching cost layers.</summary>
    public static int Version { get; private set; }

    /// <summary>
    /// Fresh-session wipe. Domain reload is DISABLED in this project (Enter Play Mode Options), so
    /// these statics survive play sessions — and an editor play-stop leaks every registration
    /// (Application.quitting sets GS.qutting, which makes Building.OnDestroy skip unregistering;
    /// RefreshManager's big static-reset list never covered this registry). The leaked cells then
    /// SHADOW the next session's walls at the same world cells (first-claimer rule) while their
    /// owners are destroyed — every shadowed wall reads as OPEN GROUND for the whole session, with
    /// re-registration powerless. Runs before any Awake, every play, domain reload or not.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        solid.Clear();
        wallIdOfCell.Clear();
        wallOwners.Clear();
        wallMults.Clear();
        byWall.Clear();
        wallByTf.Clear();
        ClearDirty();
        Version++;
        BasePathGrid.InvalidateSessionCaches();
    }

    // ------------------------------------------------------------------ dirty-cell tracking
    // Every mutator records which cells it touched, so the squeeze cache can recompute just that
    // neighbourhood instead of the whole rect on every Version bump (a wall dying mid-wave used
    // to cost a full ~58k-cell rebuild). Overflow (or an un-enumerable change) forces a full one.

    const int DirtyCap = 4096;
    static readonly List<Vector2Int> dirtyCells = new List<Vector2Int>();
    static bool dirtyOverflow;

    public static List<Vector2Int> DirtyCells => dirtyCells;
    public static bool DirtyOverflow => dirtyOverflow;

    public static void ClearDirty()
    {
        dirtyCells.Clear();
        dirtyOverflow = false;
    }

    static void MarkDirty(Vector2Int c)
    {
        if (dirtyCells.Count < DirtyCap) dirtyCells.Add(c);
        else dirtyOverflow = true;
    }

    /// <summary>Stamp the raw registry into full-rect arrays (arrays pre-cleared by the caller) —
    /// iterates the sparse registrations, not every cell.</summary>
    public static void RasterStampAll(RectInt rect, bool[] solidMaskP, ushort[] rawSlotP)
    {
        foreach (var c in solid.Keys)
        {
            int x = c.x - rect.xMin, y = c.y - rect.yMin;
            if (x < 0 || y < 0 || x >= rect.width || y >= rect.height) continue;
            solidMaskP[x + y * rect.width] = true;
        }
        foreach (var kv in wallIdOfCell)
        {
            int x = kv.Key.x - rect.xMin, y = kv.Key.y - rect.yMin;
            if (x < 0 || y < 0 || x >= rect.width || y >= rect.height) continue;
            rawSlotP[x + y * rect.width] = kv.Value;
        }
    }

    /// <summary>Refresh a sub-region of the raster by probing the registry per cell (regions are
    /// small — a wall's neighbourhood — so per-cell probes beat a full registry sweep).</summary>
    public static void RasterProbe(RectInt rect, RectInt regionLocal, bool[] solidMaskP, ushort[] rawSlotP, ushort noneSlot)
    {
        for (int y = regionLocal.yMin; y < regionLocal.yMax; y++)
            for (int x = regionLocal.xMin; x < regionLocal.xMax; x++)
            {
                var cell = new Vector2Int(rect.xMin + x, rect.yMin + y);
                int idx = x + y * rect.width;
                solidMaskP[idx] = solid.ContainsKey(cell);
                rawSlotP[idx] = wallIdOfCell.TryGetValue(cell, out ushort s) ? s : noneSlot;
            }
    }

    public static IReadOnlyList<LifeScript> WallOwners => wallOwners;

    public static float CellSize => GridManager.i != null ? GridManager.i.cellSize : 1f;

    /// <summary>World position → world-quantized cell (the registry's key space).</summary>
    public static Vector2Int Cell(Vector2 world)
    {
        float cs = CellSize;
        return new Vector2Int(Mathf.FloorToInt(world.x / cs), Mathf.FloorToInt(world.y / cs));
    }

    public static bool HasSolid(Vector2Int cell) => solid.ContainsKey(cell);

    /// <summary>The wall on this cell, ONLY while it's actually standing (registered, not destroyed,
    /// not dead-awaiting-repair — a ghost wall blocks nothing). The single wall lookup the grid
    /// views use, so dead walls read as open ground everywhere at once.</summary>
    public static bool TryGetLiveWall(Vector2Int cell, out LifeScript ls, out int id)
        => TryGetLiveWall(cell, out ls, out id, out _);

    /// <summary>
    /// The point to LOOK AT / SHOOT AT on a registered wall: its nearest registered cell centre to
    /// the asker. A wall's transform can sit far from its span (EnergyWall parents to its tower),
    /// so aiming at transform.position makes LOS/attacks fail — aim here instead. Transform
    /// overload = a target handed out by the gate/acquisition; false = not a (live) wall.
    /// </summary>
    public static bool TryGetNearestWallPoint(Transform targetTf, Vector2 from, out Vector2 point)
    {
        point = from;
        if (targetTf == null || !wallByTf.TryGetValue(targetTf, out LifeScript ls)) return false;
        return TryGetNearestWallPoint(ls, from, out point);
    }

    public static bool TryGetNearestWallPoint(LifeScript ls, Vector2 from, out Vector2 point)
    {
        point = from;
        if (ls == null || ls.hasDied || !byWall.TryGetValue(ls, out var entry) || entry.cells.Count == 0) return false;
        float cs = CellSize;
        float best = float.MaxValue;
        for (int k = 0; k < entry.cells.Count; k++)
        {
            var p = new Vector2((entry.cells[k].x + 0.5f) * cs, (entry.cells[k].y + 0.5f) * cs);
            float d2 = (p - from).sqrMagnitude;
            if (d2 < best) { best = d2; point = p; }
        }
        return true;
    }

    /// <summary>A registered wall's claimed cells + the transform handed out as its target identity
    /// — lets "the walls themselves are the destination" fields seed every span cell. The list is
    /// the live registry entry: read it, don't hold it. False = not registered.</summary>
    public static bool TryGetWallCells(LifeScript ls, out List<Vector2Int> cells, out Transform tf)
    {
        cells = null; tf = null;
        if (ls is null || !byWall.TryGetValue(ls, out var entry)) return false;
        cells = entry.cells;
        tf = entry.tf;
        return true;
    }

    public static bool TryGetLiveWall(Vector2Int cell, out LifeScript ls, out int id, out float costMult)
    {
        ls = null; id = -1; costMult = 1f;
        if (!wallIdOfCell.TryGetValue(cell, out ushort slot)) return false;
        ls = slot < wallOwners.Count ? wallOwners[slot] : null;
        if (ls == null || ls.hasDied) { ls = null; return false; }   // Unity-null (destroyed) or dead
        id = slot;
        costMult = slot < wallMults.Count ? wallMults[slot] : 1f;
        return true;
    }

    /// <summary>A LIVE wall by its slot id (as handed out by TryGetLiveWall/gates) — lets crack
    /// cells "filled" by an adjacent wall price and identify themselves as that wall. False =
    /// slot free, destroyed, or dead.</summary>
    public static bool TryGetWallBySlot(int slot, out LifeScript ls, out float costMult)
    {
        ls = null; costMult = 1f;
        if (slot < 0 || slot >= wallOwners.Count) return false;
        ls = wallOwners[slot];
        if (ls == null || ls.hasDied) { ls = null; return false; }
        costMult = slot < wallMults.Count ? wallMults[slot] : 1f;
        return true;
    }

    // ------------------------------------------------------------------ solid (non-wall buildings)

    public static void RegisterSolid(Vector2Int anchorCell, Vector2Int sizeCells)
    {
        for (int x = 0; x < sizeCells.x; x++)
            for (int y = 0; y < sizeCells.y; y++)
            {
                var c = new Vector2Int(anchorCell.x + x, anchorCell.y + y);
                solid[c] = solid.TryGetValue(c, out int n) ? n + 1 : 1;
                MarkDirty(c);
            }
        Version++;
    }

    public static void UnregisterSolid(Vector2Int anchorCell, Vector2Int sizeCells)
    {
        for (int x = 0; x < sizeCells.x; x++)
            for (int y = 0; y < sizeCells.y; y++)
            {
                var c = new Vector2Int(anchorCell.x + x, anchorCell.y + y);
                if (!solid.TryGetValue(c, out int n)) continue;
                if (n <= 1) solid.Remove(c);
                else solid[c] = n - 1;
                MarkDirty(c);
            }
        Version++;
    }

    // ------------------------------------------------------------------ walls (chewable)

    /// <summary>Register a rectangular wall footprint (Wall buildings). Re-registering the same
    /// LifeScript replaces its previous cells. costMult scales the HP→chew-cost conversion —
    /// shared-bar walls (one LifeScript across a long span, broken anywhere = broken everywhere)
    /// should register well below 1.</summary>
    public static void RegisterWallRect(LifeScript ls, Vector2Int anchorCell, Vector2Int sizeCells, float costMult = 1f)
    {
        if (ls == null) return;
        int id = ClaimSlot(ls, costMult, out List<Vector2Int> cells);
        for (int x = 0; x < sizeCells.x; x++)
            for (int y = 0; y < sizeCells.y; y++)
                ClaimWallCell(new Vector2Int(anchorCell.x + x, anchorCell.y + y), (ushort)id, cells);
        Version++;
    }

    /// <summary>Register a polyline wall span (the EnergyWall's bezier samples) — every cell the
    /// line passes through becomes a chewable cell of this LifeScript. Replaces previous cells.
    /// inflateCells thickens the raster by that many rings (the physical wall has real bulk +
    /// rounded end caps; the bare centerline lets routes thread cells the body can't occupy).</summary>
    public static void RegisterWallPath(LifeScript ls, IList<Vector3> points, float costMult = 1f, int inflateCells = 0)
    {
        if (ls == null || points == null || points.Count < 2) return;
        int id = ClaimSlot(ls, costMult, out List<Vector2Int> cells);
        for (int s = 0; s < points.Count - 1; s++)
            RasterSegment(points[s], points[s + 1], (ushort)id, cells);
        InflateCells(cells, (ushort)id, inflateCells);
        Version++;
    }

    // Thicken a claimed cell set by r rings (Chebyshev — also extends past the span's ends, like
    // the physical end caps). Skips cells under solid building footprints: those must keep reading
    // BLOCKED, never chew-priced.
    static readonly List<Vector2Int> inflateScratch = new List<Vector2Int>();
    static void InflateCells(List<Vector2Int> cells, ushort id, int r)
    {
        if (r <= 0) return;
        inflateScratch.Clear();
        inflateScratch.AddRange(cells);
        foreach (var c in inflateScratch)
            for (int dx = -r; dx <= r; dx++)
                for (int dy = -r; dy <= r; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var n = new Vector2Int(c.x + dx, c.y + dy);
                    if (!HasSolid(n)) ClaimWallCell(n, id, cells);
                }
    }

    /// <summary>Register a straight wall segment. Replaces previous cells of this LifeScript.</summary>
    public static void RegisterWallSegment(LifeScript ls, Vector2 a, Vector2 b, float costMult = 1f)
    {
        if (ls == null) return;
        int id = ClaimSlot(ls, costMult, out List<Vector2Int> cells);
        RasterSegment(a, b, (ushort)id, cells);
        Version++;
    }

    public static void UnregisterWall(LifeScript ls)
    {
        // `is null`, not Unity's ==: a just-destroyed LifeScript must still unregister its cells
        if (ls is null || !byWall.TryGetValue(ls, out var entry)) return;
        foreach (var c in entry.cells)
        {
            if (wallIdOfCell.TryGetValue(c, out ushort slot) && slot == entry.id) wallIdOfCell.Remove(c);
            MarkDirty(c);
        }
        wallOwners[entry.id] = null;   // slot free for reuse
        if (entry.tf is object) wallByTf.Remove(entry.tf);   // stored ref works even if destroyed
        byWall.Remove(ls);
        Version++;
    }

    // fresh (or replaced) slot for a wall's LifeScript + its cell list
    static int ClaimSlot(LifeScript ls, float costMult, out List<Vector2Int> cells)
    {
        while (wallMults.Count < wallOwners.Count) wallMults.Add(1f);
        if (byWall.TryGetValue(ls, out var existing))
        {
            // replacing: drop old cells, keep the slot
            foreach (var c in existing.cells)
            {
                if (wallIdOfCell.TryGetValue(c, out ushort slot) && slot == existing.id) wallIdOfCell.Remove(c);
                MarkDirty(c);
            }
            existing.cells.Clear();
            cells = existing.cells;
            wallMults[existing.id] = costMult;
            return existing.id;
        }
        int id = -1;
        for (int k = 0; k < wallOwners.Count; k++)
            if (wallOwners[k] == null) { id = k; break; }   // Unity ==: catches DESTROYED owners too — List.IndexOf(null)'s comparer never does
        if (id < 0) { id = wallOwners.Count; wallOwners.Add(ls); wallMults.Add(costMult); }
        else
        {
            if (wallOwners[id] is object) PurgeSlot(id);   // destroyed-but-referenced: release its zombie cells/bookkeeping before reuse
            wallOwners[id] = ls; wallMults[id] = costMult;
        }
        cells = new List<Vector2Int>();
        byWall[ls] = (id, cells, ls.transform);
        wallByTf[ls.transform] = ls;
        return id;
    }

    // A slot whose owner was DESTROYED without unregistering (torn-down session, missed teardown)
    // still holds cells and byWall/byTf entries — release them through the normal unregister path
    // (a destroyed reference still hashes and compares fine as a dictionary key) so the slot is
    // genuinely clean for its new owner.
    static void PurgeSlot(int id)
    {
        LifeScript zombie = null;
        foreach (var kv in byWall)
            if (kv.Value.id == id) { zombie = kv.Key; break; }
        if (zombie is object) UnregisterWall(zombie);
    }

    static void ClaimWallCell(Vector2Int c, ushort id, List<Vector2Int> cells)
    {
        // first LIVE wall to claim a contested cell keeps it — but a DESTROYED incumbent (a leaked
        // registration from a torn-down session) must not shadow real walls: its cells read as
        // open ground and re-registration would skip them forever. Stealing is bookkeeping-safe:
        // a later unregister of the incumbent only removes cells still keyed to its OWN slot.
        if (wallIdOfCell.TryGetValue(c, out ushort cur))
        {
            if (cur == id) return;   // already ours (segment raster + inflate can revisit a cell)
            var incumbent = cur < wallOwners.Count ? wallOwners[cur] : null;
            if (incumbent != null) return;   // Unity ==: a LIVE incumbent keeps the cell
        }
        wallIdOfCell[c] = id;
        cells.Add(c);
        MarkDirty(c);
    }

    // ------------------------------------------------------------------ phase-walker line test

    /// <summary>
    /// Does the straight segment a→b cross any LIVE wall whose body is IMMATERIAL (the Force
    /// Field's EnergyWall)? Phase-walkers (immaterial enemies) pass through every material wall
    /// and building, so this is the only registered geometry that can refuse their straight line —
    /// immaterial-vs-immaterial collides like matter (ActionScript's wall branch). Immateriality
    /// is resolved once per wall slot (same-GO ActionScript) and cached against Version.
    /// </summary>
    public static bool SegmentCrossesImmaterialWall(Vector2 a, Vector2 b)
    {
        float cs = CellSize;
        var c = Cell(a);
        var cEnd = Cell(b);
        if (ImmaterialWallAt(c)) return true;
        if (c == cEnd) return false;
        Vector2 d = b - a;
        int stepX = d.x > 0f ? 1 : -1;
        int stepY = d.y > 0f ? 1 : -1;
        Vector2 cellMin = new Vector2(c.x * cs, c.y * cs);
        float tMaxX = d.x != 0f ? (((d.x > 0f ? cellMin.x + cs : cellMin.x) - a.x) / d.x) : float.PositiveInfinity;
        float tMaxY = d.y != 0f ? (((d.y > 0f ? cellMin.y + cs : cellMin.y) - a.y) / d.y) : float.PositiveInfinity;
        float tDeltaX = d.x != 0f ? cs / Mathf.Abs(d.x) : float.PositiveInfinity;
        float tDeltaY = d.y != 0f ? cs / Mathf.Abs(d.y) : float.PositiveInfinity;
        int guard = 4096;
        while (c != cEnd && guard-- > 0)
        {
            if (tMaxX < tMaxY) { tMaxX += tDeltaX; c.x += stepX; }
            else               { tMaxY += tDeltaY; c.y += stepY; }
            if (ImmaterialWallAt(c)) return true;
        }
        return false;
    }

    static readonly Dictionary<int, bool> immaterialBySlot = new Dictionary<int, bool>();
    static int immaterialVersion = int.MinValue;

    static bool ImmaterialWallAt(Vector2Int cell)
    {
        if (!wallIdOfCell.TryGetValue(cell, out ushort slot)) return false;
        var ls = slot < wallOwners.Count ? wallOwners[slot] : null;
        if (ls == null || ls.hasDied) return false;
        if (immaterialVersion != Version) { immaterialBySlot.Clear(); immaterialVersion = Version; }
        if (!immaterialBySlot.TryGetValue(slot, out bool im))
        {
            im = ls.TryGetComponent<ActionScript>(out var body) && body.immaterial;
            immaterialBySlot.Add(slot, im);
        }
        return im;
    }

    // supercover DDA over the world-quantized lattice — claims every cell the segment touches
    static void RasterSegment(Vector2 a, Vector2 b, ushort id, List<Vector2Int> cells)
    {
        float cs = CellSize;
        var c = Cell(a);
        var cEnd = Cell(b);
        ClaimWallCell(c, id, cells);
        if (c == cEnd) return;

        Vector2 d = b - a;
        int stepX = d.x > 0f ? 1 : -1;
        int stepY = d.y > 0f ? 1 : -1;
        Vector2 cellMin = new Vector2(c.x * cs, c.y * cs);
        float tMaxX = d.x != 0f ? (((d.x > 0f ? cellMin.x + cs : cellMin.x) - a.x) / d.x) : float.PositiveInfinity;
        float tMaxY = d.y != 0f ? (((d.y > 0f ? cellMin.y + cs : cellMin.y) - a.y) / d.y) : float.PositiveInfinity;
        float tDeltaX = d.x != 0f ? cs / Mathf.Abs(d.x) : float.PositiveInfinity;
        float tDeltaY = d.y != 0f ? cs / Mathf.Abs(d.y) : float.PositiveInfinity;

        int guard = 4096;
        while (c != cEnd && guard-- > 0)
        {
            if (tMaxX < tMaxY) { tMaxX += tDeltaX; c.x += stepX; }
            else               { tMaxY += tDeltaY; c.y += stepY; }
            ClaimWallCell(c, id, cells);
        }
    }
}

/// <summary>
/// The base's building grid as an <see cref="IPathGrid"/>, in two flavours sharing one registry:
/// <see cref="chewable"/> (W — wall cells crossable at an HP-priced cost; enemies' "through" view)
/// and <see cref="blocked"/> (D — walls impassable; the detour view, allies' view, and the LOS
/// grid, since walls block sight either way). Cell lattice = world-quantized GridManager cells.
/// </summary>
public sealed class BasePathGrid : IPathGrid
{
    public static readonly BasePathGrid chewable = new BasePathGrid(true);
    public static readonly BasePathGrid blocked = new BasePathGrid(false);

    /// <summary>Chew cost per HP, in walking-cell-equivalents: crossing a wall with hp h costs
    /// 1 + ceil(h * costPerHp) cells. Tune against real DealBuildingDamage chew times.</summary>
    public static float costPerHp = 0.5f;

    /// <summary>
    /// Per-cell cost of ground OUTSIDE the map boundary. The grid rect is the wavy map polygon's
    /// AABB (plus margin), so the ring between boundary and rect would otherwise read as open —
    /// a phantom corridor around every wall line, and around the map itself. Heavy-but-finite:
    /// spawning enemies out in the pull band still get field answers (their cells are reached,
    /// expensively), and the downhill direction naturally leads them in onto real ground.
    /// </summary>
    public static int offMapCost = 16;

    /// <summary>
    /// Extra cost of walking a cell that TOUCHES a wall/solid/filled cell (ring 1 of the distance
    /// transform). Together with crack filling this is obstacle padding (~0.25u) quantized to the
    /// grid: routes prefer one cell of daylight rounding walls, but hugging stays possible at a
    /// small premium (chew faces, doorways, targets parked against buildings). DELIBERATELY not
    /// the dungeon's size-driven graded rings (<see cref="PathPadding"/> / MineGridAdapter): the
    /// base's chew-vs-detour economy is tuned around real obstacle costs, and deep rings taxed
    /// wall CROSSINGS (both aprons) into detours — the base stays on this original single ring,
    /// decoupled from body-size tiers, so tier changes never rebuild base caches or fields.
    /// </summary>
    public static int wallPaddingCost = 1;

    /// <summary>
    /// APERTURE PRICING — the anti-funnel rule, calibrated in WORLD UNITS (the path grid is far
    /// finer than a body — cellSize ~0.25). Every open cell measures the clear width of the
    /// corridor it sits in along FOUR axes (horizontal, vertical, both diagonals — so zigzag
    /// seams between diagonally offset walls are caught too):
    ///  - narrower than minPassableWidth → FILLED: no body fits, so for pathing it IS the flanking
    ///    wall (chew price + gate identity; solid-flanked = simply impassable) — never walked into.
    ///  - narrower than tightWidth → narrowGapCost2 per cell: a one-body squeeze — most units keep
    ///    chewing the edges (the through-route crosses a wall instead, stamping the gate) and only
    ///    a trickle files through.
    ///  - narrower than snugWidth → narrowGapCost3 per cell: a small toll; flow begins.
    ///  - at/above snugWidth → free.
    /// Premiums apply in BOTH views so they trade in the same economy as chew costs and detour
    /// lengths. minPassableWidth references the STANDARD enemy body (~0.8 diameter) — fields are
    /// shared, one reference body for everyone. narrowGapCost2 competes directly with chew cost
    /// (1 + hp × costPerHp): raise it to keep enemies chewing at even higher wall HP rather than
    /// queueing at a one-body squeeze.
    /// </summary>
    public static float minPassableWidth = 0.85f;
    public static float tightWidth = 1.2f;
    public static float snugWidth = 1.6f;
    public static int narrowGapCost2 = 24;
    public static int narrowGapCost3 = 4;

    /// <summary>
    /// Chew-cost multiplier for CAVITY-EDGE walls — wall cells flanking an existing gap (a filled
    /// width-1 crack or a priced narrow corridor). At 0.5 a breach edge reads half as expensive as
    /// virgin wall, so through-routes cross there, gates stamp there, and enemies WIDEN existing
    /// cavities (2× preference) instead of starting fresh holes. The preference naturally switches
    /// off once the gap reaches free-flow width (4+), when its cells stop being priced as narrow.
    /// </summary>
    public static float breachEdgeChewMult = 0.5f;

    readonly bool chew;
    BasePathGrid(bool chewP) { chew = chewP; }

    /// <summary>The base grid governs queries once GridManager has sized itself to the map (it
    /// pre-warms shortly after scene start). Before that, callers fall back to no-walls behavior.</summary>
    public static bool Ready => GridManager.i != null && GridManager.i.Built;

    bool IPathGrid.Ready => Ready;

    public RectInt CellRect
    {
        get
        {
            var gm = GridManager.i;
            float cs = gm.cellSize;
            // origin is lattice-snapped by EnsureGridFitsMap, so this is exact
            return new RectInt(Mathf.FloorToInt(gm.origin.x / cs), Mathf.FloorToInt(gm.origin.y / cs),
                gm.width, gm.height);
        }
    }

    public float CellSize => GridManager.i.cellSize;

    public Vector3Int WorldToCell(Vector2 world)
    {
        float cs = CellSize;
        return new Vector3Int(Mathf.FloorToInt(world.x / cs), Mathf.FloorToInt(world.y / cs), 0);
    }

    public Vector3 CellCenterWorld(Vector3Int c)
    {
        float cs = CellSize;
        return new Vector3((c.x + 0.5f) * cs, (c.y + 0.5f) * cs, 0f);
    }

    /// <summary>
    /// The nearest in-rect cell centre to a world position — the ENTRY POINT for off-grid queriers
    /// (map-edge spawns), so they can still read the fields for their best target and march toward
    /// where the grid takes over. False = already inside the rect (or grid not ready).
    /// </summary>
    public static bool ClampToGrid(Vector2 world, out Vector2 clamped)
    {
        clamped = world;
        if (!Ready) return false;
        var r = blocked.CellRect;
        var c = blocked.WorldToCell(world);
        int x = Mathf.Clamp(c.x, r.xMin, r.xMax - 1);
        int y = Mathf.Clamp(c.y, r.yMin, r.yMax - 1);
        if (x == c.x && y == c.y) return false;
        clamped = blocked.CellCenterWorld(new Vector3Int(x, y, 0));
        return true;
    }

    // Hot queries (flow-field rebuilds hit EnterCost 4x per expanded cell; LOS DDA hits IsSolid
    // per crossed cell, per enemy per frame) read the Version-fresh raster arrays instead of
    // paying Dictionary<Vector2Int> probes. LIVE checks (destroyed / hasDied owners) stay at
    // query time via TryGetWallBySlot — a wall dying never bumps Version, exactly as before.

    // in-rect fast path index; false = off the squeeze rect (rare border queries) → dictionary path
    static bool TryRawIndex(Vector3Int c, out int idx)
    {
        int x = c.x - squeezeRect.xMin, y = c.y - squeezeRect.yMin;
        if (x < 0 || y < 0 || x >= squeezeRect.width || y >= squeezeRect.height) { idx = -1; return false; }
        idx = x + y * squeezeRect.width;
        return true;
    }

    public bool IsSolid(Vector3Int c)
    {
        EnsureSqueezeCache();
        if (TryRawIndex(c, out int idx))
        {
            if (solidMask[idx]) return true;
            ushort slot = rawSlot[idx];
            return slot != RawNone && BaseBlockMap.TryGetWallBySlot(slot, out _, out _);
        }
        var cell = new Vector2Int(c.x, c.y);
        return BaseBlockMap.HasSolid(cell) || BaseBlockMap.TryGetLiveWall(cell, out _, out _);
    }

    public int EnterCost(Vector3Int c)
    {
        EnsureSqueezeCache();
        if (TryRawIndex(c, out int idx))
        {
            ushort slot = rawSlot[idx];
            if (slot != RawNone && BaseBlockMap.TryGetWallBySlot(slot, out LifeScript wls, out float wmult))
                return chew ? WallCost(wls, wmult, breachEdgeCells[idx]) : PathGrid.BLOCKED;
            if (solidMask[idx]) return PathGrid.BLOCKED;
            int f = squeezeFill[idx];
            if (f >= 0 && BaseBlockMap.TryGetWallBySlot(f, out LifeScript ffls, out float ffmult))
                return chew ? WallCost(ffls, ffmult, true) : PathGrid.BLOCKED;
            if (f == FillSolid) return PathGrid.BLOCKED;
            int cst = InMap(c) ? 1 : offMapCost;
            cst += Mathf.Max(narrowPrem[idx], padRingCells[idx] == 1 ? wallPaddingCost : 0);
            return cst;
        }

        var cell = new Vector2Int(c.x, c.y);
        if (BaseBlockMap.TryGetLiveWall(cell, out LifeScript ls, out _, out float mult))
            return chew ? WallCost(ls, mult, BreachEdge(c)) : PathGrid.BLOCKED;
        if (BaseBlockMap.HasSolid(cell)) return PathGrid.BLOCKED;
        // CRACK FILLING: an open cell pinched between walls/solids on opposite sides is a slot no
        // circle body actually fits. For pathfinding it IS the adjacent wall: same chew price, same
        // gate identity (routes "through" it correctly target the wall), impassable in the detour
        // view. A pinch between two solid buildings (no wall to chew) is simply impassable.
        // Cracks are cavity seams by definition — their filler prices at the widening discount.
        int fill = SqueezeFill(c);
        if (fill >= 0 && BaseBlockMap.TryGetWallBySlot(fill, out LifeScript fls, out float fmult))
            return chew ? WallCost(fls, fmult, true) : PathGrid.BLOCKED;
        if (fill == FillSolid) return PathGrid.BLOCKED;
        int cost = InMap(c) ? 1 : offMapCost;
        // aperture premium and graded padding: the LARGER wins — never both, but padding must not
        // vanish inside premium-priced corridors (a flat premium has no centring gradient, so
        // routes hugged the wall side of snug lanes for free)
        cost += Mathf.Max(NarrowPremium(c), PadCost(c));
        return cost;
    }

    static int WallCost(LifeScript ls, float mult, bool cavityEdge = false)
        => 1 + Mathf.Clamp(Mathf.CeilToInt(ls.hp * costPerHp * mult * (cavityEdge ? breachEdgeChewMult : 1f)), 1, 63);

    // ------------------------------------------------------------------ map-interior cache
    // One bool per rect cell: is its centre inside the map's outer boundary polygon? Rebuilt only
    // when the rect changes or the map is rebuilt (MapManager.OnUpdateMap → InvalidateMapCache) —
    // the polygon test is far too slow to run per EnterCost call.

    static bool[] inMapCells;
    static RectInt inMapRect;
    static bool inMapValid;

    public static void InvalidateMapCache() { inMapValid = false; }

    /// <summary>New-session scrub of the derived static caches (domain reload off): the squeeze
    /// raster gates on BaseBlockMap.Version equality, which a freshly reset registry could
    /// accidentally re-hit — force both caches to rebuild on first query.</summary>
    public static void InvalidateSessionCaches()
    {
        squeezeVersion = int.MinValue;
        inMapValid = false;
    }

    static bool InMap(Vector3Int c)
    {
        EnsureInMapCache();
        if (inMapCells == null) return true;   // no map polygon yet — everything counts as inside
        int x = c.x - inMapRect.xMin, y = c.y - inMapRect.yMin;
        if (x < 0 || y < 0 || x >= inMapRect.width || y >= inMapRect.height) return false;
        return inMapCells[x + y * inMapRect.width];
    }

    static void EnsureInMapCache()
    {
        var r = blocked.CellRect;
        if (inMapValid && inMapCells != null && r.Equals(inMapRect)) return;
        Vector2[] poly = MapManager.GetOuterBoundary();
        if (poly == null) { inMapCells = null; return; }   // stays invalid — retried until the map exists
        inMapRect = r;
        if (inMapCells == null || inMapCells.Length != r.width * r.height)
            inMapCells = new bool[r.width * r.height];
        float cs = blocked.CellSize;
        for (int y = 0; y < r.height; y++)
            for (int x = 0; x < r.width; x++)
                inMapCells[x + y * r.width] = MapManager.PointInPoly(
                    new Vector2((r.xMin + x + 0.5f) * cs, (r.yMin + y + 0.5f) * cs), poly);
        inMapValid = true;
    }

    public bool AllowSolidSeeds => true;

    public int WallIdAt(Vector3Int c)
    {
        if (!chew) return -1;   // D view never crosses walls, so gates never arise
        EnsureSqueezeCache();
        if (TryRawIndex(c, out int idx))
        {
            ushort slot = rawSlot[idx];
            if (slot != RawNone && BaseBlockMap.TryGetWallBySlot(slot, out _, out _)) return slot;
            int f = squeezeFill[idx];
            return f >= 0 && BaseBlockMap.TryGetWallBySlot(f, out _, out _) ? f : -1;
        }
        if (BaseBlockMap.TryGetLiveWall(new Vector2Int(c.x, c.y), out _, out int id)) return id;
        // filled cracks carry their wall's identity so the gate propagates across them too
        int fill = SqueezeFill(c);
        return fill >= 0 && BaseBlockMap.TryGetWallBySlot(fill, out _, out _) ? fill : -1;
    }

    // ------------------------------------------------------------------ crack (squeeze) cache
    // One entry per rect cell: FillNone = not pinched, FillSolid = pinched between solids (no wall
    // to chew), >= 0 = pinched with a live wall alongside — that wall's slot id "fills" the crack.
    // Keyed to the registry Version, so it rebuilds only when walls/buildings actually change.

    const int FillNone = -2, FillSolid = -1;
    const ushort RawNone = ushort.MaxValue;
    static int[] squeezeFill;       // FINAL fill state (pass-1 crack fills overwritten by pass-2 aperture fills)
    static int[] crackFill;         // pass-1-only snapshot — pass 2's forward reads use this (raster-order semantics)
    static byte[] padRingCells;     // chebyshev ring distance to nearest solid/filled cell (0 = solid, capped)
    static byte[] narrowPrem;       // aperture pricing: per-cell premium for thin corridors
    static bool[] blockMask;        // pass-1 rasterisation of Blockedish — pass 2 scans arrays, not dictionaries
    static int[] wallIdMask;        // wall slot id of each blocked cell (-1 = solid) — gate identity for fills
    static bool[] breachEdgeCells;  // blocked cells flanking a gap — chewing these WIDENS a cavity
    static ushort[] rawSlot;        // Version-fresh raster of wallIdOfCell (RawNone = no registration)
    static bool[] solidMask;        // Version-fresh raster of the solid registry
    static RectInt squeezeRect;
    static int squeezeVersion = int.MinValue;

    /// <summary>Kill-switch: false = every registry change rebuilds the whole rect (the old
    /// behaviour), for A/B-ing the incremental dirty-region path.</summary>
    public static bool incrementalSqueeze = true;

    static int SqueezeFill(Vector3Int c)
    {
        EnsureSqueezeCache();
        if (squeezeFill == null) return FillNone;
        int x = c.x - squeezeRect.xMin, y = c.y - squeezeRect.yMin;
        if (x < 0 || y < 0 || x >= squeezeRect.width || y >= squeezeRect.height) return FillNone;
        return squeezeFill[x + y * squeezeRect.width];
    }

    static int PadCost(Vector3Int c)
    {
        EnsureSqueezeCache();
        if (padRingCells == null) return 0;
        int x = c.x - squeezeRect.xMin, y = c.y - squeezeRect.yMin;
        if (x < 0 || y < 0 || x >= squeezeRect.width || y >= squeezeRect.height) return 0;
        return padRingCells[x + y * squeezeRect.width] == 1 ? wallPaddingCost : 0;
    }

    static int NarrowPremium(Vector3Int c)
    {
        EnsureSqueezeCache();
        if (narrowPrem == null) return 0;
        int x = c.x - squeezeRect.xMin, y = c.y - squeezeRect.yMin;
        if (x < 0 || y < 0 || x >= squeezeRect.width || y >= squeezeRect.height) return 0;
        return narrowPrem[x + y * squeezeRect.width];
    }

    static bool BreachEdge(Vector3Int c)
    {
        EnsureSqueezeCache();
        if (breachEdgeCells == null) return false;
        int x = c.x - squeezeRect.xMin, y = c.y - squeezeRect.yMin;
        if (x < 0 || y < 0 || x >= squeezeRect.width || y >= squeezeRect.height) return false;
        return breachEdgeCells[x + y * squeezeRect.width];
    }

    /// <summary>Is this wall cell flanking an existing cavity — i.e. would chewing it WIDEN a gap?
    /// (For the wall-hunting fields: cavity-edge walls read as more attractive to weighted views.)</summary>
    public static bool IsCavityEdge(Vector2Int cell) => BreachEdge(new Vector3Int(cell.x, cell.y, 0));

    /// <summary>The padding-driven part of an open cell's surcharge — how much of
    /// `Max(aperture, padding)` exists ONLY because of the padding rings. The ruler prices this
    /// into its wander-discounted bucket (padding is comfort, not obstacle), so wander-0 lines
    /// ignore it and still attack the first wall in the way.</summary>
    public static int PadExcess(Vector3Int c) => Mathf.Max(0, PadCost(c) - NarrowPremium(c));

    /// <summary>Debug/gizmo read of everything the router believes about one cell — solidity,
    /// map interior, crack fill, aperture premium, padding cost. Lets the route gizmo heat-map
    /// (and the console grid dump) show exactly what the router is being charged, so "why is it
    /// hugging HERE" is answerable on sight.</summary>
    public static void DebugCellState(Vector3Int c, out bool isSolid, out bool inMap, out bool filled,
        out int aperture, out int padding)
    {
        isSolid = ((IPathGrid)blocked).Ready && blocked.IsSolid(c);
        inMap = InMap(c);
        filled = SqueezeFill(c) != FillNone;
        aperture = NarrowPremium(c);
        padding = PadCost(c);
    }

    static void EnsureSqueezeCache()
    {
        if (!Ready) return;   // pre-warm queries fall back to the dictionary paths (arrays stay null)
        var r = blocked.CellRect;
        // every array checked individually: a hot-reload patch can null ONE new static while the
        // others (and the version stamp) survive — a joint guard then never rebuilds and the new
        // layer silently reads as empty
        int n = r.width * r.height;
        bool arraysValid = squeezeFill != null && crackFill != null && narrowPrem != null
            && padRingCells != null && blockMask != null && wallIdMask != null && breachEdgeCells != null
            && rawSlot != null && solidMask != null
            && squeezeFill.Length == n && r.Equals(squeezeRect);
        if (arraysValid && squeezeVersion == BaseBlockMap.Version) return;

        var dirty = BaseBlockMap.DirtyCells;
        bool full = !arraysValid || !incrementalSqueeze || BaseBlockMap.DirtyOverflow || dirty.Count == 0;

        float csz = blocked.CellSize;
        int reach = Mathf.CeilToInt(snugWidth / csz);   // aperture scan radius — pass 2's influence reach

        RectInt reg = default;
        if (!full)
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            for (int k = 0; k < dirty.Count; k++)
            {
                int x = dirty[k].x - r.xMin, y = dirty[k].y - r.yMin;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
            reg = ClampRegion(minX - reach - 1, minY - reach - 1, maxX + reach + 1, maxY + reach + 1, r);
            if (reg.width <= 0 || reg.height <= 0) { full = true; }            // dirty cells all off-rect
            else if ((long)reg.width * reg.height > (long)n * 2 / 5) full = true;   // not worth being clever
        }

        squeezeVersion = BaseBlockMap.Version;
        squeezeRect = r;
        if (full)
        {
            FullSqueezeRebuild(r, n);
            BaseBlockMap.ClearDirty();
            return;
        }

        // Incremental: recompute the dirty neighbourhood. Whenever pass 2's fill results CHANGE
        // near the region rim, the erosion cascade could reach cells outside it — grow the region
        // and redo, so the final state matches what a full rebuild would have produced. Growth
        // decisions use the CUMULATIVE change bounds (later sweeps compare against the previous
        // sweep, so their `changed` alone would under-report proximity to the new rim).
        int cumMinX = int.MaxValue, cumMinY = int.MaxValue, cumMaxX = int.MinValue, cumMaxY = int.MinValue;
        for (int guard = 0; guard < 64; guard++)
        {
            BaseBlockMap.RasterProbe(r, reg, solidMask, rawSlot, RawNone);
            Pass1Region(reg, r);
            Pass2Region(reg, r, out RectInt changed, out bool anyChange);
            if (anyChange)
            {
                if (changed.xMin < cumMinX) cumMinX = changed.xMin;
                if (changed.yMin < cumMinY) cumMinY = changed.yMin;
                if (changed.xMax - 1 > cumMaxX) cumMaxX = changed.xMax - 1;
                if (changed.yMax - 1 > cumMaxY) cumMaxY = changed.yMax - 1;
            }
            if (cumMinX == int.MaxValue) break;   // nothing actually changed
            bool grow = false;
            int gMinX = reg.xMin, gMinY = reg.yMin, gMaxX = reg.xMax - 1, gMaxY = reg.yMax - 1;
            if (reg.xMin > 0 && cumMinX - reg.xMin < reach) { gMinX = reg.xMin - reach; grow = true; }
            if (reg.yMin > 0 && cumMinY - reg.yMin < reach) { gMinY = reg.yMin - reach; grow = true; }
            if (reg.xMax < r.width && reg.xMax - 1 - cumMaxX < reach) { gMaxX = reg.xMax - 1 + reach; grow = true; }
            if (reg.yMax < r.height && reg.yMax - 1 - cumMaxY < reach) { gMaxY = reg.yMax - 1 + reach; grow = true; }
            if (!grow) break;
            reg = ClampRegion(gMinX, gMinY, gMaxX, gMaxY, r);
            if ((long)reg.width * reg.height > (long)n * 2 / 5)
            {
                FullSqueezeRebuild(r, n);
                BaseBlockMap.ClearDirty();
                return;
            }
        }

        // chamfer reads solids/fills within maxRing(2); breach edges read gaps within 1
        var cham = ClampRegion(reg.xMin - 3, reg.yMin - 3, reg.xMax - 1 + 3, reg.yMax - 1 + 3, r);
        ChamferRegion(cham, r);
        var br = ClampRegion(cham.xMin - 1, cham.yMin - 1, cham.xMax - 1 + 1, cham.yMax - 1 + 1, r);
        BreachRegion(br, r);
        BaseBlockMap.ClearDirty();
    }

    static RectInt ClampRegion(int minX, int minY, int maxX, int maxY, RectInt r)
    {
        minX = Mathf.Max(0, minX);
        minY = Mathf.Max(0, minY);
        maxX = Mathf.Min(r.width - 1, maxX);
        maxY = Mathf.Min(r.height - 1, maxY);
        return new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    static void EnsureArrays(int n)
    {
        if (squeezeFill == null || squeezeFill.Length != n) squeezeFill = new int[n];
        if (crackFill == null || crackFill.Length != n) crackFill = new int[n];
        if (padRingCells == null || padRingCells.Length != n) padRingCells = new byte[n];
        if (narrowPrem == null || narrowPrem.Length != n) narrowPrem = new byte[n];
        if (blockMask == null || blockMask.Length != n) blockMask = new bool[n];
        if (wallIdMask == null || wallIdMask.Length != n) wallIdMask = new int[n];
        if (breachEdgeCells == null || breachEdgeCells.Length != n) breachEdgeCells = new bool[n];
        if (rawSlot == null || rawSlot.Length != n) rawSlot = new ushort[n];
        if (solidMask == null || solidMask.Length != n) solidMask = new bool[n];
    }

    static void FullSqueezeRebuild(RectInt r, int n)
    {
        EnsureArrays(n);
        System.Array.Fill(rawSlot, RawNone);
        System.Array.Clear(solidMask, 0, n);
        BaseBlockMap.RasterStampAll(r, solidMask, rawSlot);
        var whole = new RectInt(0, 0, r.width, r.height);
        Pass1Region(whole, r);
        Pass2Region(whole, r, out _, out _);
        ChamferRegion(whole, r);
        BreachRegion(whole, r);
    }

    // pass 1: rasterise the registry, fill cracks. Writes crackFill (the pass-1 snapshot that
    // pass 2's forward reads use) — squeezeFill gets its final value in pass 2.
    static void Pass1Region(RectInt reg, RectInt r)
    {
        for (int y = reg.yMin; y < reg.yMax; y++)
            for (int x = reg.xMin; x < reg.xMax; x++)
            {
                int cx = r.xMin + x, cy = r.yMin + y;
                int idx = x + y * r.width;
                crackFill[idx] = FillNone;
                if (BlockedishAt(cx, cy, r, out int selfId)) { blockMask[idx] = true; wallIdMask[idx] = selfId; continue; }
                blockMask[idx] = false;
                wallIdMask[idx] = -1;
                bool l = BlockedishAt(cx - 1, cy, r, out int wl), rr = BlockedishAt(cx + 1, cy, r, out int wr);
                bool d = BlockedishAt(cx, cy - 1, r, out int wd), u = BlockedishAt(cx, cy + 1, r, out int wu);
                if (!((l && rr) || (d && u))) continue;    // crack fill needs opposing sides pinched
                int wall = FillSolid;
                if (l && rr && wl >= 0) wall = wl;
                else if (l && rr && wr >= 0) wall = wr;
                else if (d && u && wd >= 0) wall = wd;
                else if (d && u && wu >= 0) wall = wu;
                crackFill[idx] = wall;
            }
    }

    // pass 2: aperture widths in WORLD UNITS, measured over pass-1 results plus this pass's
    // own fills (a sub-body gap that fills makes its neighbours narrower — the erosion
    // cascades, but only below the fill threshold, so real corridors never seal themselves).
    // Four axes catch zigzag seams between diagonally offset walls that pure H/V misses.
    // Raster-order semantics: cells EARLIER in the sweep are read at their final (pass-2) value,
    // cells LATER at their pass-1 value — crackFill/squeezeFill split keeps that exact for
    // region recomputes too. `changed` reports the bounds of cells whose final fill differs
    // from the previous build (drives the incremental rim re-expansion).
    static void Pass2Region(RectInt reg, RectInt r, out RectInt changed, out bool anyChange)
    {
        float csz = blocked.CellSize;
        float diagStep = csz * 1.41421f;
        int maxOrtho = Mathf.CeilToInt(snugWidth / csz);
        int maxDiag = Mathf.CeilToInt(snugWidth / diagStep);
        int chMinX = int.MaxValue, chMinY = int.MaxValue, chMaxX = int.MinValue, chMaxY = int.MinValue;
        for (int y = reg.yMin; y < reg.yMax; y++)
            for (int x = reg.xMin; x < reg.xMax; x++)
            {
                int idx = x + y * r.width;
                int prevFinal = squeezeFill[idx];
                int newFinal = crackFill[idx];
                narrowPrem[idx] = 0;
                if (!blockMask[idx] && newFinal == FillNone)
                {
                    float w = float.MaxValue;
                    int gateWall = -1;
                    MeasureAxis(x, y, idx, 1, 0, csz, maxOrtho, r, ref w, ref gateWall);
                    MeasureAxis(x, y, idx, 0, 1, csz, maxOrtho, r, ref w, ref gateWall);
                    MeasureAxis(x, y, idx, 1, 1, diagStep, maxDiag, r, ref w, ref gateWall);
                    MeasureAxis(x, y, idx, 1, -1, diagStep, maxDiag, r, ref w, ref gateWall);
                    if (w < minPassableWidth)
                        newFinal = gateWall >= 0 ? gateWall : FillSolid;   // no body fits: IS the wall
                    else if (w < tightWidth) narrowPrem[idx] = (byte)Mathf.Clamp(narrowGapCost2, 0, 255);
                    else if (w < snugWidth) narrowPrem[idx] = (byte)Mathf.Clamp(narrowGapCost3, 0, 255);
                }
                squeezeFill[idx] = newFinal;
                if (newFinal != prevFinal)
                {
                    if (x < chMinX) chMinX = x;
                    if (x > chMaxX) chMaxX = x;
                    if (y < chMinY) chMinY = y;
                    if (y > chMaxY) chMaxY = y;
                }
            }
        anyChange = chMinX != int.MaxValue;
        changed = anyChange ? new RectInt(chMinX, chMinY, chMaxX - chMinX + 1, chMaxY - chMinY + 1) : default;
    }

    // pass 2.5: chebyshev distance-to-solid rings over walls, solids and everything pass 1/2
    // filled, via a standard two-pass chamfer transform. The base only prices ring 1 (the
    // original wallPaddingCost), so the transform caps at 2 — cells at the cap read as free.
    // Ring values only depend on solids/fills within the cap, so a region recompute inflated
    // past the cap reads valid stored values at its rim.
    static void ChamferRegion(RectInt reg, RectInt r)
    {
        int W = r.width;
        int maxRing = 2;
        for (int y = reg.yMin; y < reg.yMax; y++)
            for (int x = reg.xMin; x < reg.xMax; x++)
            {
                int idx = x + y * W;
                int best = blockMask[idx] || squeezeFill[idx] != FillNone ? 0 : maxRing;
                if (best > 0)
                {
                    if (x > 0) best = Mathf.Min(best, padRingCells[idx - 1] + 1);
                    if (y > 0)
                    {
                        best = Mathf.Min(best, padRingCells[idx - W] + 1);
                        if (x > 0) best = Mathf.Min(best, padRingCells[idx - W - 1] + 1);
                        if (x < W - 1) best = Mathf.Min(best, padRingCells[idx - W + 1] + 1);
                    }
                }
                padRingCells[idx] = (byte)Mathf.Min(best, maxRing);
            }
        for (int y = reg.yMax - 1; y >= reg.yMin; y--)
            for (int x = reg.xMax - 1; x >= reg.xMin; x--)
            {
                int idx = x + y * W;
                int best = padRingCells[idx];
                if (best == 0) continue;
                if (x < W - 1) best = Mathf.Min(best, padRingCells[idx + 1] + 1);
                if (y < r.height - 1)
                {
                    best = Mathf.Min(best, padRingCells[idx + W] + 1);
                    if (x < W - 1) best = Mathf.Min(best, padRingCells[idx + W + 1] + 1);
                    if (x > 0) best = Mathf.Min(best, padRingCells[idx + W - 1] + 1);
                }
                padRingCells[idx] = (byte)best;
            }
    }

    // pass 3: cavity edges — blocked cells 4-adjacent to a gap cell (a wall-filled crack or a
    // priced narrow-corridor cell). Chewing one of these widens an existing cavity, so
    // WallCost discounts them by breachEdgeChewMult and the widening snowballs until the gap
    // hits free-flow width. (Solid-solid pinches don't count: no chew can widen those.)
    static void BreachRegion(RectInt reg, RectInt r)
    {
        for (int y = reg.yMin; y < reg.yMax; y++)
            for (int x = reg.xMin; x < reg.xMax; x++)
            {
                int idx = x + y * r.width;
                breachEdgeCells[idx] = blockMask[idx] &&
                    (GapAt(x - 1, y, r) || GapAt(x + 1, y, r) || GapAt(x, y - 1, r) || GapAt(x, y + 1, r));
            }
    }

    static bool GapAt(int x, int y, RectInt r)
    {
        if (x < 0 || y < 0 || x >= r.width || y >= r.height) return false;
        int i = x + y * r.width;
        return narrowPrem[i] > 0 || squeezeFill[i] >= 0;
    }

    // Clear width (world units) of the corridor through this cell along one axis; keeps the
    // narrowest result across axes plus a live wall id flanking that narrowest pinch — the gate
    // identity if the cell ends up filled. Open on either side within the scan = not a corridor.
    static void MeasureAxis(int x, int y, int readerIdx, int dx, int dy, float step, int maxScan, RectInt r,
        ref float best, ref int gateWall)
    {
        int a = ScanWallish(x, y, readerIdx, dx, dy, maxScan, r, out int idA);
        if (a <= 0) return;
        int b = ScanWallish(x, y, readerIdx, -dx, -dy, maxScan, r, out int idB);
        if (b <= 0) return;
        float w = (a + b - 1) * step;
        if (w >= best) return;
        best = w;
        gateWall = idA >= 0 ? idA : idB;
    }

    static int ScanWallish(int x, int y, int readerIdx, int dx, int dy, int maxScan, RectInt r, out int wallId)
    {
        wallId = -1;
        for (int s = 1; s <= maxScan; s++)
        {
            int nx = x + dx * s, ny = y + dy * s;
            if (nx < 0 || ny < 0 || nx >= r.width || ny >= r.height) return 0;   // rect edge = open
            int ni = nx + ny * r.width;
            if (blockMask[ni]) { wallId = wallIdMask[ni]; return s; }
            // earlier-in-raster cells read at their pass-2 value, later ones at pass-1 —
            // the original single-array sweep's exact semantics
            int fillAt = ni < readerIdx ? squeezeFill[ni] : crackFill[ni];
            if (fillAt != FillNone) { wallId = fillAt >= 0 ? fillAt : -1; return s; }
        }
        return 0;
    }

    // absolute-cell blockedish over the Version-fresh raster; off-rect falls back to the registry
    static bool BlockedishAt(int gx, int gy, RectInt r, out int wallId)
    {
        int x = gx - r.xMin, y = gy - r.yMin;
        if (x < 0 || y < 0 || x >= r.width || y >= r.height) return Blockedish(gx, gy, out wallId);
        int idx = x + y * r.width;
        ushort slot = rawSlot[idx];
        if (slot != RawNone && BaseBlockMap.TryGetWallBySlot(slot, out _, out _)) { wallId = slot; return true; }
        wallId = -1;
        return solidMask[idx];
    }

    static bool Blockedish(int x, int y, out int wallId)
    {
        var cell = new Vector2Int(x, y);
        if (BaseBlockMap.TryGetLiveWall(cell, out _, out wallId)) return true;
        wallId = -1;
        return BaseBlockMap.HasSolid(cell);
    }

    public IReadOnlyList<LifeScript> Walls => BaseBlockMap.WallOwners;
}
