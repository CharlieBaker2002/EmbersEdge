using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Replaces DM. Generates the tile-mining dungeon for an era: picks a random set of pockets from the
/// authored library, places them small-weight-near-centre / large-weight-near-edge without overlap,
/// normalises their points to the era's target difficulty, then carves them into MineField. Pockets
/// stay dormant until the player tunnels into them (MineField.OnCavityBreached -> Pocket.OnDiscover).
/// </summary>
public class MineDungeonManager : MonoBehaviour
{
    public static MineDungeonManager i;

    [SerializeField] MineAuthoringSO authoring;
    public MineAuthoringSO Authoring => authoring;   // read access for Pocket (status-tile prefabs etc.)
    [Tooltip("Where the player lands on diving in (created/positioned at the entry cavity).")]
    public Transform entryPoint;
    [HideInInspector] public Pocket activePocket;

    [Header("Layout")]
    [Tooltip("Dungeon carve region per era, in WORLD units (index = era). Moved here from the " +
             "authoring asset — MineAuthoring is Mine-Forge content only.")]
    public Vector2[] dungeonAreas = { new Vector2(60f, 60f), new Vector2(80f, 80f), new Vector2(100f, 100f) };
    public int entryCavityRadius = 8;   // cells
    [Tooltip("Minimum solid cells kept between pockets (>=1 so pockets never merge into one).")]
    public int pocketGap = 1;           // min cells between pockets
    public int areaMargin = 3;          // cells kept clear at the area edge

    [Header("Budget")]
    [Tooltip("Planning may overspend the era's target by up to this factor; the excess is trimmed back " +
             "(one enemy per room, closest-to-entry → furthest, repeating) after placement.")]
    public float overBudgetFactor = 1.15f;

    [Header("Debug / standalone test")]
    public bool generateOnStart = false;
    [Tooltip("Cheat: reveals every undiscovered pocket's walls/fog as soon as this is checked — same " +
             "geometry a real breach produces — but does NOT spawn their enemies. Enemies still only " +
             "spawn once the player physically walks inside.")]
    public bool showPockets = false;

    readonly List<Pocket> pockets = new List<Pocket>();
    readonly List<MineSpawner> spawners = new List<MineSpawner>();
    Vector3Int entryCell;
    bool showPocketsApplied;

    void Awake()
    {
        i = this;

        // The MINE FIELD lives ON this GameObject now (one combined dungeon object). Adopt a local
        // MineField — adding one if missing (all its art self-loads from Resources) — and strip any
        // stray MineField left on another scene object from the old two-object setup. The strip must
        // be IMMEDIATE: a deferred Destroy leaves the stray alive for the rest of the frame, so its
        // own (later) Awake steals MineField.i, the dungeon builds on it, and at end of frame the
        // component dies while its tilemaps survive — walls that render but never collide.
        foreach (var stray in FindObjectsByType<MineField>(FindObjectsInactive.Include))
            if (stray.gameObject != gameObject)
            {
                Debug.LogWarning($"MineDungeonManager: removing stray MineField on '{stray.gameObject.name}' — " +
                                 "the mine field is a component of the MineDungeonManager object now.");
                DestroyImmediate(stray);
            }
        var mine = GetComponent<MineField>();
        if (mine == null) mine = gameObject.AddComponent<MineField>();
        MineField.i = mine;

        if (entryPoint == null)
        {
            // Always have a valid landing transform, even before the first generation.
            entryPoint = new GameObject("DungeonEntry").transform;
            entryPoint.SetParent(transform, false);
        }
    }

    void Start()
    {
        if (authoring == null) authoring = Resources.Load<MineAuthoringSO>("MineAuthoring");
        GS.OnNewEra += GenerateDungeon;             // regenerate each era (cf. DM.Start)
        if (generateOnStart) GenerateDungeon(GS.era);
    }

    void OnDestroy()
    {
        GS.OnNewEra -= GenerateDungeon;
        if (MineField.i != null) MineField.i.OnCavityBreached -= OnCavityBreached;
    }

    // showPockets cheat: reveal every undiscovered pocket the moment the box is ticked (Inspector edit
    // during Play, or set from code). Re-arms itself if toggled off then on again.
    void Update()
    {
        if (showPockets && !showPocketsApplied)
        {
            showPocketsApplied = true;
            RevealAllPockets();
        }
        else if (!showPockets)
        {
            showPocketsApplied = false;
        }
    }

