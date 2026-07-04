using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pathfinding over cell grids (dungeon MineField / base building grid — see <see cref="IPathGrid"/>).
/// Three layers:
///
///  1. <see cref="MineFlowField"/> — a multi-source weighted flow field (bucket-queue Dijkstra; pure
///     BFS when every cost is 1). One rebuild costs ~one visit per open cell (sub-ms) REGARDLESS of
///     seed count; each agent then reads its steering direction, nearest-target identity, and the
///     first chewable wall on its route with O(1) lookups.
///  2. <see cref="MinePath"/> statics — grid line-of-sight (thin + projectile-width rays, plus
///     "which of my targets can I actually see" helpers for shooters) and A* for one-off A→B paths.
///     All of it dispatches by QUERIER POSITION (dungeon grid / base grid / no grid = no walls), so
///     shared enemy code calls these unconditionally in either dimension.
///  3. <see cref="MinePathManager"/> — the query front door. Dungeon queries hit its own lazily-
///     created standing fields; base-positioned queries route to <see cref="BasePathManager"/>.
///     Fields only rebuild while something is actually querying them, so idle worlds cost zero.
///
/// Movement model matches the rest of the game: these return DIRECTIONS (feed ActionScript force
/// steering), not rigid waypoint following. 4-way wave expansion (walls are axis-aligned tiles) with
/// 8-way, corner-cut-safe direction sampling so agents still move diagonally down open rooms.
/// </summary>
public class MineFlowField
{
    public struct Seed
    {
        public Vector2 pos;
        public int cost;         // starting distance in cells — a "priority penalty" (0 = full priority)
        public Transform owner;  // optional: WHO this seed is, so queries can name the nearest target
        public Seed(Vector2 p, int c = 0, Transform o = null) { pos = p; cost = c; owner = o; }
    }

    const ushort UNREACHED = ushort.MaxValue;
    const ushort NO_GATE = ushort.MaxValue;

    IPathGrid grid;
    RectInt rect;
    ushort[] dist;
    ushort[] seedOf;   // which seed's wavefront claimed each cell — propagated for free during the sweep
    ushort[] gateOf;   // FIRST chewable wall an agent at this cell meets walking downhill (NO_GATE = none)

    // scratch, reused across rebuilds — no steady-state allocation
    readonly List<List<int>> buckets = new List<List<int>>();   // buckets[d] = cells pushed at distance d
    readonly List<(int li, int cost, int src)> seedCells = new List<(int, int, int)>();
    readonly List<Transform> owners = new List<Transform>();
    IReadOnlyList<LifeScript> walls;   // grid's wall table captured at rebuild (gate ids index it)

    public bool IsBuilt => dist != null && grid != null;

    int LocalIdx(Vector3Int c) => (c.x - rect.xMin) + (c.y - rect.yMin) * rect.width;
    bool InRect(Vector3Int c) => c.x >= rect.xMin && c.x < rect.xMax && c.y >= rect.yMin && c.y < rect.yMax;

    static readonly Vector3Int[] N4 =
        { new Vector3Int(1, 0, 0), new Vector3Int(-1, 0, 0), new Vector3Int(0, 1, 0), new Vector3Int(0, -1, 0) };

    /// <summary>
    /// Rebuild the whole field from a set of seed positions. Seeds inside solid cells snap to an
    /// open 4-neighbour (or, on grids with <see cref="IPathGrid.AllowSolidSeeds"/>, stay put so
    /// building footprints attract attackers into contact). Seed cost is an integer head start in
    /// cells — a "priority penalty". Chewable wall cells are crossed at their
    /// <see cref="IPathGrid.EnterCost"/>, and the identity of the first wall on each cell's route
    /// is recorded for <see cref="TryGetGate"/>.
    /// </summary>
    public void Rebuild(IPathGrid g, List<Seed> seeds)
    {
        grid = g;
        rect = g.CellRect;
        walls = g.Walls;
        int size = rect.width * rect.height;
        if (size <= 0) { dist = null; return; }
        if (dist == null || dist.Length != size)
        {
            dist = new ushort[size];
            seedOf = new ushort[size];
            gateOf = new ushort[size];
        }
        for (int k = 0; k < size; k++) dist[k] = UNREACHED;
        for (int k = 0; k < buckets.Count; k++) buckets[k].Clear();

        // resolve seeds to cells
        seedCells.Clear();
        owners.Clear();
        for (int s = 0; s < seeds.Count; s++)
        {
            var c = g.WorldToCell(seeds[s].pos);
            if (!InRect(c)) continue;
            if (g.IsSolid(c) && !g.AllowSolidSeeds)
            {
                Vector3Int snapped = default;
                bool found = false;
                foreach (var d in N4)
                    if (InRect(c + d) && !g.IsSolid(c + d)) { snapped = c + d; found = true; break; }
                if (!found) continue;
                c = snapped;
            }
            seedCells.Add((LocalIdx(c), Mathf.Max(0, seeds[s].cost), owners.Count));
            owners.Add(seeds[s].owner);
        }
        if (seedCells.Count == 0) return;

        // Bucket-queue Dijkstra: buckets[d] holds cells pushed at distance d; a stale-pop guard
        // (dist changed since push) keeps it to one EXPANSION per cell. With every EnterCost == 1
        // this is exactly the old layered BFS (each bucket == the old `next` list) — dungeon parity.
        int maxLevel = 0;
        for (int s = 0; s < seedCells.Count; s++)
        {
            var (li, cost, src) = seedCells[s];
            if (cost >= dist[li]) continue;
            dist[li] = (ushort)cost; seedOf[li] = (ushort)src; gateOf[li] = NO_GATE;
            Bucket(cost).Add(li);
            if (cost > maxLevel) maxLevel = cost;
        }

        for (int level = 0; level <= maxLevel && level < UNREACHED - 66; level++)
        {
            if (level >= buckets.Count) continue;
            var bucket = buckets[level];
            for (int i = 0; i < bucket.Count; i++)
            {
                int li = bucket[i];
                if (dist[li] != level) continue;   // superseded by a cheaper route since it was pushed
                var c = new Vector3Int(rect.xMin + li % rect.width, rect.yMin + li / rect.width, 0);
                // gate carried outward: crossing a wall cell stamps that wall onto everything beyond it
                int cWall = grid.WallIdAt(c);
                ushort gate = cWall >= 0 ? (ushort)cWall : gateOf[li];
                foreach (var d in N4)
                {
                    var n = c + d;
                    if (!InRect(n)) continue;
                    int ec = grid.EnterCost(n);
                    if (ec == PathGrid.BLOCKED) continue;
                    int nd = level + ec;
                    if (nd >= UNREACHED - 1) continue;
                    int ni = LocalIdx(n);
                    if (nd >= dist[ni]) continue;
                    dist[ni] = (ushort)nd;
                    seedOf[ni] = seedOf[li];   // the claiming wavefront carries its identity outward
                    gateOf[ni] = gate;
                    Bucket(nd).Add(ni);
                    if (nd > maxLevel) maxLevel = nd;
                }
            }
            bucket.Clear();
        }
    }

