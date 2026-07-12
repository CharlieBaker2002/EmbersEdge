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
///
/// PER-UNIT coefficients over shared fields (Unit.crowdAversion, Unit.wallExploitIQ): a penalty
/// baked into a shared field can't be rescaled per querier, so each biased lookup keeps a small
/// set of ANCHOR views — the penalty baked at coefficient 0 (blind), 1 (baseline) and 3.5 (extremist)
/// — and a unit's coefficient rescores the two bracketing anchors' candidates as
/// walk + coeff × penalty, taking the better. Exact on any anchor, a tight two-real-candidate
/// bracket in between; beyond 3.5 the top two anchors still rank correctly against each other.
/// Anchor views are query-gated like everything else, so an all-default population (coeff 1
/// everywhere) never builds or pays for the extra views.
/// </summary>
public class BasePathManager : MonoBehaviour
{
    public static BasePathManager i;

    [Tooltip("Stop rebuilding a field this long after its last query.")]
    public float idleTimeout = 1.5f;

    /// <summary>
    /// Dog-pile avoidance: penalize seeds by how many enemies already routed to them. This is THE
    /// swarm-vs-spread dial (wander is unrelated — it only picks chew-vs-detour). These knobs set
    /// the WORLD's census; each unit scales its own reaction via Unit.crowdAversion. More swarming
    /// = raise crowdDivisor / lower crowdMaxPenalty / crowding = false; more spreading = reverse.
    /// </summary>
    public static bool crowding = true;
    /// <summary>+1 cell of seed cost per this many enemies already assigned to a target.</summary>
    public static int crowdDivisor = 4;
    public static int crowdMaxPenalty = 3;

    /// <summary>Allies-parent children in this set are INVISIBLE to base enemy targeting (both the
    /// field seeding and the phase-walker straight-line pool). The base auto-seeds every ally, so
    /// this is the opt-OUT — the mirror of the dungeon's opt-in RegisterAllyTarget. Used by
    /// empty-handed drones (they only attract enemies while holding cargo) and dormant vehicle
    /// hulls. Static registry + domain-reload-off ⇒ needs the explicit reset below.</summary>
    public static readonly HashSet<Transform> UntargetableAllies = new HashSet<Transform>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetUntargetableAllies() => UntargetableAllies.Clear();

    enum Fam { All, Char, Bld }

    // One W/D pair per VIEW: a target family × a baked crowd-penalty coefficient (its anchor).
    // Deliberately different rebuild periods so live views drift out of phase instead of all
    // rebuilding on the same frame.
    class TargetClass
    {
        public readonly MineFlowField W = new MineFlowField();   // walls chewable (through view)
        public readonly MineFlowField D = new MineFlowField();   // walls blocked (detour view)
        public readonly float interval;
        public readonly float penCoeff;   // crowd penalty baked into seeds = round(penCoeff × base)
        public readonly Fam fam;
        public float builtT = float.NegativeInfinity, queryT = float.NegativeInfinity;
        // BASE (coefficient-1) crowd penalty per owner this rebuild — lets decide-time rescoring
        // un-bake the view's own coefficient and re-apply the unit's: walk + aversion × base
        public readonly Dictionary<Transform, int> penApplied = new Dictionary<Transform, int>();
        public TargetClass(float intervalP, float penCoeffP, Fam famP)
        { interval = intervalP; penCoeff = penCoeffP; fam = famP; }
    }

    // Wall-hunting views: seeds are the wall cells themselves, handicapped by remaining HP at the
    // view's anchor coefficient (0 = pure nearest, 1 = valued like the chew router, 3 = weakness
    // hunter). Shares the walls-blocked grid; steering never chews.
    class WallView
    {
        public readonly MineFlowField F = new MineFlowField();
        public readonly float hCoeff;
        public float builtT = float.NegativeInfinity, queryT = float.NegativeInfinity;
        public WallView(float c) { hCoeff = c; }
    }

    // anchor views per family, indexed 0 → coeff 0 (blind), 1 → coeff 1 (baseline), 2 → coeff 3.5.
    // character is census-free (single seed), so one view fills all three slots.
    TargetClass[] allViews;
    TargetClass[] charViews;
    TargetClass[] buildingViews;
    WallView[] wallViews;

