using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Keeps the standing BASE flow fields fresh and makes the per-unit ATTACK/STEERING DECISION.
/// Reached through <see cref="MinePathManager.Decide"/> (which dispatches by querier position) —
/// enemy code never calls this directly unless it wants base-specific behavior.
///
/// Target CLASSES, each a W/D field pair over one registry (lazily created, query-gated rebuilds):
///  - all:       player + ally units + non-wall buildings — the no-preference pool
///  - character: the player alone
///  - buildings: non-wall buildings alone
///  - walls:     every registered wall cell (single field — here the walls ARE the destination)
///  - enemies:   for ally units (walls blocked — allies never chew)
///
/// W = walls CHEWABLE (the "through" view: distance includes chew costs priced by wall HP, and the
/// gate lookup names the first wall on the route). D = walls BLOCKED (the detour view). The
/// per-unit wander decision picks between them: detour when dD &lt;= wander * dW; otherwise steer
/// into the wall and the wall on the route IS the target. Unit preference flags pick the CLASS —
/// absolute priority: the nearest reachable member of the preferred class(es) wins regardless of
/// closer non-preferred targets, falling back to the 'all' pool only when no preferred target is
/// reachable. There is NO range and NO memory anywhere in this: the answer is always the current
/// best objective, so enemies can never be targetless while anything ally-side lives, and
/// reprioritising onto something closer happens by itself.
///
/// Dog-pile variety comes from per-unit wander jitter (Unit.wanderEff) plus per-seed crowding
/// penalties (targets already claimed by many enemies read as a few cells further away).
/// </summary>
public class BasePathManager : MonoBehaviour
{
    public static BasePathManager i;

    [Tooltip("Stop rebuilding a field this long after its last query.")]
    public float idleTimeout = 1.5f;

    /// <summary>Dog-pile avoidance: penalize seeds by how many enemies already routed to them.</summary>
    public static bool crowding = true;
    /// <summary>+1 cell of seed cost per this many enemies already assigned to a target.</summary>
    public static int crowdDivisor = 2;
    public static int crowdMaxPenalty = 8;

    // One W/D pair per target class. Deliberately different rebuild periods so the classes drift
    // out of phase instead of all rebuilding on the same frame.
    class TargetClass
    {
        public readonly MineFlowField W = new MineFlowField();   // walls chewable (through view)
        public readonly MineFlowField D = new MineFlowField();   // walls blocked (detour view)
        public readonly float interval;
        public float builtT = float.NegativeInfinity, queryT = float.NegativeInfinity;
        public TargetClass(float intervalP) { interval = intervalP; }
    }

    readonly TargetClass all = new TargetClass(0.15f);
    readonly TargetClass character = new TargetClass(0.17f);
    readonly TargetClass buildingsC = new TargetClass(0.19f);
    readonly MineFlowField toWalls = new MineFlowField();     // walls blocked; wall cells are the seeds
    readonly MineFlowField toEnemies = new MineFlowField();   // walls blocked
    float builtWallsT = float.NegativeInfinity, queryWallsT = float.NegativeInfinity;
    float builtEnemyT = float.NegativeInfinity, queryEnemyT = float.NegativeInfinity;
    const float WallsInterval = 0.23f, EnemiesInterval = 0.21f;

    readonly List<MineFlowField.Seed> seedScratch = new List<MineFlowField.Seed>();
    readonly Dictionary<Transform, int> crowd = new Dictionary<Transform, int>();

    static BasePathManager Ensure()
    {
        if (i == null)
        {
            i = new GameObject("BasePathManager").AddComponent<BasePathManager>();
        }
        return i;
    }

    void Awake()
    {
        if (i == null) i = this;
        MapManager.OnUpdateMap += BasePathGrid.InvalidateMapCache;
    }

    void OnDestroy()
    {
        if (i == this) i = null;
        MapManager.OnUpdateMap -= BasePathGrid.InvalidateMapCache;
    }

    static bool Ready => BasePathGrid.Ready;

    // ------------------------------------------------------------------ THE decision