    // Reveal every undiscovered pocket in ONE batched MineField pass (a handful of SetTiles calls
    // total) — triggering showPockets is effectively free, even with every era-3 pocket at once.
    void RevealAllPockets()
    {
        if (MineField.i == null) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        List<int> pids = null;
        foreach (var p in pockets)
            if (p != null && p.TryMarkRevealed(out int pid)) (pids ??= new List<int>()).Add(pid);
        if (pids != null) MineField.i.RevealPocketsGeometry(pids);
        Debug.Log($"[RevealTiming] RevealAllPockets trigger: {pids?.Count ?? 0} pockets, {sw.ElapsedMilliseconds}ms " +
                  "(if the game still hitches ~2s but this reads ~0ms, the cost is OUTSIDE the reveal — " +
                  "watch which [RevealTiming] line is missing/slow and any log right after this one)");
    }

    // =====================================================================================
    //  Generation
    // =====================================================================================

    public void GenerateDungeon(int era)
    {
        // Self-heal: the field lives on this object, so a lost singleton just means re-adopting it.
        if (MineField.i == null)
        {
            var mine = GetComponent<MineField>();
            if (mine == null) mine = gameObject.AddComponent<MineField>();
            MineField.i = mine;
        }
        ClearPockets();

        EraMine em = (authoring != null) ? authoring.GetEra(era) : FallbackEra();
        float cs = MineField.i.CellSize;
        Vector2 area = (dungeonAreas != null && dungeonAreas.Length > 0)
            ? dungeonAreas[Mathf.Clamp(era, 0, dungeonAreas.Length - 1)]
            : new Vector2(60f, 60f);
        int W = Mathf.Max(32, Mathf.RoundToInt(area.x / cs));
        int H = Mathf.Max(32, Mathf.RoundToInt(area.y / cs));
        entryCell = new Vector3Int(0, 0, 0);

        List<PocketInstance> placed = PlacePockets(em, era, W, H);

        var layout = new MineDungeonLayout
        {
            era = era,
            areaCells = new BoundsInt(-W / 2, -H / 2, 0, W, H, 1),
            entryCell = entryCell,
            entryCavityRadius = entryCavityRadius,
            pockets = placed,
        };

        // Subscribe before Build: the entry-cavity breach fires during Build and is harmlessly ignored
        // (it matches no pocket); real pocket breaches arrive later as the player digs in.
        MineField.i.OnCavityBreached -= OnCavityBreached;
        MineField.i.OnCavityBreached += OnCavityBreached;

        MineField.i.Build(layout);

        if (entryPoint == null)
        {
            entryPoint = new GameObject("DungeonEntry").transform;
            entryPoint.SetParent(transform, true);
        }
        entryPoint.position = MineField.i.CellCenterWorld(entryCell);

        foreach (var inst in placed)
        {
            var go = new GameObject("Pocket_" + inst.template.name);
            go.transform.SetParent(transform, false);
            go.transform.position = MineField.i.CellCenterWorld(inst.CentreCell);
            var p = go.AddComponent<Pocket>();
            p.Init(inst);
            pockets.Add(p);
        }
        if (showPockets) RevealAllPockets();   // cheat already on when a new dungeon generates — one batch

        PlaceSpawners(em, era, placed, W, H);
    }

    // Fill the rock with spawners against the era's SPAWNER budget (MineField.eraNSpawnerPoints,
    // interpolated by difficulty): sample randomly from the era's authored spawners — repeats allowed —
    // spending each pick's points (avg per armed day, forge-overridable) until the budget is used up.
    // If nothing in the pool carries points, fall back to placing each authored spawner once.
    void PlaceSpawners(EraMine em, int era, List<PocketInstance> placed, int W, int H)
    {
        if (em.spawners == null) return;
        var pool = new List<MineSpawnerTemplate>();
        float maxPts = 0f;
        foreach (var st in em.spawners)
        {
            if (st == null || !st.enabled || st.days == null || st.days.Count == 0) continue;
            pool.Add(st);
            maxPts = Mathf.Max(maxPts, MineAuthoringSO.SpawnerPoints(st));
        }
        if (pool.Count == 0) return;

        if (maxPts <= 0f)
        {
            foreach (var st in pool) TryPlaceSpawner(st, placed, W, H);
            return;
        }

        float budget = MineField.i.SpawnerPointsForEra(era, SetM.difficulty);
        float spent = 0f;
        int safety = 128;   // bounds zero-point picks and full-map placement misses
        while (spent < budget && safety-- > 0)
        {
            var st = pool[Random.Range(0, pool.Count)];
            float pts = MineAuthoringSO.SpawnerPoints(st);
            if (pts <= 0f) continue;
            if (TryPlaceSpawner(st, placed, W, H)) spent += pts;
        }
    }