    readonly MineFlowField toEnemies = new MineFlowField();   // walls blocked
    readonly Dictionary<Transform, int> wallPen = new Dictionary<Transform, int>();  // base HP handicap per wall
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
        allViews = new[]
        {
            new TargetClass(0.16f, 0f, Fam.All),
            new TargetClass(0.15f, 1f, Fam.All),
            new TargetClass(0.18f, 3.5f, Fam.All),
        };
        var ch = new TargetClass(0.17f, 1f, Fam.Char);
        charViews = new[] { ch, ch, ch };
        buildingViews = new[]
        {
            new TargetClass(0.2f, 0f, Fam.Bld),
            new TargetClass(0.19f, 1f, Fam.Bld),
            new TargetClass(0.22f, 3.5f, Fam.Bld),
        };
        wallViews = new[] { new WallView(0f), new WallView(1f), new WallView(3.5f) };
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
    /// phase = a phase-walker (immaterial, or committed to it): material walls are air to it, so
    /// its answer is the nearest candidate by straight line with the line itself as steering, and
    /// walls are never returned as chew targets (preferWalls still hunts them); only an immaterial
    /// wall (Force Field) refuses the line, and a fully refused phaser routes like matter.
    /// </summary>
    public static bool Decide(Vector2 pos, float wander, float crowdAversion, float wallExploitIQ,
        bool preferCharacter, bool preferBuildings, bool preferWalls, out Transform target, out Vector2 dir,
        bool phase = false)
        => Choose(pos, wander, crowdAversion, wallExploitIQ, preferCharacter, preferBuildings, preferWalls,
            out target, out dir, out _, out _, phase);

    /// <summary>
    /// <see cref="Decide"/> plus WHICH field won and the evaluation position it was read at —
    /// the debug route gizmo walks that field from that point. The evaluation position differs
    /// from <paramref name="pos"/> only for off-grid queriers (map-edge spawns), which are clamped
    /// to their nearest in-rect cell so newborns still pick their true best target by preference;
    /// their dir then marches them at that entry point until the grid governs them.
    /// </summary>
    public static bool Choose(Vector2 pos, float wander, float crowdAversion, float wallExploitIQ,
        bool preferCharacter, bool preferBuildings, bool preferWalls,
        out Transform target, out Vector2 dir, out MineFlowField field, out Vector2 evalPos,
        bool phase = false)
    {
        target = null;
        dir = Vector2.zero;
        field = null;
        evalPos = pos;
        if (!Ready) return false;
        var m = Ensure();
        bool offGrid = BasePathGrid.ClampToGrid(pos, out evalPos);
        float bestEff = float.MaxValue;
        bool any = false;
        if (phase)
        {
            // PHASE-WALKERS: material walls and solid footprints are air to this body, so no field
            // is read — the nearest preferred candidate wins by straight line (from the TRUE
            // position, no grid clamp: rulers work off-grid too) and the steering IS that line.
            // Walls never come back as chew targets. The one thing that still refuses a line is an
            // IMMATERIAL wall (the Force Field's span — immaterial-vs-immaterial stays solid); with
            // every candidate's line refused the phaser falls through to the material logic below
            // and deals with the field wall like everyone else (detour or chew). preferWalls is
            // honoured unchanged — a wall-hunting phaser keeps hunting walls.
            if (preferCharacter) any |= m.EvalPhaseFam(Fam.Char, pos, ref bestEff, ref target, ref dir);
            if (preferBuildings) any |= m.EvalPhaseFam(Fam.Bld, pos, ref bestEff, ref target, ref dir);
            if (preferWalls) any |= m.EvalWalls(evalPos, wallExploitIQ, ref bestEff, ref target, ref dir, ref field);
            if (!any) any = m.EvalPhaseFam(Fam.All, pos, ref bestEff, ref target, ref dir);
            if (any && target != null) return true;
            bestEff = float.MaxValue; any = false; target = null; dir = Vector2.zero; field = null;
        }
        if (preferCharacter) any |= m.EvalClass(m.charViews, evalPos, wander, crowdAversion, ref bestEff, ref target, ref dir, ref field);
        if (preferBuildings) any |= m.EvalClass(m.buildingViews, evalPos, wander, crowdAversion, ref bestEff, ref target, ref dir, ref field);
        if (preferWalls) any |= m.EvalWalls(evalPos, wallExploitIQ, ref bestEff, ref target, ref dir, ref field);
        if (!any) any = m.EvalClass(m.allViews, evalPos, wander, crowdAversion, ref bestEff, ref target, ref dir, ref field);
        if (!any || target == null) return false;
        if (offGrid)
        {
            Vector2 d = evalPos - pos;
            if (d.sqrMagnitude > 1e-4f) dir = d.normalized;
        }
        return true;
    }