    /// <summary>
    /// The single motion+targeting answer for one unit at the base: WHO to attack and WHICH WAY to
    /// walk, taken from the same field snapshot so they can never disagree. Preferred classes are
    /// tried first (nearest reachable member wins); no reachable preferred target falls back to the
    /// 'all' pool. Within the winning class the wander decision applies: detour around walls when
    /// the detour is cheap enough, otherwise the wall on the route becomes the target and the
    /// direction steers into its face. dir == zero means arrived (or boxed in) — approach the
    /// target directly. False = position off-grid or nothing reachable at all.
    /// </summary>
    public static bool Decide(Vector2 pos, float wander, bool preferCharacter, bool preferBuildings,
        bool preferWalls, out Transform target, out Vector2 dir)
        => Choose(pos, wander, preferCharacter, preferBuildings, preferWalls, out target, out dir, out _, out _);

    /// <summary>
    /// <see cref="Decide"/> plus WHICH field won and the evaluation position it was read at —
    /// the debug route gizmo walks that field from that point. The evaluation position differs
    /// from <paramref name="pos"/> only for off-grid queriers (map-edge spawns), which are clamped
    /// to their nearest in-rect cell so newborns still pick their true best target by preference;
    /// their dir then marches them at that entry point until the grid governs them.
    /// </summary>
    public static bool Choose(Vector2 pos, float wander, bool preferCharacter, bool preferBuildings,
        bool preferWalls, out Transform target, out Vector2 dir, out MineFlowField field, out Vector2 evalPos)
    {
        target = null;
        dir = Vector2.zero;
        field = null;
        evalPos = pos;
        if (!Ready) return false;
        var m = Ensure();
        bool offGrid = BasePathGrid.ClampToGrid(pos, out evalPos);
        int bestEff = int.MaxValue;
        bool any = false;
        if (preferCharacter) any |= m.EvalClass(m.character, evalPos, wander, ref bestEff, ref target, ref dir, ref field);
        if (preferBuildings) any |= m.EvalClass(m.buildingsC, evalPos, wander, ref bestEff, ref target, ref dir, ref field);
        if (preferWalls) any |= m.EvalWalls(evalPos, ref bestEff, ref target, ref dir, ref field);
        if (!any) any = m.EvalClass(m.all, evalPos, wander, ref bestEff, ref target, ref dir, ref field);
        if (!any || target == null) return false;
        if (offGrid)
        {
            Vector2 d = evalPos - pos;
            if (d.sqrMagnitude > 1e-4f) dir = d.normalized;
        }
        return true;
    }

    // Evaluate one class as a candidate answer; claims the ref slots only when it beats bestEff.
    bool EvalClass(TargetClass tc, Vector2 pos, float wander, ref int bestEff, ref Transform target, ref Vector2 dir, ref MineFlowField field)
    {
        Touch(tc);
        int dW = tc.W.DistanceCells(pos);
        int dD = tc.D.DistanceCells(pos);
        // the wander decision: dW <= dD always (W is a relaxation of D); dD == -1 = no wall-free
        // route exists (sealed in, or standing ON a wall cell mid-chew — free hysteresis) -> chew
        if (dD >= 0 && dW >= 0 && dD <= wander * dW)
        {
            if (dD >= bestEff) return false;
            if (!tc.D.TryGetNearestSeed(pos, out Transform o, out _) || o == null) return false;
            tc.D.TryGetStepDir(pos, out Vector2 sd);
            bestEff = dD; target = o; dir = sd; field = tc.D;
            return true;
        }
        // chew (or no wall intervenes): the first wall on the route is the target — ranged enemies
        // volley the wall they're breaking instead of chasing a ghost behind it
        if (dW < 0 || dW >= bestEff) return false;
        Transform tgt = tc.W.TryGetGate(pos, out LifeScript wall)
            ? wall.transform
            : (tc.W.TryGetNearestSeed(pos, out Transform o2, out _) ? o2 : null);
        if (tgt == null) return false;
        tc.W.TryGetStepDir(pos, out Vector2 sd2);
        bestEff = dW; target = tgt; dir = sd2; field = tc.W;
        return true;
    }

