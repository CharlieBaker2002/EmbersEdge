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
    /// line passes through becomes a chewable cell of this LifeScript. Replaces previous cells.</summary>
    public static void RegisterWallPath(LifeScript ls, IList<Vector3> points, float costMult = 1f)
    {
        if (ls == null || points == null || points.Count < 2) return;
        int id = ClaimSlot(ls, costMult, out List<Vector2Int> cells);
        for (int s = 0; s < points.Count - 1; s++)
            RasterSegment(points[s], points[s + 1], (ushort)id, cells);
        Version++;
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
            if (wallIdOfCell.TryGetValue(c, out ushort slot) && slot == entry.id) wallIdOfCell.Remove(c);
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
                if (wallIdOfCell.TryGetValue(c, out ushort slot) && slot == existing.id) wallIdOfCell.Remove(c);
            existing.cells.Clear();
            cells = existing.cells;
            wallMults[existing.id] = costMult;
            return existing.id;
        }
        int id = wallOwners.IndexOf(null);
        if (id < 0) { id = wallOwners.Count; wallOwners.Add(ls); wallMults.Add(costMult); }
        else { wallOwners[id] = ls; wallMults[id] = costMult; }
        cells = new List<Vector2Int>();
        byWall[ls] = (id, cells, ls.transform);
        wallByTf[ls.transform] = ls;
        return id;
    }

    static void ClaimWallCell(Vector2Int c, ushort id, List<Vector2Int> cells)
    {
        if (wallIdOfCell.ContainsKey(c)) return;   // first wall to claim a cell keeps it
        wallIdOfCell[c] = id;
        cells.Add(c);
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
    /// Extra cost of walking a cell that TOUCHES a wall/solid (8-neighbour). Together with crack
    /// filling this is obstacle padding (~0.25u at cellSize 1) quantized to the grid: cracks
    /// narrower than a body are blocked outright, and routes prefer to keep one cell of daylight
    /// when rounding walls — but hugging stays possible where it's needed (chewing a wall face,
    /// squeezing a doorway, reaching a target parked against a building) at a small premium.
    /// 0 disables. Keep small: it should bias corners, never dominate route choice.
    /// </summary>
    public static int wallPaddingCost = 1;

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

    public bool IsSolid(Vector3Int c)
    {
        var cell = new Vector2Int(c.x, c.y);
        return BaseBlockMap.HasSolid(cell) || BaseBlockMap.TryGetLiveWall(cell, out _, out _);
    }

    public int EnterCost(Vector3Int c)
    {
        var cell = new Vector2Int(c.x, c.y);
        if (BaseBlockMap.TryGetLiveWall(cell, out LifeScript ls, out _, out float mult))
            return chew ? WallCost(ls, mult) : PathGrid.BLOCKED;
        if (BaseBlockMap.HasSolid(cell)) return PathGrid.BLOCKED;
        // CRACK FILLING: an open cell pinched between walls/solids on opposite sides is a slot no
        // circle body actually fits. For pathfinding it IS the adjacent wall: same chew price, same
        // gate identity (routes "through" it correctly target the wall), impassable in the detour
        // view. A pinch between two solid buildings (no wall to chew) is simply impassable.
        int fill = SqueezeFill(c);
        if (fill >= 0 && BaseBlockMap.TryGetWallBySlot(fill, out LifeScript fls, out float fmult))
            return chew ? WallCost(fls, fmult) : PathGrid.BLOCKED;
        if (fill == FillSolid) return PathGrid.BLOCKED;
        int cost = InMap(c) ? 1 : offMapCost;
        if (NearSolid(c)) cost += wallPaddingCost;   // padding: prefer a cell of daylight off walls
        return cost;
    }

    static int WallCost(LifeScript ls, float mult)
        => 1 + Mathf.Clamp(Mathf.CeilToInt(ls.hp * costPerHp * mult), 1, 63);

    // ------------------------------------------------------------------ map-interior cache
    // One bool per rect cell: is its centre inside the map's outer boundary polygon? Rebuilt only
    // when the rect changes or the map is rebuilt (MapManager.OnUpdateMap → InvalidateMapCache) —
    // the polygon test is far too slow to run per EnterCost call.

    static bool[] inMapCells;
    static RectInt inMapRect;
    static bool inMapValid;

    public static void InvalidateMapCache() { inMapValid = false; }

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
    static int[] squeezeFill;
    static bool[] nearSolidCells;   // open cell 8-adjacent to a wall/solid — the padding ring
    static RectInt squeezeRect;
    static int squeezeVersion = int.MinValue;

    static int SqueezeFill(Vector3Int c)
    {
        EnsureSqueezeCache();
        if (squeezeFill == null) return FillNone;
        int x = c.x - squeezeRect.xMin, y = c.y - squeezeRect.yMin;
        if (x < 0 || y < 0 || x >= squeezeRect.width || y >= squeezeRect.height) return FillNone;
        return squeezeFill[x + y * squeezeRect.width];
    }

    static bool NearSolid(Vector3Int c)
    {
        EnsureSqueezeCache();
        if (nearSolidCells == null) return false;
        int x = c.x - squeezeRect.xMin, y = c.y - squeezeRect.yMin;
        if (x < 0 || y < 0 || x >= squeezeRect.width || y >= squeezeRect.height) return false;
        return nearSolidCells[x + y * squeezeRect.width];
    }

    static void EnsureSqueezeCache()
    {
        var r = blocked.CellRect;
        if (squeezeFill != null && squeezeVersion == BaseBlockMap.Version && r.Equals(squeezeRect)) return;
        squeezeVersion = BaseBlockMap.Version;
        squeezeRect = r;
        if (squeezeFill == null || squeezeFill.Length != r.width * r.height)
        {
            squeezeFill = new int[r.width * r.height];
            nearSolidCells = new bool[r.width * r.height];
        }
        for (int y = 0; y < r.height; y++)
            for (int x = 0; x < r.width; x++)
            {
                int cx = r.xMin + x, cy = r.yMin + y;
                int idx = x + y * r.width;
                squeezeFill[idx] = FillNone;
                nearSolidCells[idx] = false;
                if (Blockedish(cx, cy, out _)) continue;   // the cell itself is wall/solid, not a crack
                bool l = Blockedish(cx - 1, cy, out int wl), rr = Blockedish(cx + 1, cy, out int wr);
                bool d = Blockedish(cx, cy - 1, out int wd), u = Blockedish(cx, cy + 1, out int wu);
                nearSolidCells[idx] = l || rr || d || u
                    || Blockedish(cx - 1, cy - 1, out _) || Blockedish(cx + 1, cy - 1, out _)
                    || Blockedish(cx - 1, cy + 1, out _) || Blockedish(cx + 1, cy + 1, out _);
                if (!((l && rr) || (d && u))) continue;    // crack fill needs opposing sides pinched
                int wall = FillSolid;
                if (l && rr && wl >= 0) wall = wl;
                else if (l && rr && wr >= 0) wall = wr;
                else if (d && u && wd >= 0) wall = wd;
                else if (d && u && wu >= 0) wall = wu;
                squeezeFill[idx] = wall;
            }
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