    // The two anchor views bracketing this coefficient. Exact on an anchor (second view dropped);
    // beyond the top anchor the top two still rank correctly against each other. Single-view
    // families (character) collapse to one evaluation.
    static void PickViews(TargetClass[] views, float c, out TargetClass v1, out TargetClass v2)
    {
        if (c <= 0.001f) { v1 = views[0]; v2 = null; return; }
        if (c <= 1f) { v1 = views[1]; v2 = Mathf.Approximately(c, 1f) ? null : views[0]; }
        else if (c <= 3.5f) { v1 = views[2]; v2 = Mathf.Approximately(c, 3.5f) ? null : views[1]; }
        else { v1 = views[2]; v2 = views[1]; }
        if (v2 == v1) v2 = null;
    }

    // Evaluate one class as a candidate answer; claims the ref slots only when it beats bestEff.
    // Each bracketing anchor view yields a real candidate (target + route from its own field),
    // rescored as walk + aversion × penalty; the unit adopts the better one wholesale — nothing
    // is ever interpolated into a position or target that doesn't exist.
    bool EvalClass(TargetClass[] views, Vector2 pos, float wander, float aversion,
        ref float bestEff, ref Transform target, ref Vector2 dir, ref MineFlowField field)
    {
        PickViews(views, aversion, out TargetClass v1, out TargetClass v2);
        if (!EvalScored(v1, pos, wander, aversion, out Transform t1, out float s1, out Vector2 d1, out MineFlowField f1))
            return false;   // identical seeds & reachability across views — none can succeed if this failed
        // second anchor only matters when something is actually crowded
        if (v2 != null && v1.penApplied.Count > 0 &&
            EvalScored(v2, pos, wander, aversion, out Transform t2, out float s2, out Vector2 d2, out MineFlowField f2) &&
            s2 < s1)
        { t1 = t2; s1 = s2; d1 = d2; f1 = f2; }
        if (s1 >= bestEff) return false;
        bestEff = s1; target = t1; dir = d1; field = f1;
        return true;
    }

    // One view's candidate, rescored under the unit's own coefficient: un-bake the view's baked
    // penalty (walk = eff - round(viewCoeff × basePen)) and re-apply at the unit's strength.
    bool EvalScored(TargetClass tc, Vector2 pos, float wander, float aversion,
        out Transform tgt, out float score, out Vector2 sd, out MineFlowField f)
    {
        score = float.MaxValue;
        if (!EvalVariant(tc, pos, wander, out Transform own, out tgt, out int eff, out sd, out f)) return false;
        int pen = PenOf(tc, own);
        score = eff - Mathf.RoundToInt(tc.penCoeff * pen) + aversion * pen;
        return true;
    }