    List<int> Bucket(int d)
    {
        while (buckets.Count <= d) buckets.Add(new List<int>());
        return buckets[d];
    }

    /// <summary>
    /// The steering direction from a world position toward the nearest seed — points at the centre
    /// of the best downhill neighbour cell (diagonals allowed only when both flanking orthogonal
    /// cells are open, so it never steers through a wall corner; chewable wall cells CAN be the
    /// downhill choice — that's the "walk into the wall and chew" behavior). Returns false when the
    /// position is off-grid or unreachable from every seed — fall back to direct steering. Returns
    /// true with <c>dir == Vector2.zero</c> when standing on a seed cell (arrived).
    /// </summary>
    public bool TryGetStepDir(Vector2 world, out Vector2 dir)
    {
        dir = Vector2.zero;
        if (!IsBuilt) return false;
        var c = grid.WorldToCell(world);
        if (!InRect(c)) return false;
        int li = LocalIdx(c);
        if (dist[li] == UNREACHED) return false;
        if (dist[li] == 0) return true;   // standing on a target cell

        int best = dist[li];
        Vector3Int bestCell = c;
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                var n = new Vector3Int(c.x + dx, c.y + dy, 0);
                if (!InRect(n)) continue;
                if (dx != 0 && dy != 0)   // diagonal: no cutting wall corners
                {
                    if (grid.IsSolid(new Vector3Int(c.x + dx, c.y, 0))) continue;
                    if (grid.IsSolid(new Vector3Int(c.x, c.y + dy, 0))) continue;
                }
                int ni = LocalIdx(n);
                if (dist[ni] < best) { best = dist[ni]; bestCell = n; }
            }
        if (bestCell == c) return false;   // isolated local minimum — shouldn't happen, be safe
        dir = ((Vector2)grid.CellCenterWorld(bestCell) - world).normalized;
        return true;
    }

    /// <summary>
    /// Debug: append the downhill cell-centre route from a world position to its nearest seed —
    /// the exact cells an agent following <see cref="TryGetStepDir"/> walks (same corner-cut-safe
    /// neighbour rules). Returns the number of points appended (0 = off-grid/unreachable).
    /// </summary>
    public int TraceRoute(Vector2 world, List<Vector2> outPts, int maxSteps = 512)
    {
        if (!IsBuilt) return 0;
        var c = grid.WorldToCell(world);
        if (!InRect(c) || dist[LocalIdx(c)] == UNREACHED) return 0;
        int n = 0;
        while (n < maxSteps)
        {
            outPts.Add(grid.CellCenterWorld(c));
            n++;
            int here = dist[LocalIdx(c)];
            if (here == 0) break;
            int best = here;
            Vector3Int bestCell = c;
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var nb = new Vector3Int(c.x + dx, c.y + dy, 0);
                    if (!InRect(nb)) continue;
                    if (dx != 0 && dy != 0)
                    {
                        if (grid.IsSolid(new Vector3Int(c.x + dx, c.y, 0))) continue;
                        if (grid.IsSolid(new Vector3Int(c.x, c.y + dy, 0))) continue;
                    }
                    int ni = LocalIdx(nb);
                    if (dist[ni] < best) { best = dist[ni]; bestCell = nb; }
                }
            if (bestCell == c) break;
            c = bestCell;
        }
        return n;
    }

    /// <summary>Path distance (in cells, wall chew-costs included) from a world position to the
    /// nearest seed. -1 = unreachable/off-grid.</summary>
    public int DistanceCells(Vector2 world)
    {
        if (!IsBuilt) return -1;
        var c = grid.WorldToCell(world);
        if (!InRect(c)) return -1;
        int d = dist[LocalIdx(c)];
        return d == UNREACHED ? -1 : d;
    }

    /// <summary>
    /// WHO is nearest (by path, priority penalties included) from this position, and how far — one
    /// array read; the identity was propagated during the rebuild. This replaces the whole
    /// "physics-overlap candidates, then rank" dance for target acquisition. False =
    /// unreachable/off-grid; <paramref name="owner"/> can be null if that seed carried no transform.
    /// </summary>
    public bool TryGetNearestSeed(Vector2 world, out Transform owner, out int distCells)
    {
        owner = null; distCells = -1;
        if (!IsBuilt) return false;
        var c = grid.WorldToCell(world);
        if (!InRect(c)) return false;
        int li = LocalIdx(c);
        if (dist[li] == UNREACHED) return false;
        distCells = dist[li];
        int src = seedOf[li];
        if (src < owners.Count) owner = owners[src];
        return true;
    }

    /// <summary>
    /// The FIRST chewable wall an agent at this position will meet walking its downhill route (the
    /// wave stamped it outward during the rebuild — O(1) read). This is the "attack THIS wall"
    /// target when the wander decision says chew. False = no wall between here and the nearest seed.
    /// </summary>
    public bool TryGetGate(Vector2 world, out LifeScript wall)
    {
        wall = null;
        if (!IsBuilt) return false;
        var c = grid.WorldToCell(world);
        if (!InRect(c)) return false;
        int li = LocalIdx(c);
        if (dist[li] == UNREACHED || gateOf[li] == NO_GATE) return false;
        int id = gateOf[li];
        if (walls == null || id >= walls.Count) return false;
        wall = walls[id];
        return wall != null && !wall.hasDied;
    }
}

