using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Replaces Room. One carved cavity in the mine. Dormant until the player tunnels in
/// (MineDungeonManager routes MineField.OnCavityBreached here), then spawns its authored enemies in
/// recorded ORDER, trimmed so their cumulative price never exceeds the pocket's normalised points.
/// Core/boss pockets (confinement, waves-as-arena, core placement, era advance) are wired in Phase 4.
/// </summary>
public class Pocket : MonoBehaviour
{
    public PocketInstance Instance { get; private set; }
    public bool Discovered { get; private set; }
    public bool Cleared { get; private set; }

    PocketTemplate T => Instance.template;
    readonly List<GameObject> alives = new List<GameObject>();
    readonly List<float> alivePts = new List<float>();   // price of each alive (lockstep with alives)
    float unspawnedPoints;                               // authored points not yet spawned
    float darkness = 1f;
    EmbersEdge EE;          // core/boss pockets only
    Coroutine spawnRoutine;
    EmberTether tether;     // the harvesting leash cast into this pocket on discovery
    bool corePassive;       // a dormant core is sitting in the room, waiting for the tether
    bool coreActivated;
    bool promptShown;       // the "V — Tether The Core" key prompt is up
    bool geometryRevealed;  // showPockets cheat: walls/fog already cleared, guards against re-revealing

    const float CORE_INTERACT_RANGE = 2f;   // how close the player must be to throw the tether on

    public void Init(PocketInstance inst)
    {
        Instance = inst;
    }

    /// <summary>
    /// showPockets debug cheat: open this pocket's walls/fog right now (same geometry a real breach
    /// produces) WITHOUT spawning anything or marking it discovered. Enemies still only spawn once the
    /// dungeon is actually excavated into it — MineField.BreakCell fires the normal discovery event
    /// itself the moment a wall bordering this (already-open) pocket gets mined.
    /// </summary>
    public void RevealNow()
    {
        if (MineField.i == null) return;
        if (TryMarkRevealed(out int pid)) MineField.i.RevealPocketGeometry(pid);
    }

    /// <summary>
    /// Claim this pocket for a BATCH reveal (MineDungeonManager.RevealAllPockets): flags it revealed
    /// and hands back its MineField index so the caller can clear every pocket in ONE batched pass
    /// instead of per-pocket calls. Returns false if there's nothing to reveal.
    /// </summary>
    public bool TryMarkRevealed(out int pid)
    {
        pid = Instance != null ? Instance.pocketIndex : -1;
        if (Discovered || geometryRevealed || pid < 0) return false;
        geometryRevealed = true;
        return true;
    }

    public void OnDiscover()
    {
        if (Discovered) return;
        Discovered = true;
        MineDungeonManager.i.activePocket = this;
        CastTether();                  // the ember tether shoots into the room centre, fishing-rod style
        SpawnExtras();                 // per-pocket props/obstacles appear at once on discovery
        SpawnStatusTiles();            // painted status floor-tiles (Stun/Slow/Speed/Root/Launch)
        if (T.isBoss)
        {
            SetupCore();               // the boss arena arms immediately (boss + core, confined)
            spawnRoutine = StartCoroutine(SpawnRoutine());
        }
        else if (T.hasCore)
        {
            SpawnPassiveCore();        // the core idles, empty room — waves only start once tethered
        }
        else
        {
            spawnRoutine = StartCoroutine(SpawnRoutine());
        }
    }

    // Cast the ember disc from the player into the pocket centre. Its leash length is
    // max(player distance at breach, furthest cavity point) + 1 — the room always fits inside it.
    // The pocket's authored enemy points drive the disc's "left until complete" bar, and its ember
    // value (1 by default) is what completion sends home.
    void CastTether()
    {
        Vector2 anchor = WorldCenter();
        float playerD = GS.CS() != null ? Vector2.Distance(GS.CS().position, anchor) : 0f;
        float len = Mathf.Max(playerD, FurthestCavityDist(anchor)) + 1f;
        unspawnedPoints = MineAuthoringSO.PocketAutoPoints(T);
        tether = EmberTether.Cast(this, anchor, len, unspawnedPoints, T.EmberValue);
    }