    // One view of one class: the wander decision picks chew-vs-detour, and the answer comes back
    // as (seed owner, attack target, effective distance, steering, winning field). Target differs
    // from owner only in the chew case, where the gate wall is what actually gets attacked.
    bool EvalVariant(TargetClass tc, Vector2 pos, float wander,
        out Transform owner, out Transform target, out int eff, out Vector2 dir, out MineFlowField field)
    {
        owner = null; target = null; eff = -1; dir = Vector2.zero; field = null;
        Touch(tc);
        int dW = tc.W.DistanceCells(pos);
        int dD = tc.D.DistanceCells(pos);
        // the wander decision: dW <= dD always (W is a relaxation of D); dD == -1 = no wall-free
        // route exists (sealed in, or standing ON a wall cell mid-chew — free hysteresis) -> chew
        if (dD >= 0 && dW >= 0 && dD <= wander * dW)
        {
            if (!tc.D.TryGetNearestSeed(pos, out owner, out _) || owner == null) return false;
            target = owner;
            tc.D.TryGetStepDir(pos, out dir);
            eff = dD; field = tc.D;
            return true;
        }
        // chew (or no wall intervenes): the first wall on the route is the target — ranged enemies
        // volley the wall they're breaking instead of chasing a ghost behind it
        if (dW < 0) return false;
        tc.W.TryGetNearestSeed(pos, out owner, out _);
        // RULER CANDIDATE — what makes low wander CONTINUOUS instead of a cliff at 0: the straight
        // line to the owner is priced as EUCLIDEAN length plus the grid's surcharges, with the
        // unit's wander discounting the chew part: rulerWalk + wander × rulerChew vs dW. The
        // euclidean base is deliberate: the field's 4-connected metric prices a diagonal and the
        // staircase AROUND a wall end identically, so a walk-vs-walk comparison would call the
        // detour free and diagonal attackers would never chew (they'd orbit the wall tip). Pricing
        // the line at what it truly measures gives straightness an inherent edge that chew cost
        // (× wander) must beat — at 0 the line always wins: dead straight at the target, attacking
        // the first wall geometrically in the way. At wander ≥ 1 the ruler isn't considered at
        // all (the field is the sanctioned optimum), keeping default units untouched; near the
        // seam the two only disagree when a wall's remaining chew is smaller than the staircase
        // slack, where both routes chew the same dying wall anyway.
        if (wander < 1f && owner != null &&
            RulerRoute(pos, owner.position, out float rWalk, out float rChew, out LifeScript sWall) &&
            rWalk + Mathf.Max(wander, 0f) * rChew < dW)
        {
            target = sWall != null ? sWall.transform : owner;
            Vector2 aim = owner.position;
            if (sWall != null) BaseBlockMap.TryGetNearestWallPoint(sWall, pos, out aim);
            Vector2 dv = aim - pos;
            if (dv.sqrMagnitude > 1e-4f) dir = dv.normalized;
            eff = dW; field = tc.W;   // class scoring stays on the field snapshot — the ruler only replaces steering + gate
            return true;
        }
        target = tc.W.TryGetGate(pos, out LifeScript wall) ? wall.transform : owner;
        if (target == null) return false;
        tc.W.TryGetStepDir(pos, out dir);
        eff = dW; field = tc.W;
        return true;
    }

    // The straight segment a→b priced for the ruler-vs-field comparison: walk = EUCLIDEAN length
    // in cell units (a straight line's true cost — the 4-metric would price diagonals ~1.4× dearer
    // and make around-the-end staircases read as free) plus the HARD ground surcharges of crossed
    // cells (aperture/off-map premiums); chew = the DISCOMFORT the line asks the unit to swallow —
    // wall chew surcharges AND the padding rings (crossing a wall means crossing its padded apron
    // on both sides; pricing that into walk made the straight line read ~tens of cells longer than
    // it is, so wander-0 units stopped attacking the wall in front of their target and detoured —
    // padding is a comfort cost and must be wander-discounted like the chew it wraps). The first
    // wall comes back as the gate. Ends on b's cell — or, target's-own-mass style (the LineOfSight
    // solid-tail rule), by staying solid from first solid contact to b. Solid-then-passable means
    // an unchewable building stands in the way: no ruler route (false).
    static bool RulerRoute(Vector2 a, Vector2 b, out float walk, out float chew, out LifeScript firstWall)
    {
        chew = 0f; firstWall = null;
        var g = BasePathGrid.chewable;
        float cs = g.CellSize;
        walk = (b - a).magnitude / cs;
        Vector3Int c = g.WorldToCell(a), cEnd = g.WorldToCell(b);
        int c0 = g.EnterCost(c);   // the querier's own cell surcharge, as the field pays it
        if (c0 != PathGrid.BLOCKED)
        {
            if (g.WallIdAt(c) >= 0) chew += c0 - 1;
            else { int pe = BasePathGrid.PadExcess(c); chew += pe; walk += c0 - 1 - pe; }
        }
        if (c == cEnd) return true;
        Vector2 d = b - a;
        int stepX = d.x > 0f ? 1 : -1, stepY = d.y > 0f ? 1 : -1;
        Vector2 cellMin = new Vector2(c.x * cs, c.y * cs);
        float tMaxX = d.x != 0f ? (((d.x > 0f ? cellMin.x + cs : cellMin.x) - a.x) / d.x) : float.PositiveInfinity;
        float tMaxY = d.y != 0f ? (((d.y > 0f ? cellMin.y + cs : cellMin.y) - a.y) / d.y) : float.PositiveInfinity;
        float tDeltaX = d.x != 0f ? cs / Mathf.Abs(d.x) : float.PositiveInfinity;
        float tDeltaY = d.y != 0f ? cs / Mathf.Abs(d.y) : float.PositiveInfinity;
        bool solidRun = false;
        int guard = 4096;
        while (guard-- > 0)
        {
            if (tMaxX < tMaxY) { tMaxX += tDeltaX; c.x += stepX; }
            else               { tMaxY += tDeltaY; c.y += stepY; }
            if (c == cEnd) return true;   // arrived — the end cell is the seed, never paid for
            int cost = g.EnterCost(c);
            if (cost == PathGrid.BLOCKED) { solidRun = true; continue; }   // possibly the target's own hull
            if (solidRun) return false;   // solid then passable — something unchewable blocks the line
            int wallId = g.WallIdAt(c);
            if (wallId >= 0)
            {
                chew += cost - 1;
                if (firstWall == null) BaseBlockMap.TryGetWallBySlot(wallId, out firstWall, out _);
            }
            else
            {
                int pe = BasePathGrid.PadExcess(c);   // padding part → the wander-discounted bucket
                chew += pe;
                walk += cost - 1 - pe;
            }
        }
        return false;
    }