/// <summary>
/// Stateless grid queries: line-of-sight (thin and projectile-width), shooter target visibility
/// helpers, and A* for specific A→B paths. Everything picks its grid by QUERIER POSITION (dungeon /
/// base / neither = "no walls"), so shared enemy code calls these unconditionally anywhere.
/// </summary>
public static class MinePath
{
    /// <summary>The grid governing a position: dungeon → MineField, base → the base D-grid (walls
    /// block sight and are never chewed by LOS/A*), neither ready → null ("no walls").</summary>
    static IPathGrid GridFor(Vector2 pos)
    {
        if (!PathZone.AtBase(pos))
            return MineGridAdapter.i.Ready ? (IPathGrid)MineGridAdapter.i : null;
        return BasePathGrid.Ready ? BasePathGrid.blocked : null;
    }

    // ---------------------------------------------------------------- line of sight

    /// <summary>
    /// True when the straight segment a→b is clear of intervening solids (grid DDA traversal — pure
    /// array reads, no Physics2D, so it is immune to the stale-collider-in-builds problem). Targets
    /// that ARE solid things (walls, buildings — whose transforms sit in the middle of their own
    /// solid footprints) count as visible via the SOLID-TAIL rule: once the ray enters solid cells
    /// it must STAY solid all the way to the target ("I'm looking at the target's own mass").
    /// Solid-then-open still blocks — that was a wall with space behind it. The eye's own cell is
    /// tolerated too (bodies get pushed onto the rounded padding cells of big footprints).
    /// No grid governs the eye → always true.
    /// </summary>
    public static bool LineOfSight(Vector2 a, Vector2 b)
    {
        var g = GridFor(a);
        if (g == null) return true;
        return LineOfSight(g, a, b);
    }

    static bool LineOfSight(IPathGrid g, Vector2 a, Vector2 b)
    {
        var c = g.WorldToCell(a);
        var cEnd = g.WorldToCell(b);
        if (c == cEnd) return true;

        float cs = g.CellSize;
        Vector2 d = b - a;
        int stepX = d.x > 0f ? 1 : -1;
        int stepY = d.y > 0f ? 1 : -1;
        Vector2 cellMin = (Vector2)g.CellCenterWorld(c) - 0.5f * cs * Vector2.one;
        float tMaxX = d.x != 0f ? (((d.x > 0f ? cellMin.x + cs : cellMin.x) - a.x) / d.x) : float.PositiveInfinity;
        float tMaxY = d.y != 0f ? (((d.y > 0f ? cellMin.y + cs : cellMin.y) - a.y) / d.y) : float.PositiveInfinity;
        float tDeltaX = d.x != 0f ? cs / Mathf.Abs(d.x) : float.PositiveInfinity;
        float tDeltaY = d.y != 0f ? cs / Mathf.Abs(d.y) : float.PositiveInfinity;

        bool solidRun = false;   // ray has entered solid cells — only the target's own mass may follow
        int guard = 4096;        // far beyond any real grid diagonal
        while (guard-- > 0)
        {
            if (tMaxX < tMaxY) { tMaxX += tDeltaX; c.x += stepX; }
            else               { tMaxY += tDeltaY; c.y += stepY; }
            if (c == cEnd) return !solidRun || g.IsSolid(c);   // inside-solid approach must END in solid (the target), not see through a wall to open ground
            if (g.IsSolid(c)) solidRun = true;
            else if (solidRun) return false;                   // solid then open — a wall, not the target's hull
        }
        return true;
    }