    // The walls class: nearest wall span cell is the destination itself (siege units).
    bool EvalWalls(Vector2 pos, ref int bestEff, ref Transform target, ref Vector2 dir, ref MineFlowField field)
    {
        queryWallsT = Time.time;
        if (Time.time - builtWallsT > WallsInterval) RebuildWalls();
        int d = toWalls.DistanceCells(pos);
        if (d < 0 || d >= bestEff) return false;
        if (!toWalls.TryGetNearestSeed(pos, out Transform o, out _) || o == null) return false;
        toWalls.TryGetStepDir(pos, out Vector2 sd);
        bestEff = d; target = o; dir = sd; field = toWalls;
        return true;
    }

    // ------------------------------------------------------------------ steering-only API

    /// <summary>
    /// Steering toward the best 'all'-pool target under this wander, with no preference or target
    /// identity — the generic route direction for spawn facing and the map-edge pull. Enemy AI
    /// proper goes through <see cref="Decide"/>. False = off-grid/no targets.
    /// </summary>
    public static bool DirToNearestAllyTarget(Vector2 pos, float wander, out Vector2 dir)
    {
        dir = Vector2.zero;
        if (!Ready) return false;
        var m = Ensure();
        m.Touch(m.all);
        bool offGrid = BasePathGrid.ClampToGrid(pos, out Vector2 evalPos);
        int dW = m.all.W.DistanceCells(evalPos);
        int dD = m.all.D.DistanceCells(evalPos);
        bool detour = dD >= 0 && dW >= 0 && dD <= wander * dW;
        if (!(detour ? m.all.D : m.all.W).TryGetStepDir(evalPos, out dir)) return false;
        if (offGrid)
        {
            Vector2 d = evalPos - pos;
            if (d.sqrMagnitude > 1e-4f) dir = d.normalized;   // march at the grid entry point first
        }
        return true;
    }

    /// <summary>Steering direction toward the nearest live enemy (ally units; walls/buildings impassable).</summary>
    public static bool DirToNearestEnemy(Vector2 pos, out Vector2 dir)
    {
        dir = Vector2.zero;
        if (!Ready) return false;
        var m = Ensure();
        m.queryEnemyT = Time.time;
        if (Time.time - m.builtEnemyT > EnemiesInterval) m.RebuildEnemies();
        return m.toEnemies.TryGetStepDir(pos, out dir);
    }

    /// <summary>Which live enemy is nearest by path from here, and how far in cells.</summary>
    public static bool TryNearestEnemy(Vector2 pos, out Transform target, out int distCells)
    {
        target = null; distCells = -1;
        if (!Ready) return false;
        var m = Ensure();
        m.queryEnemyT = Time.time;
        if (Time.time - m.builtEnemyT > EnemiesInterval) m.RebuildEnemies();
        return m.toEnemies.TryGetNearestSeed(pos, out target, out distCells) && target != null;
    }

    // ------------------------------------------------------------------ rebuilds

    void Touch(TargetClass tc)
    {
        tc.queryT = Time.time;
        if (Time.time - tc.builtT > tc.interval) RebuildClass(tc);
    }

    void Update()
    {
        if (!Ready) return;
        float now = Time.time;
        MaybeRebuild(all, now);
        MaybeRebuild(character, now);
        MaybeRebuild(buildingsC, now);
        if (now - queryWallsT < idleTimeout && now - builtWallsT > WallsInterval) RebuildWalls();
        if (now - queryEnemyT < idleTimeout && now - builtEnemyT > EnemiesInterval) RebuildEnemies();
    }

    void MaybeRebuild(TargetClass tc, float now)
    {
        if (now - tc.queryT < idleTimeout && now - tc.builtT > tc.interval) RebuildClass(tc);
    }