    static int PenOf(TargetClass tc, Transform owner)
        => owner != null && tc.penApplied.TryGetValue(owner, out int p) ? p : 0;

    // The walls class: nearest wall span cell is the destination itself (siege units). iq rescales
    // how strongly a wall's remaining HP attracts, via the same anchor-view bracketing.
    bool EvalWalls(Vector2 pos, float iq, ref float bestEff, ref Transform target, ref Vector2 dir, ref MineFlowField field)
    {
        WallView w1, w2;
        if (iq <= 0.001f) { w1 = wallViews[0]; w2 = null; }
        else if (iq <= 1f) { w1 = wallViews[1]; w2 = Mathf.Approximately(iq, 1f) ? null : wallViews[0]; }
        else if (iq <= 3.5f) { w1 = wallViews[2]; w2 = Mathf.Approximately(iq, 3.5f) ? null : wallViews[1]; }
        else { w1 = wallViews[2]; w2 = wallViews[1]; }
        float sBest = float.MaxValue;
        Transform o = null;
        MineFlowField f = null;
        ScoreWallView(w1, pos, iq, ref sBest, ref o, ref f);
        if (w2 != null) ScoreWallView(w2, pos, iq, ref sBest, ref o, ref f);
        if (o == null || sBest >= bestEff) return false;
        f.TryGetStepDir(pos, out Vector2 sd);
        bestEff = sBest; target = o; dir = sd; field = f;
        return true;
    }

    void ScoreWallView(WallView v, Vector2 pos, float iq, ref float sBest, ref Transform o, ref MineFlowField f)
    {
        v.queryT = Time.time;
        if (Time.time - v.builtT > WallsInterval) RebuildWallView(v);
        int d = v.F.DistanceCells(pos);
        if (d < 0 || !v.F.TryGetNearestSeed(pos, out Transform ow, out _) || ow == null) return;
        int h = wallPen.TryGetValue(ow, out int hh) ? hh : 0;
        float s = d - Mathf.RoundToInt(v.hCoeff * h) + iq * h;
        if (s < sBest) { sBest = s; o = ow; f = v.F; }
    }

    // ------------------------------------------------------------------ phase-walkers

    /// <summary>How many nearest candidates get a line test before the phased answer gives up —
    /// past that, being walled off from everything near means the material fallback should run.</summary>
    const int PhaseLineChecks = 6;