    /// <summary>
    /// Line-of-sight for something with WIDTH — a projectile or a body that must fit through the
    /// gap, not just a sightline. Casts the centre ray plus one ray along each edge (offset
    /// ±radius perpendicular to the flight line); all three must be clear.
    /// </summary>
    public static bool LineOfSightWide(Vector2 a, Vector2 b, float radius)
    {
        if (radius <= 0f) return LineOfSight(a, b);
        Vector2 d = b - a;
        if (d.sqrMagnitude < 1e-6f) return LineOfSight(a, b);
        Vector2 perp = new Vector2(-d.y, d.x).normalized * radius;
        return LineOfSight(a, b)
            && LineOfSight(a + perp, b + perp)
            && LineOfSight(a - perp, b - perp);
    }

    // ------------------------------------------------- shooter target-visibility helpers

    /// <summary>
    /// Where to LOOK AT / SHOOT AT on a target: normally its transform position — but a registered
    /// WALL's transform can sit far from its span (EnergyWall parents to its Force Field tower), so
    /// wall targets aim at their nearest registered cell instead. Dictionary lookup, no GetComponent
    /// — fine to call per frame.
    /// </summary>
    public static Vector2 AimPoint(Transform target, Vector2 from)
    {
        if (target == null) return from;
        if (PathZone.AtBase(from) && BasePathGrid.Ready &&
            BaseBlockMap.TryGetNearestWallPoint(target, from, out Vector2 p))
            return p;
        return target.position;
    }

    /// <summary>Can a shooter at <paramref name="eye"/> see this target? (radius &gt; 0 = must also
    /// fit a projectile that wide.) Wall targets are checked against their nearest span point, so
    /// "can I see the wall I'm breaking" works regardless of where its transform sits.</summary>
    public static bool CanSee(Vector2 eye, Transform target, float projectileRadius = 0f)
        => target != null && LineOfSightWide(eye, AimPoint(target, eye), projectileRadius);

    /// <summary>The first target in the list with a clear shot from <paramref name="eye"/>, or null. Null/dead entries are skipped.</summary>
    public static Transform FirstVisible(Vector2 eye, IList<Transform> targets, float projectileRadius = 0f)
    {
        if (targets == null) return null;
        for (int i = 0; i < targets.Count; i++)
            if (targets[i] != null && LineOfSightWide(eye, targets[i].position, projectileRadius))
                return targets[i];
        return null;
    }