    /// <summary>
    /// Points still standing between the player and a complete room: everything not yet spawned plus
    /// everything alive. This is the SAME truth the clear check uses (alives), so the tether bar can't
    /// be confused by phase/splitting enemies (Discer etc.) firing extra death hooks.
    /// </summary>
    public float RemainingPoints()
    {
        CountAlive();   // prunes dead entries (and their prices) first
        float s = unspawnedPoints;
        for (int k = 0; k < alivePts.Count; k++) s += alivePts[k];
        return s;
    }

    float FurthestCavityDist(Vector2 from)
    {
        float best = 0f;
        foreach (var c in T.Cells())
        {
            Vector2 p = PocketCellToWorld(new Vector2(c.x + 0.5f, c.y + 0.5f));
            float d = Vector2.Distance(from, p);
            if (d > best) best = d;
        }
        return best;
    }

    // A NON-BOSS core pocket opens quiet: the core sits in the room, dormant, with no enemies. The
    // player must walk up and throw the tether onto it (V) to wake it — see TryActivateCore.
    void SpawnPassiveCore()
    {
        Vector3 corePos = T.core.present ? (Vector3)PocketCellToWorld(T.core.pos) : transform.position;
        EE = Instantiate(Resources.Load<GameObject>("EmbersEdge").GetComponent<EmbersEdge>(),
            corePos, Quaternion.identity, transform);
        corePassive = true;
    }

    // Key-prompt babysitting for the passive core: show V while the player stands next to it.
    void Update()
    {
        if (!corePassive || coreActivated || EE == null || GS.CS() == null) return;
        bool near = Vector2.Distance(GS.CS().position, EE.transform.position) < CORE_INTERACT_RANGE;
        if (near && !promptShown)
        {
            // only claim the guide if no other system currently owns a "V" (never steal/duplicate)
            if (UIManager.keyGuides != null && UIManager.keyGuides.ContainsKey("V")) return;
            UIManager.MakeKey("V", EE.transform.position + Vector3.down * 1.2f, "Tether The Core");
            promptShown = true;
        }
        else if (!near && promptShown)
        {
            UIManager.DeleteKey("V");
            promptShown = false;
        }
    }

    /// <summary>
    /// Throw the tether onto the passive core (V near it — EmberTether.HandleRecall routes here first).
    /// The core wakes, the tether line takes the era colour and LOCKS (no untether, no leaving), the
    /// room confines like a boss arena and the authored waves begin. Cleared => the core is claimable.
    /// </summary>
    public bool TryActivateCore()
    {
        if (!corePassive || coreActivated || EE == null || GS.CS() == null) return false;
        if (Vector2.Distance(GS.CS().position, EE.transform.position) > CORE_INTERACT_RANGE) return false;

        coreActivated = true;
        corePassive = false;
        if (promptShown) { UIManager.DeleteKey("V"); promptShown = false; }

        EE.Activate(1f, SampleSOs());
        if (PortalScript.i != null) PortalScript.i.NoPortal();
        if (tether != null) tether.AttachToCore(EE.transform.position,
            Mathf.Max(FurthestCavityDist(EE.transform.position),
                      Vector2.Distance(GS.CS().position, EE.transform.position)) + 1f);
        spawnRoutine = StartCoroutine(SpawnRoutine());
        return true;
    }

    // Per-pocket extra objects spawn instantly the moment the pocket is breached (not on a wave timer).
    // Each painted object rolls its own spawn chance — the only source of placement randomness.
    void SpawnExtras()
    {
        if (T.extras == null) return;
        foreach (var po in T.extras)
            if (po.prefab != null && Random.value <= po.SpawnChance)
                Instantiate(po.prefab, PocketCellToWorld(po.pos), Quaternion.identity, GS.FindParent(GS.Parent.misc));
    }

    // Painted status TILES (Stun/Slow/Speed/Root/Launch) are open cavity cells that carry a permanent
    // floor-effect entity — spawn one FloorTile at each on discovery (it re-parents under this pocket).
    void SpawnStatusTiles()
    {
        var auth = MineDungeonManager.i != null ? MineDungeonManager.i.Authoring : null;
        if (auth == null) return;
        foreach (var st in T.StatusTiles())
        {
            var prefab = auth.StatusTilePrefab(st.type);
            if (prefab != null)
                Instantiate(prefab, PocketCellToWorld(new Vector2(st.cell.x, st.cell.y)),
                            Quaternion.identity, GS.FindParent(GS.Parent.misc));
        }
    }