    void RebuildClass(TargetClass tc)
    {
        bool incChar = tc == all || tc == character;
        bool incUnits = tc == all;
        bool incBuildings = tc == all || tc == buildingsC;

        // crowding census against the class's OLD W field, before reseeding: how many enemies are
        // currently routed to each owner? Their seed reads as a few cells further this rebuild.
        // (Pointless for the single-seed character class — every route shifts equally.)
        crowd.Clear();
        if (crowding && tc != character && tc.W.IsBuilt)
        {
            var enemies = GS.FindParent(GS.Parent.enemies);
            if (enemies != null)
                for (int k = 0; k < enemies.childCount; k++)
                {
                    var ch = enemies.GetChild(k);
                    if (!ch.gameObject.activeInHierarchy) continue;
                    if (tc.W.TryGetNearestSeed(ch.position, out Transform o, out _) && o != null)
                        crowd[o] = crowd.TryGetValue(o, out int n) ? n + 1 : 1;
                }
        }

        tc.builtT = Time.time;
        seedScratch.Clear();

        if (incChar && CharacterScript.CS != null && PathZone.AtBase(CharacterScript.CS.transform.position))
            seedScratch.Add(new MineFlowField.Seed(CharacterScript.CS.transform.position,
                CrowdCost(CharacterScript.CS.transform), CharacterScript.CS.transform));

        if (incUnits)
        {
            var allies = GS.FindParent(GS.Parent.allies);
            if (allies != null)
                for (int k = 0; k < allies.childCount; k++)
                {
                    var ch = allies.GetChild(k);
                    if (ch.gameObject.activeInHierarchy)
                        seedScratch.Add(new MineFlowField.Seed(ch.position, CrowdCost(ch), ch));
                }
        }

        if (incBuildings)
        {
            // Non-wall buildings are first-class targets. Seed EVERY footprint cell (not just the
            // centre) — the wave can't escape the interior of a large solid footprint, so the
            // perimeter cells must carry the seed outward; enemies then path into contact anywhere
            // on the hull. Footprints come from the building's own registration (world-quantized —
            // GridManager growth re-anchors its origin, so its anchorCell coords can go stale).
            float cs = BaseBlockMap.CellSize;
            for (int b = 0; b < Building.buildings.Count; b++)
            {
                var bld = Building.buildings[b];
                if (bld == null || !bld.gameObject.activeInHierarchy) continue;
                bld.EnsurePathFootprintCurrent();   // moved / stale-frame registrations re-anchor here
                if (!bld.TryGetPathFootprint(out Vector2Int anchor, out Vector2Int cells)) continue;
                int cost = CrowdCost(bld.transform);
                for (int x = 0; x < cells.x; x++)
                    for (int y = 0; y < cells.y; y++)
                        seedScratch.Add(new MineFlowField.Seed(
                            new Vector2((anchor.x + x + 0.5f) * cs, (anchor.y + y + 0.5f) * cs),
                            cost, bld.transform));
            }
        }

        tc.W.Rebuild(BasePathGrid.chewable, seedScratch);
        tc.D.Rebuild(BasePathGrid.blocked, seedScratch);   // identical seeds, one collection pass
    }

    int CrowdCost(Transform t)
        => crowding && t != null && crowd.TryGetValue(t, out int n)
            ? Mathf.Min(crowdMaxPenalty, n / Mathf.Max(1, crowdDivisor)) : 0;

    void RebuildWalls()
    {
        builtWallsT = Time.time;
        seedScratch.Clear();
        float cs = BaseBlockMap.CellSize;
        var owners = BaseBlockMap.WallOwners;
        for (int w = 0; w < owners.Count; w++)
        {
            var ls = owners[w];
            if (ls == null || ls.hasDied) continue;
            if (!BaseBlockMap.TryGetWallCells(ls, out List<Vector2Int> cells, out Transform tf)) continue;
            for (int k = 0; k < cells.Count; k++)
                seedScratch.Add(new MineFlowField.Seed(
                    new Vector2((cells[k].x + 0.5f) * cs, (cells[k].y + 0.5f) * cs), 0, tf));
        }
        toWalls.Rebuild(BasePathGrid.blocked, seedScratch);
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
        toEnemies.Rebuild(BasePathGrid.blocked, seedScratch);
    }
}