    /// <summary>
    /// Collect every target with a clear shot into <paramref name="results"/> (cleared first; pass
    /// null to just count). Returns the count — 0 answers "are there walls in the way of ALL my
    /// possible targets", i.e. reposition instead of shooting.
    /// </summary>
    public static int VisibleTargets(Vector2 eye, IList<Transform> targets, List<Transform> results, float projectileRadius = 0f)
    {
        results?.Clear();
        if (targets == null) return 0;
        int n = 0;
        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i] == null || !LineOfSightWide(eye, targets[i].position, projectileRadius)) continue;
            n++;
            results?.Add(targets[i]);
        }
        return n;
    }

    /// <summary>True when at least one target is visible from <paramref name="eye"/>.</summary>
    public static bool AnyVisible(Vector2 eye, IList<Transform> targets, float projectileRadius = 0f)
        => FirstVisible(eye, targets, projectileRadius) != null;

    // ---------------------------------------------------------------- A* (specific A→B)

    // One scratch context per grid so alternating dungeon/base queries don't thrash re-allocation.
    static readonly AStarContext ctxMine = new AStarContext();
    static readonly AStarContext ctxBase = new AStarContext();

    /// <summary>
    /// A* from one world position to another over open cells of the governing grid (8-way, no
    /// corner cutting, solid cells — including chewable walls — are impassable). Fills
    /// <paramref name="outPath"/> with world-space cell-centre waypoints from start to goal and
    /// returns true on success. This is the per-agent-per-target tool (locked targets, move
    /// commands); crowds sharing a destination should use a flow field instead. No governing grid →
    /// trivially succeeds with a straight two-point "path".
    /// </summary>
    public static bool FindPath(Vector2 from, Vector2 to, List<Vector2> outPath, int maxExpansions = 4096)
    {
        outPath.Clear();
        var g = GridFor(from);
        if (g == null) { outPath.Add(from); outPath.Add(to); return true; }
        var ctx = PathZone.AtBase(from) ? ctxBase : ctxMine;
        return ctx.FindPath(g, from, to, outPath, maxExpansions);
    }

    /// <summary>
    /// Convenience for steering-style movement: the direction to move RIGHT NOW to head toward a
    /// specific destination along a real path (A* under the hood — use for a locked target, not a
    /// crowd). False = unreachable; fall back to direct steering or give up.
    /// </summary>
    static readonly List<Vector2> _stepScratch = new List<Vector2>();
    public static bool StepToward(Vector2 from, Vector2 to, out Vector2 dir, int maxExpansions = 4096)
    {
        dir = Vector2.zero;
        if (!FindPath(from, to, _stepScratch, maxExpansions)) return false;
        // waypoint 0 is our own cell centre — skip it, aim at the first real step ahead
        for (int i = 1; i < _stepScratch.Count; i++)
        {
            Vector2 d = _stepScratch[i] - from;
            if (d.sqrMagnitude > 0.01f) { dir = d.normalized; return true; }
        }
        Vector2 tail = to - from;
        if (tail.sqrMagnitude > 0.01f) dir = tail.normalized;   // same cell as goal — home in directly
        return true;
    }

    /// <summary>
    /// Drop intermediate waypoints a body of this radius can skip via direct line-of-sight —
    /// straightens A* staircase paths into natural diagonals. In-place.
    /// </summary>
    public static void SimplifyPath(List<Vector2> path, float radius = 0f)
    {
        int write = 0;
        for (int i = 0; i < path.Count - 1;)
        {
            path[write++] = path[i];
            int j = path.Count - 1;
            while (j > i + 1 && !LineOfSightWide(path[i], path[j], radius)) j--;
            i = j;
        }
        if (path.Count > 0) path[write++] = path[path.Count - 1];
        path.RemoveRange(write, path.Count - write);
    }

    /// <summary>Stamped scratch arrays + binary heap for one grid's A* queries — reused across
    /// queries (zero steady-state allocation), cleared implicitly by bumping the stamp.</summary>
    class AStarContext
    {
        int[] gScore, visitStamp, cameFrom, heapIdx, heap, fScore;
        int stamp, heapCount;

        static readonly (int dx, int dy, int cost)[] N8 =
        {
            (1, 0, 10), (-1, 0, 10), (0, 1, 10), (0, -1, 10),
            (1, 1, 14), (1, -1, 14), (-1, 1, 14), (-1, -1, 14),
        };

        public bool FindPath(IPathGrid g, Vector2 from, Vector2 to, List<Vector2> outPath, int maxExpansions)
        {
            var rect = g.CellRect;
            int w = rect.width, size = w * rect.height;
            if (size <= 0) return false;
            if (gScore == null || gScore.Length != size)
            {
                gScore = new int[size]; visitStamp = new int[size]; cameFrom = new int[size];
                fScore = new int[size]; heapIdx = new int[size]; heap = new int[size];
                stamp = 0;
            }
            stamp++;

            var cs = g.WorldToCell(from);
            var cg = g.WorldToCell(to);
            bool InRect(Vector3Int c) => c.x >= rect.xMin && c.x < rect.xMax && c.y >= rect.yMin && c.y < rect.yMax;
            if (!InRect(cs) || !InRect(cg) || g.IsSolid(cs) || g.IsSolid(cg)) return false;
            int Li(int x, int y) => (x - rect.xMin) + (y - rect.yMin) * w;

            int start = Li(cs.x, cs.y), goal = Li(cg.x, cg.y);
            int Heuristic(int li)
            {
                int dx = Mathf.Abs(li % w - (goal % w)), dy = Mathf.Abs(li / w - (goal / w));
                return dx > dy ? 14 * dy + 10 * (dx - dy) : 14 * dx + 10 * (dy - dx);   // octile
            }

            heapCount = 0;
            gScore[start] = 0; fScore[start] = Heuristic(start); cameFrom[start] = -1; visitStamp[start] = stamp;
            HeapPush(start);

            int expansions = 0;
            while (heapCount > 0 && expansions++ < maxExpansions)
            {
                int cur = HeapPop();
                if (cur == goal)
                {
                    // reconstruct (goal→start), then reverse into world waypoints
                    for (int li = goal; li != -1; li = cameFrom[li]) outPath.Add(Vector2.zero);   // reserve
                    int at = outPath.Count - 1;
                    for (int li = goal; li != -1; li = cameFrom[li])
                        outPath[at--] = g.CellCenterWorld(new Vector3Int(rect.xMin + li % w, rect.yMin + li / w, 0));
                    return true;
                }
                int cx = rect.xMin + cur % w, cy = rect.yMin + cur / w;
                foreach (var (dx, dy, cost) in N8)
                {
                    var n = new Vector3Int(cx + dx, cy + dy, 0);
                    if (!InRect(n) || g.IsSolid(n)) continue;
                    if (dx != 0 && dy != 0 &&
                        (g.IsSolid(new Vector3Int(cx + dx, cy, 0)) || g.IsSolid(new Vector3Int(cx, cy + dy, 0))))
                        continue;   // no corner cutting
                    int ni = Li(n.x, n.y);
                    int gsc = gScore[cur] + cost;
                    if (visitStamp[ni] == stamp && gsc >= gScore[ni]) continue;
                    bool fresh = visitStamp[ni] != stamp;
                    visitStamp[ni] = stamp; gScore[ni] = gsc; fScore[ni] = gsc + Heuristic(ni); cameFrom[ni] = cur;
                    if (fresh || heapIdx[ni] < 0) HeapPush(ni); else HeapUp(heapIdx[ni]);
                }
            }
            return false;
        }

        // binary min-heap on fScore
        void HeapPush(int li)
        {
            heap[heapCount] = li; heapIdx[li] = heapCount; heapCount++;
            HeapUp(heapCount - 1);
        }
        int HeapPop()
        {
            int top = heap[0];
            heapIdx[top] = -1;
            heapCount--;
            if (heapCount > 0)
            {
                heap[0] = heap[heapCount]; heapIdx[heap[0]] = 0;
                HeapDown(0);
            }
            return top;
        }
        void HeapUp(int i)
        {
            while (i > 0)
            {
                int p = (i - 1) >> 1;
                if (fScore[heap[p]] <= fScore[heap[i]]) break;
                (heap[p], heap[i]) = (heap[i], heap[p]);
                heapIdx[heap[p]] = p; heapIdx[heap[i]] = i;
                i = p;
            }
        }
        void HeapDown(int i)
        {
            while (true)
            {
                int l = 2 * i + 1, r = l + 1, best = i;
                if (l < heapCount && fScore[heap[l]] < fScore[heap[best]]) best = l;
                if (r < heapCount && fScore[heap[r]] < fScore[heap[best]]) best = r;
                if (best == i) break;
                (heap[best], heap[i]) = (heap[i], heap[best]);
                heapIdx[heap[best]] = best; heapIdx[heap[i]] = i;
                i = best;
            }
        }
    }
}