    // Seed one spawner into the rock, REPLACING regular walls: its whole footprint (from the PNG's world
    // size) must be plain wall — never ore, never pocket space — and clear of the entry cavity and every
    // pocket rect. Spread reuses the ore-fairness idea: weigh several valid spots per placement and keep
    // the one FURTHEST from the spawners already placed (oreFairness 0 = pure dice, 1 = strongly even).
    bool TryPlaceSpawner(MineSpawnerTemplate st, List<PocketInstance> placed, int W, int H)
    {
        float cs = MineField.i.CellSize;
        Vector2 ws = st.WorldSize;
        int cw = Mathf.Max(1, Mathf.CeilToInt(ws.x / cs));
        int ch = Mathf.Max(1, Mathf.CeilToInt(ws.y / cs));

        int candN = 1 + Mathf.RoundToInt(Mathf.Clamp01(MineField.i.oreFairness) * 15f);
        BoundsInt best = default;
        float bestScore = -1f;
        for (int cand = 0; cand < candN; cand++)
        {
            // rejection-sample one valid candidate footprint
            BoundsInt rect = default;
            bool ok = false;
            for (int a = 0; a < 128 && !ok; a++)
            {
                int x = Random.Range(-W / 2 + areaMargin, W / 2 - areaMargin);
                int y = Random.Range(-H / 2 + areaMargin, H / 2 - areaMargin);
                rect = new BoundsInt(x - cw / 2, y - ch / 2, 0, cw, ch, 1);
                ok = FootprintFits(rect, placed);
            }
            if (!ok) continue;
            // score = distance to the nearest spawner already placed (bigger = fairer)
            float score = float.MaxValue;
            Vector2 c = RectCentreWorld(rect);
            foreach (var s in spawners)
                if (s != null) score = Mathf.Min(score, ((Vector2)s.transform.position - c).sqrMagnitude);
            if (spawners.Count == 0) score = Random.value;   // first spawner: nothing to be far from
            if (score > bestScore) { bestScore = score; best = rect; }
        }
        if (bestScore < 0f) return false;

        MineField.i.ClearWallArt(best);   // the spawner PNG *is* those walls now
        var go = new GameObject("Spawner_" + st.name);
        go.transform.SetParent(transform, false);
        go.transform.position = RectCentreWorld(best);
        var ms = go.AddComponent<MineSpawner>();
        ms.Init(st);
        MineField.i.RegisterSpawnerFootprint(best, ms);
        spawners.Add(ms);
        return true;
    }

    // Every cell of the footprint: off the entry cavity, plain replaceable wall (solid, no ore, no
    // pocket cavity), and outside every pocket rect (+2 pad).
    bool FootprintFits(BoundsInt rect, List<PocketInstance> placed)
    {
        int entryClear = entryCavityRadius + 4;
        for (int y = rect.yMin; y < rect.yMax; y++)
            for (int x = rect.xMin; x < rect.xMax; x++)
            {
                if (Mathf.Abs(x) < entryClear && Mathf.Abs(y) < entryClear) return false;
                if (!MineField.i.IsPlainWall(new Vector3Int(x, y, 0))) return false;
                foreach (var p in placed)
                    if (Inflate(p.cellRect, 2).Contains(new Vector2Int(x, y))) return false;
            }
        return true;
    }

    Vector2 RectCentreWorld(BoundsInt r)
    {
        Vector2 a = MineField.i.CellCenterWorld(new Vector3Int(r.xMin, r.yMin, 0));
        Vector2 b = MineField.i.CellCenterWorld(new Vector3Int(r.xMax - 1, r.yMax - 1, 0));
        return (a + b) * 0.5f;
    }

    /// <summary>
    /// The nearest UNDISCOVERED core-granting pocket (boss included) — the "ember core spot" the faint
    /// spawn-flash hint lines point toward. Null once every core is found.
    /// </summary>
    public Vector2? NearestCoreSpot(Vector2 from)
    {
        float best = float.MaxValue;
        Vector2 bestP = default;
        bool found = false;
        foreach (var p in pockets)
        {
            if (p == null || p.Discovered) continue;
            if (p.Instance == null || p.Instance.template == null || !p.Instance.template.hasCore) continue;
            float d = ((Vector2)p.transform.position - from).sqrMagnitude;
            if (d < best) { best = d; bestP = p.transform.position; found = true; }
        }
        return found ? bestP : (Vector2?)null;
    }

    void OnCavityBreached(Vector3Int cell)
    {
        var c2 = new Vector2Int(cell.x, cell.y);
        foreach (var p in pockets)
        {
            if (!p.Discovered && p.Instance.cellRect.Contains(c2))
            {
                p.OnDiscover();
                return;
            }
        }
    }

    public void ResetActivePocket()
    {
        if (activePocket != null) activePocket.ResetPocket();
    }

    // =====================================================================================
    //  Dimension freeze — teleporting freezes time in the dimension you leave and resumes the
    //  one you arrive in. Leaving the dungeon DISABLES every enemy in it (nothing is deleted);
    //  the next dive re-enables them exactly where they stood.
    // =====================================================================================

    readonly List<GameObject> frozen = new List<GameObject>();