    // Only the PhaseLineChecks nearest candidates are ever line-tested, so keep just those: a
    // fixed ascending buffer filled during the scan replaces building + sorting the full list.
    readonly (float d2, Transform t)[] phaseBest = new (float, Transform)[PhaseLineChecks];
    int phaseBestCount;
    int footprintHealFrame = -1;   // several phasers (x up to 3 fams) per frame share one heal pass

    // One family's phased answer: the nearest candidate by STRAIGHT LINE whose segment crosses no
    // immaterial wall. No field, no wander, no crowding — a phaser's metric is the ruler, and its
    // walls-are-air answer must not inherit chew-priced field distances. Candidates mirror
    // RebuildClass's seed enumeration (walls have no path footprint, so they can never appear).
    // Claims the ref slots only when it beats bestEff; score = euclidean cells, the same unit the
    // field distances are in.
    bool EvalPhaseFam(Fam fam, Vector2 pos, ref float bestEff, ref Transform target, ref Vector2 dir)
    {
        phaseBestCount = 0;
        if (fam != Fam.Bld && CharacterScript.CS != null && PathZone.AtBase(CharacterScript.CS.transform.position))
            AddPhaseCandidate(pos, CharacterScript.CS.transform);
        if (fam == Fam.All)
        {
            var allies = GS.FindParent(GS.Parent.allies);
            if (allies != null)
                for (int k = 0; k < allies.childCount; k++)
                {
                    var ch = allies.GetChild(k);
                    if (ch.gameObject.activeInHierarchy && !UntargetableAllies.Contains(ch))
                        AddPhaseCandidate(pos, ch);
                }
        }
        if (fam != Fam.Char)
        {
            bool heal = footprintHealFrame != Time.frameCount;
            for (int b = 0; b < Building.buildings.Count; b++)
            {
                var bld = Building.buildings[b];
                if (bld == null || !bld.gameObject.activeInHierarchy) continue;
                if (heal) bld.EnsurePathFootprintCurrent();
                if (!bld.TryGetPathFootprint(out _, out _)) continue;
                AddPhaseCandidate(pos, bld.transform);
            }
            if (heal) footprintHealFrame = Time.frameCount;
        }
        if (phaseBestCount == 0) return false;
        for (int k = 0; k < phaseBestCount; k++)
        {
            Vector2 to = phaseBest[k].t.position;
            if (BaseBlockMap.SegmentCrossesImmaterialWall(pos, to)) continue;   // force-field span in the way
            float score = Mathf.Sqrt(phaseBest[k].d2) / BaseBlockMap.CellSize;
            if (score >= bestEff) return false;   // ascending — no later candidate can beat this either
            bestEff = score;
            target = phaseBest[k].t;
            Vector2 d = to - pos;
            dir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.zero;
            return true;
        }
        return false;
    }

    void AddPhaseCandidate(Vector2 pos, Transform t)
    {
        float d2 = ((Vector2)t.position - pos).sqrMagnitude;
        var best = phaseBest;
        if (phaseBestCount == best.Length && d2 >= best[phaseBestCount - 1].d2) return;
        int i = phaseBestCount < best.Length ? phaseBestCount : best.Length - 1;
        while (i > 0 && best[i - 1].d2 > d2)
        {
            best[i] = best[i - 1];
            i--;
        }
        best[i] = (d2, t);
        if (phaseBestCount < best.Length) phaseBestCount++;
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
        var all = m.allViews[1];   // baseline view
        m.Touch(all);
        bool offGrid = BasePathGrid.ClampToGrid(pos, out Vector2 evalPos);
        int dW = all.W.DistanceCells(evalPos);
        int dD = all.D.DistanceCells(evalPos);
        bool detour = dD >= 0 && dW >= 0 && dD <= wander * dW;
        if (!(detour ? all.D : all.W).TryGetStepDir(evalPos, out dir)) return false;
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
        for (int k = 0; k < 3; k++)
        {
            MaybeRebuild(allViews[k], now);
            MaybeRebuild(buildingViews[k], now);
        }
        MaybeRebuild(charViews[0], now);
        for (int k = 0; k < wallViews.Length; k++)
        {
            var v = wallViews[k];
            if (now - v.queryT < idleTimeout && now - v.builtT > WallsInterval) RebuildWallView(v);
        }
        if (now - queryEnemyT < idleTimeout && now - builtEnemyT > EnemiesInterval) RebuildEnemies();
    }