/// <summary>
/// Keeps the standing DUNGEON flow fields fresh, and is the single query front door for enemy/ally
/// code: base-positioned queries route to <see cref="BasePathManager"/> automatically, so callers
/// never care which dimension they're in. Enemy AI goes through ONE call — <see cref="Decide"/> —
/// which answers "who do I attack and which way do I walk" from the unit's pathing personality
/// (wander + preference flags); the steering-only Dir* helpers remain for spawn facing, map pull
/// and ally units. Created lazily on first query (no scene edit — the editor
/// clobbers hand-edited YAML anyway); each field rebuilds on a tick ONLY while agents are actively
/// querying it, so an empty or frozen dungeon costs zero. Ally-side targets beyond the player
/// (dungeon buildings, drones) register/unregister themselves — with an optional priority penalty
/// in cells so e.g. drones read as "further" than they are and don't hoover up every enemy's aggro.
/// </summary>
public class MinePathManager : MonoBehaviour
{
    public static MinePathManager i;

    /// <summary>Default wall-detour willingness when a caller doesn't pass a per-unit value.</summary>
    public const float DefaultWander = 1.5f;

    [Tooltip("Seconds between rebuilds of each actively-queried flow field.")]
    public float rebuildInterval = 0.15f;
    [Tooltip("Stop rebuilding a field this long after its last query.")]
    public float idleTimeout = 1.5f;

    public readonly MineFlowField toAllyTargets = new MineFlowField();   // nearest of player+registered
    public readonly MineFlowField toPlayer = new MineFlowField();        // player specifically
    public readonly MineFlowField toEnemies = new MineFlowField();       // nearest live enemy

    // ally-side seeds beyond the player — future buildings/drones register here
    static readonly List<(Transform t, int cost)> allyTargets = new List<(Transform, int)>();

    float builtAllyT = float.NegativeInfinity, builtPlayerT = float.NegativeInfinity, builtEnemyT = float.NegativeInfinity;
    float queryAllyT = float.NegativeInfinity, queryPlayerT = float.NegativeInfinity, queryEnemyT = float.NegativeInfinity;
    readonly List<MineFlowField.Seed> seedScratch = new List<MineFlowField.Seed>();

    static MinePathManager Ensure()
    {
        if (i == null)
        {
            i = new GameObject("MinePathManager").AddComponent<MinePathManager>();
        }
        return i;
    }

    void Awake() { if (i == null) i = this; }
    void OnDestroy() { if (i == this) i = null; }

    /// <summary>An ally-side thing DUNGEON enemies should path to (dungeon building, drone). Cost =
    /// priority penalty in cells (0 = as attractive as the player; 8 = treated as 8 cells further
    /// away). Base-side targets are found automatically (Building registry / allies parent).</summary>
    public static void RegisterAllyTarget(Transform t, int priorityCost = 0)
    {
        if (t == null) return;
        UnregisterAllyTarget(t);
        allyTargets.Add((t, priorityCost));
    }

    public static void UnregisterAllyTarget(Transform t)
    {
        for (int k = allyTargets.Count - 1; k >= 0; k--)
            if (allyTargets[k].t == t || allyTargets[k].t == null) allyTargets.RemoveAt(k);
    }

    static bool DungeonReady => MineField.i != null && MineField.i.Built;

    // ------------------------------------------------------------------ query API

    /// <summary>Steering direction toward the nearest ally-side target (player, units, buildings).
    /// False = unreachable/off-grid — fall back to direct steering.</summary>
    public static bool DirToNearestAllyTarget(Vector2 pos, out Vector2 dir)
        => DirToNearestAllyTarget(pos, DefaultWander, out dir);