    /// <summary>Teleported INTO the dungeon: thaw the frozen enemies, then let every spawner that
    /// already has the dig in range arm its day plan (fires 0–30s from now).</summary>
    public void OnEnterDungeon()
    {
        foreach (var g in frozen) if (g != null) g.SetActive(true);
        frozen.Clear();
        foreach (var s in spawners) if (s != null) s.OnEnterDungeon();
    }

    /// <summary>Teleported back to base: armed spawner plans land their remainder instantly, then the
    /// whole dungeon dimension freezes — every live enemy is disabled where it stands.</summary>
    public void OnLeaveDungeon()
    {
        foreach (var s in spawners) if (s != null) s.OnLeaveDungeon();
        var parent = GS.FindParent(GS.Parent.enemies);
        if (parent == null) return;
        foreach (Transform t in parent)
            if (t.gameObject.activeSelf) { t.gameObject.SetActive(false); frozen.Add(t.gameObject); }
    }

    // =====================================================================================
    //  Pocket placement (small weight -> centre, large -> edge; non-overlapping; cf. DM AABB retry)
    // =====================================================================================

    List<PocketInstance> PlacePockets(EraMine em, int era, int W, int H)
    {
        var result = new List<PocketInstance>();
        if (em.pockets.Count == 0) return result;

        // Point budget = the era's Vector2 range on MineField (era1/2/3Points), interpolated by difficulty (0→3).
        float budget = MineField.i.PointsForEra(era, SetM.difficulty);
        float pointsUsed = 0f;

        PocketTemplate boss = em.Boss();
        var nonBoss = new List<PocketTemplate>();
        foreach (var p in em.pockets) if (!p.isBoss && p.enabled) nonBoss.Add(p);

        // There is NO pocket count — keep sampling non-boss pockets until their authored points fill the
        // budget (the boss is placed separately, last, furthest from the entry). The boss's own points count
        // toward the budget, so the non-boss fill stops a boss's-worth short. TrimToBudget reconciles the rest.
        float maxPocketPts = 0f;
        foreach (var p in nonBoss) maxPocketPts = Mathf.Max(maxPocketPts, MineAuthoringSO.PocketAutoPoints(p));

        var chosen = new List<PocketInstance>();
        float chosenPoints = boss != null ? MineAuthoringSO.PocketAutoPoints(boss) : 0f;
        // If no pocket carries points (degenerate pool), the budget can't be reached — skip the fill and place
        // just the boss. Otherwise sample until full, with a count backstop so placement never runs away.
        int safety = 512;
        while (maxPocketPts > 0f && nonBoss.Count > 0 && chosenPoints < budget && safety-- > 0)
        {
            var inst = MakeInst(nonBoss[Random.Range(0, nonBoss.Count)]);
            chosen.Add(inst);
            chosenPoints += MineAuthoringSO.PocketAutoPoints(inst.template);
        }

        chosen.Sort((a, b) => a.weight.CompareTo(b.weight));

        int entryClear = entryCavityRadius + 4;
        int maxR = Mathf.Min(W, H) / 2 - areaMargin;
        int minR = entryClear + 4;
        maxR = Mathf.Max(minR, maxR);

        for (int idx = 0; idx < chosen.Count; idx++)
        {
            var inst = chosen[idx];
            ApplyCombisAndBudget(em, era, inst, budget, ref pointsUsed);   // merge combis BEFORE placement
            int pw = Mathf.Max(2, inst.template.width);
            int ph = Mathf.Max(2, inst.template.height);
            float rank = chosen.Count <= 1 ? 0f : (float)idx / (chosen.Count - 1);
            int targetR = Mathf.RoundToInt(Mathf.Lerp(minR, maxR, rank));

            if (TryPlaceRing(inst, pw, ph, targetR, W, H, entryClear, result) ||
                ScanPlace(inst, pw, ph, W, H, entryClear, result))
            {
                result.Add(inst);
            }
            else
            {
                Debug.LogWarning($"MineDungeonManager: could not place pocket '{inst.template.name}' ({pw}x{ph}); skipped.");
            }
        }

        // The boss ALWAYS goes furthest from the entry: placed last, at the valid spot whose centre is
        // the most distant from the entry cavity (accounts for the pockets already placed).
        if (boss != null)
        {
            var bossInst = MakeInst(boss);
            ApplyCombisAndBudget(em, era, bossInst, budget, ref pointsUsed);
            int pw = Mathf.Max(2, bossInst.template.width), ph = Mathf.Max(2, bossInst.template.height);
            if (FurthestPlace(bossInst, pw, ph, W, H, entryClear, result))
                result.Add(bossInst);
            else
                Debug.LogWarning($"MineDungeonManager: could not place boss pocket '{boss.name}' ({pw}x{ph}); skipped.");
        }

        TrimToBudget(result, budget);
        return result;
    }