    // BOSS pocket: spawn the claimable Ember core armed from the first moment and trap the player in a
    // base-style arena. (Non-boss core pockets instead spawn a PASSIVE core — see SpawnPassiveCore.)
    void SetupCore()
    {
        Vector3 corePos = T.core.present ? (Vector3)PocketCellToWorld(T.core.pos) : transform.position;
        EE = Instantiate(Resources.Load<GameObject>("EmbersEdge").GetComponent<EmbersEdge>(),
            corePos, Quaternion.identity, transform);
        EE.Activate(T.isBoss ? 3f : 1f, SampleSOs());
        coreActivated = true;

        if (PortalScript.i != null) PortalScript.i.NoPortal();
        // The core owns the tether from the start of a boss fight: era colour + unbreakable.
        if (tether != null) tether.AttachToCore(EE.transform.position,
            Mathf.Max(FurthestCavityDist(EE.transform.position),
                GS.CS() != null ? Vector2.Distance(GS.CS().position, EE.transform.position) : 0f) + 1f);
    }

    MarauderSO[] SampleSOs()
    {
        var list = new List<MarauderSO>();
        foreach (var po in T.AllPlaced()) if (po.enemy != null && !list.Contains(po.enemy)) list.Add(po.enemy);
        return list.ToArray();
    }

    /// <summary>World centre of this pocket's cavity — entities painted in orient "inward" toward it.</summary>
    public Vector2 WorldCenter()
    {
        if (MineField.i == null || Instance == null) return transform.position;
        RectInt r = Instance.cellRect;
        return MineField.i.CellCenterWorld(new Vector3Int((r.xMin + r.xMax) / 2, (r.yMin + r.yMax) / 2, 0));
    }

    Vector2[] WorldPolygon()
    {
        RectInt r = Instance.cellRect;
        int x0 = r.xMin + 1, x1 = r.xMax - 1, y0 = r.yMin + 1, y1 = r.yMax - 1;
        return new[]
        {
            (Vector2)MineField.i.CellCenterWorld(new Vector3Int(x0, y0, 0)),
            (Vector2)MineField.i.CellCenterWorld(new Vector3Int(x1, y0, 0)),
            (Vector2)MineField.i.CellCenterWorld(new Vector3Int(x1, y1, 0)),
            (Vector2)MineField.i.CellCenterWorld(new Vector3Int(x0, y1, 0)),
        };
    }

    IEnumerator SpawnRoutine()
    {
        yield return new WaitForSeconds(0.5f);
        yield return StartCoroutine(SpawnWaves());
        unspawnedPoints = 0f;   // everything that will ever spawn has spawned (budget skips included)
        while (CountAlive() > 0) yield return null;
        OnCleared();
    }

    // Every pocket spawns its waves in order; each wave holds its own free-form enemy placements and
    // its own timing/clear-gate. Enemies trim to the pocket's normalised point budget across all waves.
    IEnumerator SpawnWaves()
    {
        float budget = Instance.normalizedPoints;
        float spent = 0f;
        if (T.waves == null) yield break;

        foreach (var wave in T.waves)
        {
            if (wave?.placed == null) continue;
            int toSpawn = wave.placed.Count;
            float gap = toSpawn > 0 && wave.spawnDuration > 0f ? wave.spawnDuration / toSpawn : 0f;

            // By default sample which placement spawns when at random (Fisher–Yates over the indices);
            // when randomOrder is off, fall back to authored top-to-bottom list order.
            var order = new List<int>(toSpawn);
            for (int i = 0; i < toSpawn; i++) order.Add(i);
            if (wave.randomOrder)
                for (int i = toSpawn - 1; i > 0; i--)
                {
                    int j = Random.Range(0, i + 1);
                    (order[i], order[j]) = (order[j], order[i]);
                }

            bool budgetHit = false;
            foreach (int idx in order)
            {
                var po = wave.placed[idx];
                float cost = MineAuthoringSO.EnemyPoints(po.enemy);
                if (po.enemy != null && spent + cost > budget) { budgetHit = true; break; }
                spent += cost;   // enemies always spawn — spawn chance is an extras-only concept
                SpawnPlaced(po);
                if (gap > 0f) yield return new WaitForSeconds(gap);
            }
            if (budgetHit) break;

            if (wave.waitForClear)
                while (CountAlive() > 0) yield return null;
            if (wave.delayAfter > 0f) yield return new WaitForSeconds(wave.delayAfter);
        }
    }