    /// <summary>Per-unit wander overload: at the base, <paramref name="wander"/> decides detour vs
    /// chew-through-the-wall (see <see cref="Unit.wander"/>). In the dungeon walls aren't chewable,
    /// so wander is moot.</summary>
    public static bool DirToNearestAllyTarget(Vector2 pos, float wander, out Vector2 dir)
    {
        if (PathZone.AtBase(pos)) return BasePathManager.DirToNearestAllyTarget(pos, wander, out dir);
        dir = Vector2.zero;
        if (!DungeonReady) return false;
        var m = Ensure();
        m.queryAllyT = Time.time;
        if (Time.time - m.builtAllyT > m.rebuildInterval) m.RebuildAlly();
        return m.toAllyTargets.TryGetStepDir(pos, out dir);
    }

    /// <summary>Steering direction toward the PLAYER specifically, ignoring other ally targets.
    /// Dungeon-only (base callers should use DirToNearestAllyTarget).</summary>
    public static bool DirToPlayer(Vector2 pos, out Vector2 dir)
    {
        dir = Vector2.zero;
        if (PathZone.AtBase(pos) || !DungeonReady) return false;
        var m = Ensure();
        m.queryPlayerT = Time.time;
        if (Time.time - m.builtPlayerT > m.rebuildInterval) m.RebuildPlayer();
        return m.toPlayer.TryGetStepDir(pos, out dir);
    }

    /// <summary>Steering direction toward the nearest live enemy (for ally units/drones hunting
    /// whatever's closest). Walls and buildings are never chewed by allies.</summary>
    public static bool DirToNearestEnemy(Vector2 pos, out Vector2 dir)
    {
        if (PathZone.AtBase(pos)) return BasePathManager.DirToNearestEnemy(pos, out dir);
        dir = Vector2.zero;
        if (!DungeonReady) return false;
        var m = Ensure();
        m.queryEnemyT = Time.time;
        if (Time.time - m.builtEnemyT > m.rebuildInterval) m.RebuildEnemies();
        return m.toEnemies.TryGetStepDir(pos, out dir);
    }