    // Planning may overspend (overBudgetFactor); pull it back to the real target by removing ONE random
    // enemy at a time from each room in turn — closest-to-entry (origin) first, furthest/biggest last —
    // looping until the total enemy points are within budget. Each pocket's spawn budget is then set to
    // exactly what survives, so the runtime spawns all the remaining enemies.
    void TrimToBudget(List<PocketInstance> placed, float budget)
    {
        float total = 0f;
        foreach (var p in placed) total += MineAuthoringSO.PocketAutoPoints(p.template);

        if (total > budget)
        {
            var order = new List<PocketInstance>(placed);
            order.Sort((a, b) => SqrDistFromEntry(a).CompareTo(SqrDistFromEntry(b)));

            bool removedAny = true;
            while (total > budget && removedAny)
            {
                removedAny = false;
                foreach (var p in order)
                {
                    float removed = RemoveRandomEnemy(p.template);
                    if (removed <= 0f) continue;
                    total -= removed;
                    removedAny = true;
                    if (total <= budget) break;
                }
            }
        }

        // we've physically trimmed to budget, so let each pocket spawn everything that survived
        // (+1 guards against float rounding trimming the last kept enemy at spawn time)
        foreach (var p in placed) p.normalizedPoints = MineAuthoringSO.PocketAutoPoints(p.template) + 1f;
    }

    static float SqrDistFromEntry(PocketInstance p)
    {
        var c = p.CentreCell;   // entry cavity is at the origin
        return c.x * (float)c.x + c.y * (float)c.y;
    }

    // Remove one random enemy placement from a pocket; returns its point cost (0 if it had none).
    static float RemoveRandomEnemy(PocketTemplate t)
    {
        var slots = new List<(PocketWave w, int i)>();
        if (t.waves != null)
            foreach (var wv in t.waves)
                if (wv?.placed != null)
                    for (int i = 0; i < wv.placed.Count; i++)
                        if (wv.placed[i].enemy != null) slots.Add((wv, i));
        if (slots.Count == 0) return 0f;

        var pick = slots[Random.Range(0, slots.Count)];
        float price = MineAuthoringSO.EnemyPoints(pick.w.placed[pick.i].enemy);
        pick.w.placed.RemoveAt(pick.i);
        return price;
    }

    // ===== Combi-pockets =====================================================================
    //  Roll add-on combi-pockets onto a host (per combi-tile), merge them into its template so the
    //  cells/waves/extras/footprint/points all count as one bigger pocket, then allocate its spawn
    //  budget. The dungeon never spends more than the era's point budget of actual enemy points.

    public const int CombiHalf = 2;   // combi-tiles / the connection edge are 5 cells wide (2*half+1)

    void ApplyCombisAndBudget(EraMine em, int era, PocketInstance inst, float budget, ref float pointsUsed)
    {
        // Planning is allowed to OVERSPEND up to overBudgetFactor (e.g. +15%); the excess is trimmed back
        // to the real budget afterwards by TrimToBudget. So no per-pocket cap here — spawn what's authored.
        float over = budget * Mathf.Max(1f, overBudgetFactor);
        PocketTemplate host = inst.template;
        float hostBase = MineAuthoringSO.PocketAutoPoints(host);
        float headroom = Mathf.Max(0f, over - pointsUsed - hostBase);
        inst.template = BuildMerged(em, era, host, headroom);   // attaches combis (+ combi-combis) within headroom

        inst.normalizedPoints = MineAuthoringSO.PocketAutoPoints(inst.template);   // final value set by TrimToBudget
        pointsUsed += inst.normalizedPoints;
    }

    // 10% (era 1) → 50% (last era) chance per combi-tile.
    float CombiChance(int era)
        => Mathf.Lerp(0.1f, 0.5f, MineAuthoringSO.ERAS <= 1 ? 0f : (float)era / (MineAuthoringSO.ERAS - 1));

    // Build a runtime template = root pocket plus recursively-attached combi-pockets (base -> combi ->
    // combi-combi; depth capped at 2). Each combi is ROTATED so its top inlet faces its host and it grows
    // outward, connected through a 5-wide opening. Cells/waves/extras merge into one 0-origin template.
    PocketTemplate BuildMerged(EraMine em, int era, PocketTemplate root, float combiBudget)
    {
        var cavity = new HashSet<Vector2Int>();   // open cells (working coords)
        var full = new HashSet<Vector2Int>();     // every footprint cell (cavity + solid walls)
        var tileMap = new Dictionary<Vector2Int, PocketTileType>();   // painted type per working cell
        var waves = new List<PocketWave>();
        var extras = new List<PlacedObject>();
        float used = 0f;

        // Every pocket is dropped in a random one of the 4 cardinal rotations (0/90/180/270°). The root is
        // stamped at this rotation and its combis attach relative to it (AttachRecursive already rotates by k),
        // so the whole merged template comes out rotated — placement reads the rotated width/height below.
        int rootK = Random.Range(0, 4);
        AttachRecursive(root, rootK, Vector2Int.zero, 0, em, era, combiBudget, ref used, cavity, full, tileMap, waves, extras);

        // Footprint for placement = the OPEN cells only (the carved cavity), NOT the authoring rect — so a
        // small room carved into a 15×15 canvas reserves only its open extent. The outer solid border is
        // dropped (it's just dungeon ore); interior solid pillars within the open extent are kept.
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var c in cavity)
        {
            if (c.x < minX) minX = c.x; if (c.y < minY) minY = c.y;
            if (c.x > maxX) maxX = c.x; if (c.y > maxY) maxY = c.y;
        }
        if (cavity.Count == 0) { minX = 0; minY = 0; maxX = root.width - 1; maxY = root.height - 1; }
        var shift = new Vector2Int(-minX, -minY);
        var fshift = new Vector2(shift.x, shift.y);