    // Convert a free-form cell-space position within the pocket to a world position.
    Vector2 PocketCellToWorld(Vector2 cellPos)
    {
        if (MineField.i == null) return (Vector2)transform.position;
        RectInt r = Instance.cellRect;
        Vector2 baseWorld = (Vector2)MineField.i.CellCenterWorld(new Vector3Int(r.xMin, r.yMin, 0));
        return baseWorld + cellPos * MineField.i.CellSize;
    }

    void SpawnPlaced(PlacedObject po)
    {
        Vector2 pos = PocketCellToWorld(po.pos);

        if (po.enemy != null && po.enemy.prefab != null)
        {
            MineFX.EnemySpawnFlash(pos);        // pocket enemies materialise in a flash of ember
            MineFX.CoreHintLine(pos);           // …with a faint line toward the nearest core spot
            GameObject g = (EE != null)
                ? EE.SpawnEnemy(po.enemy.prefab, pos)
                : Instantiate(po.enemy.prefab, pos, Quaternion.identity, GS.FindParent(GS.Parent.enemies));
            float price = MineAuthoringSO.EnemyPoints(po.enemy);
            unspawnedPoints = Mathf.Max(0f, unspawnedPoints - price);
            alives.Add(g);
            alivePts.Add(price);
            foreach (LifeScript l in g.GetComponentsInChildren<LifeScript>()) l.orbSpawnPlace = transform;
            foreach (ILA ila in g.GetComponentsInChildren<ILA>()) ila.UpdateCoef(darkness);
            // its soul visibly syphons into the tether disc on death (the bar itself is driven by
            // RemainingPoints, so odd multi-death enemies can't desync it)
            LifeScript first = g.GetComponentInChildren<LifeScript>();
            if (first != null)
            {
                var tf = first.gameObject.AddComponent<TetherFuelOnDeath>();
                tf.points = price;
                if (first.onDeaths == null) first.onDeaths = new List<MonoBehaviour>();
                first.onDeaths.Add(tf);
            }
        }
        else if (po.prefab != null)
        {
            Instantiate(po.prefab, pos, Quaternion.identity, GS.FindParent(GS.Parent.misc));
        }
    }

    int CountAlive()
    {
        for (int k = alives.Count - 1; k >= 0; k--)
            if (alives[k] == null)
            {
                alives.RemoveAt(k);
                if (k < alivePts.Count) alivePts.RemoveAt(k);
            }
        return alives.Count;
    }

    void OnCleared()
    {
        if (Cleared) return;
        Cleared = true;
        // Sweep up whatever the pocket dropped: collect every wild orb within the cavity's own extent
        // (centre → furthest OPEN floor cell — not the authored grid, which may be mostly wall).
        Vector2 c = WorldCenter();
        GS.CollectOrbs(c, FurthestCavityDist(c));
        if (tether != null) tether.NotifyCleared();   // fuelled by every soul — send the ember home
        if (T.hasCore && EE != null)
        {
            // Claim the Ember core: dive to base and place it (the old absorb-core -> place-at-base loop).
            EE.transform.parent = GS.FindParent(GS.Parent.ee);
            MapManager.BeginPlace(EE, MechaSuit.lastlife);
        }
        // Boss pocket additionally advances the era via the boss enemy's own PortalScript.DefeatedBoss().
    }

    /// <summary>Stop spawning when leaving the dungeon. Live enemies are NOT destroyed any more —
    /// teleporting freezes the dungeon dimension (MineDungeonManager.OnLeaveDungeon disables every
    /// enemy in place) and the next dive resumes it, so the fight is waiting where it stood.</summary>
    public void ResetPocket()
    {
        if (spawnRoutine != null) StopCoroutine(spawnRoutine);
        if (tether != null) tether.Release();
        if (promptShown) { UIManager.DeleteKey("V"); promptShown = false; }
    }
}