    /// <summary>
    /// THE per-enemy motion + targeting decision: writes WHO to attack into <see cref="Unit.target"/>
    /// and returns WHICH WAY to walk, in either dimension, driven by the unit's own pathing
    /// personality (wanderEff + preference flags). Target and direction come from the same field
    /// snapshot, so steering can never disagree with intent — and there is NO range and NO memory:
    /// the answer is always the current best objective, which is how enemies both never go
    /// targetless (something is returned while any ally-side thing lives) and reprioritise by
    /// themselves (a closer thing simply becomes the answer). At the base the wander decision
    /// applies per class: obstructed and unwilling to detour → the wall on the route IS the target.
    /// Off the field's rect (map-edge spawn), the fields are read from the nearest in-rect cell —
    /// newborns still pick their true best target by their own criteria and march at that entry
    /// point. dir == zero = arrived — approach the target straight. False = nothing left to fight
    /// anywhere (hold still; target is nulled).
    /// </summary>
    public static bool Decide(Unit u, out Vector2 dir)
    {
        dir = Vector2.zero;
        if (u == null) return false;
        u.target = null;
        Vector2 pos = u.transform.position;
        var player = CharacterScript.CS;

        if (PathZone.AtBase(pos))
        {
            if (BasePathManager.Decide(pos, u.wanderEff, u.crowdAversion, u.wallExploitIQ,
                    u.preferCharacter, u.preferBuildings, u.preferWalls, out Transform tgt, out dir))
            {
                u.target = tgt;
                return true;
            }
            // grid not built yet (or nothing seeded) — march at the player with intent
            if (player != null && PathZone.AtBase(player.transform.position))
            {
                u.target = player.transform;
                Vector2 d = (Vector2)player.transform.position - pos;
                if (d.sqrMagnitude > 1e-4f) dir = d.normalized;
                return true;
            }
            return false;
        }

        if (!DungeonReady) return false;
        var m = Ensure();
        if (u.preferCharacter && player != null)
        {
            m.queryPlayerT = Time.time;
            if (Time.time - m.builtPlayerT > m.rebuildInterval) m.RebuildPlayer();
            if (m.toPlayer.TryGetStepDir(pos, out dir))
            {
                u.target = player.transform;
                return true;
            }
            dir = Vector2.zero;
        }
        m.queryAllyT = Time.time;
        if (Time.time - m.builtAllyT > m.rebuildInterval) m.RebuildAlly();
        if (m.toAllyTargets.TryGetNearestSeed(pos, out Transform o, out _) && o != null)
        {
            u.target = o;
            m.toAllyTargets.TryGetStepDir(pos, out dir);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Editor debugging: call from OnDrawGizmosSelected to draw the route the unit's fields will
    /// walk it along (cyan cell trail) and its current objective's aim point (magenta sphere) —
    /// select an enemy in play mode with Scene-view Gizmos on. Reads the same decision the unit
    /// makes, so what you see is what it does.
    /// </summary>
    static readonly List<Vector2> routeScratch = new List<Vector2>();
    public static void DrawRouteGizmo(Unit u)
    {
        if (u == null || !Application.isPlaying) return;
        Vector2 pos = u.transform.position;
        MineFlowField field = null;
        Vector2 evalPos = pos;
        Transform tgt = u.target;

        if (PathZone.AtBase(pos))
        {
            BasePathManager.Choose(pos, u.wanderEff, u.crowdAversion, u.wallExploitIQ,
                u.preferCharacter, u.preferBuildings, u.preferWalls, out tgt, out _, out field, out evalPos);
        }
        else if (DungeonReady && i != null)
        {
            field = u.preferCharacter && i.toPlayer.DistanceCells(pos) >= 0 ? i.toPlayer : i.toAllyTargets;
        }

        routeScratch.Clear();
        Vector3 tail = pos;
        if (field != null && field.TraceRoute(evalPos, routeScratch) > 0)
        {
            Gizmos.color = Color.cyan;
            for (int k = 0; k < routeScratch.Count; k++)
            {
                Gizmos.DrawLine(tail, routeScratch[k]);
                tail = routeScratch[k];
            }
        }
        if (tgt != null)
        {
            Vector2 aim = MinePath.AimPoint(tgt, pos);
            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(tail, aim);
            Gizmos.DrawWireSphere(aim, 0.35f);

            // building target: box its REGISTERED footprint (what the fields actually seed) —
            // if the yellow box isn't on the building, its registration is stale/displaced,
            // which is exactly the phantom-seed failure the route trail exposes
            for (int b = 0; b < Building.buildings.Count; b++)
            {
                var bld = Building.buildings[b];
                if (bld == null || bld.transform != tgt) continue;
                if (bld.TryGetPathFootprint(out Vector2Int a, out Vector2Int sc))
                {
                    float cs = BaseBlockMap.CellSize;
                    Gizmos.color = Color.yellow;
                    Gizmos.DrawWireCube(
                        new Vector3((a.x + sc.x * 0.5f) * cs, (a.y + sc.y * 0.5f) * cs, 0f),
                        new Vector3(sc.x * cs, sc.y * cs, 0.1f));
                }
                break;
            }
        }
    }

    /// <summary>Which live enemy is nearest by path (for ally units/drones), and how far in cells.</summary>
    public static bool TryNearestEnemy(Vector2 pos, out Transform target, out int distCells)
    {
        if (PathZone.AtBase(pos)) return BasePathManager.TryNearestEnemy(pos, out target, out distCells);
        target = null; distCells = -1;
        if (!DungeonReady) return false;
        var m = Ensure();
        m.queryEnemyT = Time.time;
        if (Time.time - m.builtEnemyT > m.rebuildInterval) m.RebuildEnemies();
        return m.toEnemies.TryGetNearestSeed(pos, out target, out distCells) && target != null;
    }

    /// <summary>Path distance in cells from a position to the player (-1 = sealed off). Doubles as a
    /// cheap "is the player reachable at all" test for aggro/spawn logic. Dungeon-only.</summary>
    public static int PlayerDistanceCells(Vector2 pos)
    {
        if (PathZone.AtBase(pos) || !DungeonReady) return -1;
        var m = Ensure();
        m.queryPlayerT = Time.time;
        if (Time.time - m.builtPlayerT > m.rebuildInterval) m.RebuildPlayer();
        return m.toPlayer.DistanceCells(pos);
    }

    // ------------------------------------------------------------------ rebuilds

    void Update()
    {
        if (!DungeonReady) return;
        // keep actively-used fields warm even between queries so a query never sees stale-by-a-lot data
        if (Time.time - queryAllyT < idleTimeout && Time.time - builtAllyT > rebuildInterval) RebuildAlly();
        if (Time.time - queryPlayerT < idleTimeout && Time.time - builtPlayerT > rebuildInterval) RebuildPlayer();
        if (Time.time - queryEnemyT < idleTimeout && Time.time - builtEnemyT > rebuildInterval) RebuildEnemies();
    }

    void RebuildAlly()
    {
        builtAllyT = Time.time;
        seedScratch.Clear();
        if (CharacterScript.CS != null)
            seedScratch.Add(new MineFlowField.Seed(CharacterScript.CS.transform.position, 0, CharacterScript.CS.transform));
        for (int k = allyTargets.Count - 1; k >= 0; k--)
        {
            var (t, cost) = allyTargets[k];
            if (t == null) { allyTargets.RemoveAt(k); continue; }
            if (t.gameObject.activeInHierarchy) seedScratch.Add(new MineFlowField.Seed(t.position, cost, t));
        }
        toAllyTargets.Rebuild(MineGridAdapter.i, seedScratch);
    }

    void RebuildPlayer()
    {
        builtPlayerT = Time.time;
        seedScratch.Clear();
        if (CharacterScript.CS != null)
            seedScratch.Add(new MineFlowField.Seed(CharacterScript.CS.transform.position, 0, CharacterScript.CS.transform));
        toPlayer.Rebuild(MineGridAdapter.i, seedScratch);
    }

    void RebuildEnemies()
    {
        builtEnemyT = Time.time;
        seedScratch.Clear();
        var parent = GS.FindParent(GS.Parent.enemies);
        if (parent != null)
            for (int k = 0; k < parent.childCount; k++)
            {
                var ch = parent.GetChild(k);
                if (ch.gameObject.activeInHierarchy) seedScratch.Add(new MineFlowField.Seed(ch.position, 0, ch));
            }
        toEnemies.Rebuild(MineGridAdapter.i, seedScratch);
    }
}