        var merged = new PocketTemplate
        {
            name = root.name, isBoss = root.isBoss,   // hasCore is derived (isBoss || core.present), set below
            weightRange = root.weightRange,
            ember = root.EmberValue,                  // the host's ember payout survives the merge
            width = maxX - minX + 1, height = maxY - minY + 1,
        };
        // Keep EVERY painted non-Empty cell, shifted to the cavity origin: interior wall pillars + status
        // tiles (inside [0,width)×[0,height)) AND the authored surrounding walls (which shift to negative /
        // beyond-extent coords). cellRect stays the CAVITY extent (placement is cavity-based); the border
        // walls just extend around it and are stamped (lower priority than any cavity) by MineField.Build.
        foreach (var kv in tileMap)
            if (kv.Value != PocketTileType.Empty)
                merged.tiles.Add(new PocketTile(kv.Key + shift, kv.Value));
        ShiftPlaced(waves, fshift);
        ShiftList(extras, fshift);
        merged.waves = waves;
        merged.extras = extras;
        if (root.hasCore && root.core.present)
            merged.core = new CorePlacement { present = true, pos = RotPoint(root.core.pos, rootK, root.width, root.height) + fshift };
        return merged;
    }

    void AttachRecursive(PocketTemplate t, int k, Vector2Int offset, int depth, EraMine em, int era,
                         float combiBudget, ref float used,
                         HashSet<Vector2Int> cavity, HashSet<Vector2Int> full,
                         Dictionary<Vector2Int, PocketTileType> tileMap,
                         List<PocketWave> waves, List<PlacedObject> extras)
    {
        // stamp this template's footprint (rotated by k, translated by offset)
        for (int x = 0; x < t.width; x++)
            for (int y = 0; y < t.height; y++)
            {
                var local = new Vector2Int(x, y);
                var rc = RotCell(local, k, t.width, t.height) + offset;
                full.Add(rc);
                var tt = t.TileAt(local);
                if (!PocketTiles.IsWall(tt)) cavity.Add(rc);   // walls are solid; Empty + status tiles are cavity
                if (tt != PocketTileType.Empty) tileMap[rc] = tt;
            }
        var toff = new Vector2(offset.x, offset.y);
        waves.AddRange(CloneWavesRot(t.waves, k, t.width, t.height, toff));
        extras.AddRange(ClonePlacedRot(t.extras, k, t.width, t.height, toff));

        // base(0) -> combi(1) -> combi-combi(2); no children past depth 2
        if (depth >= 2 || t.combiTiles == null || t.combiTiles.Count == 0) return;
        if (em.combiPockets == null || em.combiPockets.Count == 0) return;

        float chance = CombiChance(era);
        var tCentre = new Vector2(t.width * 0.5f, t.height * 0.5f);
        foreach (var tile in t.combiTiles)
        {
            if (Random.value > chance) continue;
            float remaining = combiBudget - used;
            var options = new List<PocketTemplate>();
            foreach (var cp in em.combiPockets)
                if (cp != null && cp.enabled && MineAuthoringSO.PocketAutoPoints(cp) <= remaining) options.Add(cp);
            if (options.Count == 0) continue;
            var pick = options[Random.Range(0, options.Count)];

            Vector2Int tileW = RotCell(tile, k, t.width, t.height) + offset;
            Vector2Int dirW = RotDir(OutwardDir(tile, tCentre), k);   // this tile's outward dir in working coords

            int ck = RotForDir(dirW);                                  // rotate the child so its top inlet faces here
            int crw = pick.width, crh = pick.height; RotDims(ck, ref crw, ref crh);
            Vector2Int coff = CombiOffset(tileW, dirW, crw, crh);

            bool overlap = false;
            for (int x = 0; x < pick.width && !overlap; x++)
                for (int y = 0; y < pick.height; y++)
                    if (full.Contains(RotCell(new Vector2Int(x, y), ck, pick.width, pick.height) + coff)) { overlap = true; break; }
            if (overlap) continue;

            AddStrip(cavity, full, tileW, dirW);                       // 5-wide open connection
            used += MineAuthoringSO.PocketAutoPoints(pick);
            AttachRecursive(pick, ck, coff, depth + 1, em, era, combiBudget, ref used, cavity, full, tileMap, waves, extras);
        }
    }

    // A combi-tile / connection edge is 5 cells wide (perpendicular to its outward direction).
    static void AddStrip(HashSet<Vector2Int> cavity, HashSet<Vector2Int> full, Vector2Int centre, Vector2Int dir)
    {
        var perp = new Vector2Int(-dir.y, dir.x);
        for (int k = -CombiHalf; k <= CombiHalf; k++)
        {
            var c = new Vector2Int(centre.x + perp.x * k, centre.y + perp.y * k);
            cavity.Add(c); full.Add(c);
        }
    }

    static Vector2Int OutwardDir(Vector2Int tile, Vector2 centre)
    {
        float dx = (tile.x + 0.5f) - centre.x, dy = (tile.y + 0.5f) - centre.y;
        return Mathf.Abs(dx) >= Mathf.Abs(dy)
            ? new Vector2Int(dx >= 0 ? 1 : -1, 0)
            : new Vector2Int(0, dy >= 0 ? 1 : -1);
    }

    // Offset added to a (rotated) combi's cells so its near edge sits one cell beyond the host tile, centred.
    static Vector2Int CombiOffset(Vector2Int tile, Vector2Int dir, int cw, int ch)
    {
        if (dir.x == 1)  return new Vector2Int(tile.x + 1, tile.y - ch / 2);
        if (dir.x == -1) return new Vector2Int(tile.x - cw, tile.y - ch / 2);
        if (dir.y == 1)  return new Vector2Int(tile.x - cw / 2, tile.y + 1);
        return new Vector2Int(tile.x - cw / 2, tile.y - ch);   // dir.y == -1
    }

    // ----- rotation (k = number of 90° CCW turns) -----
    public static Vector2 RotPoint(Vector2 p, int k, int w, int h)
    {
        switch (((k % 4) + 4) % 4)
        {
            case 1: return new Vector2(h - p.y, p.x);
            case 2: return new Vector2(w - p.x, h - p.y);
            case 3: return new Vector2(p.y, w - p.x);
            default: return p;
        }
    }

    public static Vector2Int RotCell(Vector2Int c, int k, int w, int h)
    {
        var r = RotPoint(new Vector2(c.x + 0.5f, c.y + 0.5f), k, w, h);
        return new Vector2Int(Mathf.FloorToInt(r.x), Mathf.FloorToInt(r.y));
    }

    public static void RotDims(int k, ref int w, ref int h)
    {
        if ((((k % 4) + 4) % 4) % 2 == 1) { var t = w; w = h; h = t; }
    }

    static Vector2Int RotDir(Vector2Int d, int k)
    {
        k = ((k % 4) + 4) % 4;
        for (int i = 0; i < k; i++) d = new Vector2Int(-d.y, d.x);   // 90° CCW
        return d;
    }

    // Rotation that maps a combi's local growth (its top inlet faces -Y "up", body grows DOWN) to `dir`.
    static int RotForDir(Vector2Int dir)
    {
        if (dir == Vector2Int.right) return 1;
        if (dir == Vector2Int.up) return 2;
        if (dir == Vector2Int.left) return 3;
        return 0;   // down
    }

    static List<PocketWave> CloneWavesRot(List<PocketWave> src, int k, int w, int h, Vector2 off)
    {
        var list = new List<PocketWave>();
        if (src != null)
            foreach (var wv in src)
                list.Add(new PocketWave
                {
                    spawnDuration = wv.spawnDuration, delayAfter = wv.delayAfter, waitForClear = wv.waitForClear,
                    randomOrder = wv.randomOrder,
                    placed = ClonePlacedRot(wv.placed, k, w, h, off),
                });
        return list;
    }

    static List<PlacedObject> ClonePlacedRot(List<PlacedObject> src, int k, int w, int h, Vector2 off)
    {
        var list = new List<PlacedObject>();
        if (src != null)
            foreach (var po in src)
                list.Add(new PlacedObject { enemy = po.enemy, prefab = po.prefab, pos = RotPoint(po.pos, k, w, h) + off });
        return list;
    }

    static void ShiftPlaced(List<PocketWave> waves, Vector2 shift)
    {
        foreach (var w in waves) ShiftList(w.placed, shift);
    }

    static void ShiftList(List<PlacedObject> placed, Vector2 shift)
    {
        for (int i = 0; i < placed.Count; i++)
        {
            var po = placed[i]; po.pos += shift; placed[i] = po;
        }
    }

    // Scan every in-bounds origin for this size and keep the valid one whose centre is FURTHEST from
    // the entry (origin). Guarantees the boss is the deepest pocket regardless of the random layout.
    bool FurthestPlace(PocketInstance inst, int pw, int ph, int W, int H, int entryClear, List<PocketInstance> existing)
    {
        int oxMin = -W / 2 + areaMargin, oxMax = W / 2 - areaMargin - pw;
        int oyMin = -H / 2 + areaMargin, oyMax = H / 2 - areaMargin - ph;
        float best = -1f;
        RectInt bestRect = default;
        bool found = false;
        for (int ox = oxMin; ox <= oxMax; ox++)
            for (int oy = oyMin; oy <= oyMax; oy++)
            {
                var rect = new RectInt(ox, oy, pw, ph);
                if (!Fits(rect, W, H, entryClear, existing)) continue;
                float cx = ox + pw / 2f, cy = oy + ph / 2f;   // centre vs entry at (0,0)
                float d = cx * cx + cy * cy;
                if (d > best) { best = d; bestRect = rect; found = true; }
            }
        if (found) inst.cellRect = bestRect;
        return found;
    }

    PocketInstance MakeInst(PocketTemplate t)
        => new PocketInstance { template = t, weight = Random.Range(t.weightRange.x, t.weightRange.y) };

    bool TryPlaceRing(PocketInstance inst, int pw, int ph, int targetR, int W, int H, int entryClear, List<PocketInstance> existing)
    {
        for (int a = 0; a < 200; a++)
        {
            float ang = Random.value * Mathf.PI * 2f;
            float r = targetR * Random.Range(0.8f, 1.15f);
            int cx = Mathf.RoundToInt(Mathf.Cos(ang) * r);
            int cy = Mathf.RoundToInt(Mathf.Sin(ang) * r);
            var rect = new RectInt(cx - pw / 2, cy - ph / 2, pw, ph);
            if (Fits(rect, W, H, entryClear, existing)) { inst.cellRect = rect; return true; }
        }
        return false;
    }

    // Exhaustive fallback: scan EVERY in-bounds origin for this exact pocket size so we never report a
    // false "couldn't place" — if a non-overlapping spot exists for the pocket's actual w×h, we find it.
    // Iterates by top-left origin so the pocket's full footprint is what's bounds/overlap-tested.
    bool ScanPlace(PocketInstance inst, int pw, int ph, int W, int H, int entryClear, List<PocketInstance> existing)
    {
        int oxMin = -W / 2 + areaMargin, oxMax = W / 2 - areaMargin - pw;
        int oyMin = -H / 2 + areaMargin, oyMax = H / 2 - areaMargin - ph;
        for (int ox = oxMin; ox <= oxMax; ox++)
            for (int oy = oyMin; oy <= oyMax; oy++)
            {
                var rect = new RectInt(ox, oy, pw, ph);
                if (Fits(rect, W, H, entryClear, existing)) { inst.cellRect = rect; return true; }
            }
        return false;
    }

    bool Fits(RectInt rect, int W, int H, int entryClear, List<PocketInstance> existing)
    {
        // bounds (the FULL footprint must sit inside the playable area)
        if (rect.xMin < -W / 2 + areaMargin || rect.xMax > W / 2 - areaMargin) return false;
        if (rect.yMin < -H / 2 + areaMargin || rect.yMax > H / 2 - areaMargin) return false;
        var entry = new RectInt(-entryClear, -entryClear, entryClear * 2, entryClear * 2);
        if (rect.Overlaps(entry)) return false;
        // keep >= pocketGap solid cells between pockets so they never merge
        foreach (var e in existing)
            if (rect.Overlaps(Inflate(e.cellRect, pocketGap))) return false;
        return true;
    }

    static RectInt Inflate(RectInt r, int by) => new RectInt(r.xMin - by, r.yMin - by, r.width + 2 * by, r.height + 2 * by);

    void ClearPockets()
    {
        foreach (var p in pockets) if (p != null) Destroy(p.gameObject);
        pockets.Clear();
        foreach (var s in spawners) if (s != null) Destroy(s.gameObject);
        spawners.Clear();
        // era regen: the old dungeon's frozen enemies belong to a dungeon that no longer exists
        foreach (var g in frozen) if (g != null) Destroy(g);
        frozen.Clear();
        activePocket = null;
    }

    // Minimal in-code library so generation/carving is testable before MineForge (Phase 3) exists.
    EraMine FallbackEra()
    {
        var em = new EraMine();
        em.EnsureBossPocket();
        em.pockets.Add(new PocketTemplate { name = "Test A", width = 8, height = 8 });
        em.pockets.Add(new PocketTemplate { name = "Test B", width = 10, height = 10 });
        return em;
    }
}