    void MaybeRebuild(TargetClass tc, float now)
    {
        if (now - tc.queryT < idleTimeout && now - tc.builtT > tc.interval) RebuildClass(tc);
    }

    void RebuildClass(TargetClass tc)
    {
        bool incChar = tc.fam != Fam.Bld;
        bool incUnits = tc.fam == Fam.All;
        bool incBuildings = tc.fam != Fam.Char;

        // crowding census against the view's OWN old W field, before reseeding: how many enemies
        // are currently routed to each owner? Every view censuses (its base penalties feed the
        // decide-time rescoring) but bakes round(penCoeff × base) into the seeds — 0 for blind.
        // (Pointless for the single-seed character class — every route shifts equally.)
        crowd.Clear();
        tc.penApplied.Clear();
        if (crowding && tc.fam != Fam.Char && tc.W.IsBuilt)
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
                SeedCost(tc, CharacterScript.CS.transform), CharacterScript.CS.transform));

        if (incUnits)
        {
            var allies = GS.FindParent(GS.Parent.allies);
            if (allies != null)
                for (int k = 0; k < allies.childCount; k++)
                {
                    var ch = allies.GetChild(k);
                    if (ch.gameObject.activeInHierarchy && !UntargetableAllies.Contains(ch))
                        seedScratch.Add(new MineFlowField.Seed(ch.position, SeedCost(tc, ch), ch));
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
                int cost = SeedCost(tc, bld.transform, Mathf.Max(1, (cells.x + cells.y) / 2));
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

    // capacity ~ side length (half-perimeter of the footprint): a bigger hull hosts proportionally
    // more attackers before its seeds start reading further away. Player/ally units = 1.
    int CrowdCost(Transform t, int capacity = 1)
        => crowding && t != null && crowd.TryGetValue(t, out int n)
            ? Mathf.Min(crowdMaxPenalty, n / Mathf.Max(1, crowdDivisor * capacity)) : 0;

    // seed cost for this owner in this rebuild: the BASE penalty is recorded per owner (for
    // decide-time rescoring) and the view's anchor coefficient is what actually gets baked
    int SeedCost(TargetClass tc, Transform t, int capacity = 1)
    {
        int c = CrowdCost(t, capacity);
        if (c <= 0) return 0;
        tc.penApplied[t] = c;
        return Mathf.RoundToInt(tc.penCoeff * c);
    }

    // Wall-hunting view: every live wall's span cells seed at round(hCoeff × handicap), where the
    // handicap prices remaining HP exactly like the chew router (hp × costPerHp × registered mult)
    // — so damaged walls attract wall-hunters from proportionally further away. Base handicaps are
    // recorded per wall for decide-time rescoring.
    void RebuildWallView(WallView v)
    {
        v.builtT = Time.time;
        seedScratch.Clear();
        wallPen.Clear();
        float cs = BaseBlockMap.CellSize;
        var owners = BaseBlockMap.WallOwners;
        for (int w = 0; w < owners.Count; w++)
        {
            var ls = owners[w];
            if (ls == null || ls.hasDied) continue;
            if (!BaseBlockMap.TryGetWallCells(ls, out List<Vector2Int> cells, out Transform tf)) continue;
            BaseBlockMap.TryGetWallBySlot(w, out _, out float mult);
            int h = Mathf.Clamp(Mathf.CeilToInt(ls.hp * BasePathGrid.costPerHp * mult), 0, 63);
            // cavity-edge walls read as weaker: HP-weighted wall-hunters join the widening effort
            for (int k = 0; k < cells.Count; k++)
                if (BasePathGrid.IsCavityEdge(cells[k]))
                {
                    h = Mathf.CeilToInt(h * BasePathGrid.breachEdgeChewMult);
                    break;
                }
            wallPen[tf] = h;
            int baked = Mathf.RoundToInt(v.hCoeff * h);
            for (int k = 0; k < cells.Count; k++)
                seedScratch.Add(new MineFlowField.Seed(
                    new Vector2((cells[k].x + 0.5f) * cs, (cells[k].y + 0.5f) * cs), baked, tf));
        }
        v.F.Rebuild(BasePathGrid.blocked, seedScratch);
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
