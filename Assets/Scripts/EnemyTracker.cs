using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

public class EnemyTracker : MonoBehaviour
{
    public static EnemyTracker i;
    private ObjectPool<Director> dirs;
    [SerializeField] Director dir;
    public static List<List<Transform>> enemies = new List<List<Transform>>();
    // Directors that are in‑use this frame (returned to the pool at the start of the next)
    private readonly List<Director> activeDirs = new();
    public List<Sprite> sprs1; //every sprite img for each enemy in the game
    public List<Sprite> sprs2;
    public List<Sprite> sprs3;
    List<List<Sprite>> sprs;
    public static float dist = 3f;
    private int iteration = 0;
    // private int dirCursor = 0;
    private int delta;
    [SerializeField] private Camera cam;
    List<Director>[] dirList = new List<Director>[6];

    // Pre-wave preview: while peaceful (forecast) or armed, point Directors at where the planned wave
    // WILL spawn. Markers are lightweight empty Transforms at the boundary positions, grouped by the
    // MarauderSO so each type previews with its OWN sprite + count. Keyed by SO (not enemyID) and the
    // icon is read straight from the prefab, so EVERY planned spawn shows — the full wave, exactly what
    // Acco will spawn.
    private bool previewMode;
    private readonly List<Transform> previewMarkers = new();                          // flat list, for teardown
    private readonly Dictionary<MarauderSO, Sprite> previewSpriteCache = new();
    private readonly List<Director> previewDirs = new();
    private Transform previewContainer;

    private void Start()
    {
        GS.OnNewEra += _ =>  delta = Mathf.FloorToInt(sprs[GS.era].Count / 6f);
    }

    private void Awake()
    {
        i = this;
        previewContainer = new GameObject("WavePreviewMarkers").transform;
        previewContainer.SetParent(transform);
        for(int i = 0; i < 6; i++)
        {
            dirList[i] = new List<Director>();
        }
        // Fallback to the overlay camera if none has been set in the Inspector
        if (cam == null)
            cam = CameraScript.i.cam;
        sprs = new List<List<Sprite>> { sprs1, sprs2, sprs3 };
        enemies = new List<List<Transform>>();
        for (int i = 0; i < 20; i++)
        {
            enemies.Add(new());
        }
        delta = Mathf.FloorToInt(sprs[GS.era].Count / 6f);
        dirs = new ObjectPool<Director>(() => Instantiate(dir, UIManager.i.directors),
            d =>
            {
                d.gameObject.SetActive(true);
                d.enabled = true;          // no delay – update runs immediately
                d.transform.localScale = Vector3.one;
            },
            d =>
            {
                d.gameObject.SetActive(false);
            },
            d => Destroy(d.gameObject),
            false, 20, 200);
        GS.OnNewEra += _ =>
        {
            Director.maxdistance *= 2f;
        };
    }
    
    public void Update() //batched to be in 6 intervals
    {
        if (previewMode) { ResolvePreviewOverlaps(); return; } // built once; per-frame pass only fans overlaps
        dirList[iteration].ForEach(x=>
        {
            x.inUse = false;
            x.ts.Clear();
        });
        dirList[iteration].Clear();
        if (iteration == 5)
        {
            for (int i = iteration * delta; i < sprs[GS.era].Count; i++)
            {
                try
                {
                    Batch(enemies[i], sprs[GS.era][i], dirList[iteration]);
                }
                catch
                {
                    Debug.Log(i);
                }

            }
        }
        else
        {
            for(int i = iteration * delta; i < iteration * delta + delta; i++)
            {
                Batch(enemies[i],sprs[GS.era][i], dirList[iteration]);
            }
        }
        iteration++;
        if (iteration > 5)
        {
            iteration = 0;
        }
    }

    // Groups off‑screen enemies that share a similar angle and spawns one Director per group.
    // Directors created here are appended to <paramref name="target"/> so the caller owns recycling
    // (live waves rotate through dirList[6]; the pre-wave preview keeps its own list).
    void Batch(List<Transform> en, Sprite spr, List<Director> target, bool alwaysShow = false)
    {
        if (en == null || en.Count == 0) return;
        List<Transform> pts = new List<Transform>();

        // Live Directors only care about off-screen enemies; preview Directors (alwaysShow) keep a
        // Director for EVERY spawn — including on-screen ones — so the forecast doesn't vanish when
        // you walk up to a spawn point (Director then hugs it and points at (0,0)).
        const float MARGIN = -0.05f;
        foreach (var t in en)
        {
            if (t == null) continue;
            if (alwaysShow)
            {
                pts.Add(t);
                continue;
            }
            Vector3 vp = cam.WorldToViewportPoint(t.position);
            if (vp.x < MARGIN || vp.x > 1f - MARGIN || vp.y < MARGIN || vp.y > 1f - MARGIN)
            {
                pts.Add(t);
            }
        }
        if (pts.Count == 0) return;

        Vector3 playerPos = CharacterScript.CS.transform.position;
        const float ANGLE_THRESHOLD = 15f; // degrees

        // Simple angular clustering
        List<List<Transform>> clusters = new List<List<Transform>>();
        foreach (var t in pts)
        {
            float ang = Mathf.Atan2(t.position.y - playerPos.y, t.position.x - playerPos.x) * Mathf.Rad2Deg;
            bool placed = false;

            foreach (var cluster in clusters)
            {
                float cAng = Mathf.Atan2(cluster[0].position.y - playerPos.y, cluster[0].position.x - playerPos.x) * Mathf.Rad2Deg;
                if (Mathf.Abs(Mathf.DeltaAngle(cAng, ang)) < ANGLE_THRESHOLD)
                {
                    cluster.Add(t);
                    placed = true;
                    break;
                }
            }

            if (!placed)
            {
                clusters.Add(new List<Transform> { t });
            }
        }

        // Spawn a Director for each cluster
        foreach (var cluster in clusters)
        {
            // Find an unused Director first
            Director d = null;
            foreach (var cand in activeDirs)
            {
                if (!cand.inUse)
                {
                    d = cand;
                    break;
                }
            }

            // If every Director is busy, pull a fresh one from the pool
            if (d == null)
            {
                d = dirs.Get();
                activeDirs.Add(d);
            }

            d.inUse = true;
            d.alwaysShow = alwaysShow;
            d.Set(cluster, spr);
            target.Add(d);
            d.SetVisuals(false);
            d.gameObject.SetActive(true);
        }
    }

    /// <summary>
    /// Spawn a marker per planned enemy at its (core-relative) boundary position and switch the
    /// Director system into preview mode. Call while peaceful so players can see where the next
    /// wave will come from. Resolves each spawn's t-offset against its core's current boundary t.
    /// </summary>
    public void ShowPreview(WavePlan plan)
    {
        HidePreview(); // tear down any previous preview (Directors + markers) before rebuilding
        if (plan != null)
        {
            foreach (CorePlan cp in plan.cores)
            {
                if (cp == null || cp.core == null) continue;
                float coreT = MapManager.i.BoundaryT(cp.core.transform.position);

                // Group this core's planned spawns by enemy type.
                var groups = new Dictionary<MarauderSO, List<Transform>>();
                var order = new List<MarauderSO>();
                foreach (PlannedSpawn s in cp.spawns)
                {
                    if (s.so == null) continue; // every real spawn gets a marker — the full wave
                    // Tiny jitter so no two markers ever coincide exactly (stable overlap ordering).
                    Vector2 pos = MapManager.i.BoundaryWorldAtT(coreT + s.tOffset) + UnityEngine.Random.insideUnitCircle * 0.05f;
                    var marker = new GameObject("WaveMarker").transform;
                    marker.SetParent(previewContainer);
                    marker.position = pos;
                    previewMarkers.Add(marker);
                    if (!groups.TryGetValue(s.so, out var list)) { list = new List<Transform>(); groups[s.so] = list; order.Add(s.so); }
                    list.Add(marker);
                }

                // One PERSISTENT Director per spatial sub-cluster of each type: spread-out same-type
                // enemies get their OWN Director at their real location instead of one lumped centroid.
                // Built ONCE (no per-frame rebuild) — counts don't flicker, no "0" ghosts, no type dropped.
                foreach (var so in order)
                {
                    foreach (var sub in ClusterByProximity(groups[so], PREVIEW_CLUSTER_DIST))
                    {
                        Director d = GetPreviewDirector();
                        d.alwaysShow = true;
                        d.previewOffset = Vector2.zero; // ResolvePreviewOverlaps fans any that still collide on screen
                        d.Set(sub, SpriteFor(so));
                        d.SetVisuals(true);
                        d.gameObject.SetActive(true);
                        previewDirs.Add(d);
                    }
                }
            }
        }
        previewMode = true;
    }

    // World distance under which same-type preview markers merge into one Director (else they split,
    // so spread-out same-type enemies aren't lumped into a single misleading centroid).
    const float PREVIEW_CLUSTER_DIST = 2f;

    static List<List<Transform>> ClusterByProximity(List<Transform> markers, float dist)
    {
        var clusters = new List<List<Transform>>();
        float d2 = dist * dist;
        foreach (var m in markers)
        {
            List<Transform> best = null;
            foreach (var c in clusters)
                if (((Vector2)c[0].position - (Vector2)m.position).sqrMagnitude < d2) { best = c; break; }
            if (best == null) clusters.Add(new List<Transform> { m });
            else best.Add(m);
        }
        return clusters;
    }

    // Declump overlapping preview Directors. Clusters of overlapping Directors are laid out as a compact
    // hex spiral around their centre (vertical stacking that nestles between neighbours, like circle
    // packing), then a few relaxation passes clear any residual overlap between neighbouring clusters.
    // Gap scales with each Director's on-screen size. Runs each frame over the fixed set; only nudges
    // previewOffset (no rebuild / recycling, so no flicker or "0" ghosts). Live radar Directors untouched.
    // Half the minimum on-screen separation between preview Directors (× their current scale). Bigger =
    // more breathing room before they're considered overlapping / how far they're pushed apart.
    const float PREVIEW_HALF_SEP = 50f;

    void ResolvePreviewOverlaps()
    {
        var ds = new List<Director>();
        foreach (var d in previewDirs)
            if (d != null && d.gameObject.activeSelf) ds.Add(d);
        int n = ds.Count;
        if (n == 0) return;
        if (n == 1) { ds[0].previewOffset = Vector2.zero; return; }

        var basePos = new Vector2[n];
        var rad = new float[n];
        for (int i = 0; i < n; i++)
        {
            basePos[i] = ds[i].basePos; // intended (offset-free) position -> stable, no drift
            float scale = ds[i].rt != null ? ds[i].rt.localScale.x : 1f;
            rad[i] = PREVIEW_HALF_SEP * Mathf.Max(0.25f, scale); // half the min separation at the current zoom
        }

        // 1. Cluster Directors whose base positions overlap (union-find, transitive).
        var parent = new int[n];
        for (int i = 0; i < n; i++) parent[i] = i;
        for (int a = 0; a < n; a++)
            for (int b = a + 1; b < n; b++)
                if (Vector2.Distance(basePos[a], basePos[b]) < rad[a] + rad[b])
                    Union(parent, a, b);

        // 2. Hex-pack each cluster around its centroid (compact staggered stacking).
        var pos = new Vector2[n];
        var members = new Dictionary<int, List<int>>();
        for (int i = 0; i < n; i++)
        {
            int r = FindRoot(parent, i);
            if (!members.TryGetValue(r, out var g)) { g = new List<int>(); members[r] = g; }
            g.Add(i);
        }
        foreach (var g in members.Values)
        {
            if (g.Count == 1) { pos[g[0]] = basePos[g[0]]; continue; }

            Vector2 c = Vector2.zero;
            float dia = 0f;
            foreach (int idx in g) { c += basePos[idx]; dia = Mathf.Max(dia, 2f * rad[idx]); }
            c /= g.Count;
            // stable assignment (left-to-right) so each Director keeps a consistent side/cell
            g.Sort((x, y) => basePos[x].x != basePos[y].x ? basePos[x].x.CompareTo(basePos[y].x) : basePos[x].y.CompareTo(basePos[y].y));

            if (g.Count == 2)
            {
                // Exactly two overlapping: side by side horizontally — never stack one above the other.
                pos[g[0]] = c + new Vector2(-dia * 0.5f, 0f);
                pos[g[1]] = c + new Vector2(+dia * 0.5f, 0f);
                continue;
            }

            // 3+ overlapping: compact hex formation — the only case that stacks vertically.
            for (int k = 0; k < g.Count; k++) pos[g[k]] = c + HexSpiralOffset(k, dia);
        }

        // 3. A few relaxation passes to clear any residual overlap between neighbouring clusters.
        for (int it = 0; it < 6; it++)
        {
            bool any = false;
            for (int a = 0; a < n; a++)
                for (int b = a + 1; b < n; b++)
                {
                    float minD = rad[a] + rad[b];
                    Vector2 delta = pos[b] - pos[a];
                    float dmag = delta.magnitude;
                    if (dmag < minD)
                    {
                        Vector2 dir = dmag > 0.0001f ? delta / dmag : new Vector2(0f, 1f);
                        float push = (minD - dmag) * 0.5f;
                        pos[a] -= dir * push;
                        pos[b] += dir * push;
                        any = true;
                    }
                }
            if (!any) break;
        }

        for (int i = 0; i < n; i++) ds[i].previewOffset = pos[i] - basePos[i];
    }

    static int FindRoot(int[] p, int x) { while (p[x] != x) { p[x] = p[p[x]]; x = p[x]; } return x; }
    static void Union(int[] p, int a, int b) { int ra = FindRoot(p, a), rb = FindRoot(p, b); if (ra != rb) p[ra] = rb; }

    // Canvas offset of the index-th cell of a hex spiral (0 = centre), cells `diameter` apart — gives the
    // compact "( 0 x 0 ) / ( x 0 x )" staggered packing.
    static Vector2 HexSpiralOffset(int index, float diameter)
    {
        if (index <= 0) return Vector2.zero;
        int ring = 1, rem = index - 1;
        while (rem >= 6 * ring) { rem -= 6 * ring; ring++; }
        int edge = rem / ring, step = rem % ring;
        int[] dq = { 1, 1, 0, -1, -1, 0 };   // axial hex directions (pointy-top)
        int[] dr = { 0, -1, -1, 0, 1, 1 };
        int q = ring * dq[4], r = ring * dr[4];          // start at a corner
        for (int e = 0; e < edge; e++) { q += ring * dq[e]; r += ring * dr[e]; }
        q += step * dq[edge];
        r += step * dr[edge];
        const float vStretch = 1.25f; // push hex layers much further apart vertically (taller stacking)
        return new Vector2(diameter * (q + r * 0.5f), diameter * 0.8660254f * vStretch * r);
    }

    // Pull a recyclable Director (reuse a free one, else a fresh pool instance) and mark it in-use.
    Director GetPreviewDirector()
    {
        foreach (var cand in activeDirs)
            if (!cand.inUse) { cand.inUse = true; return cand; }
        var d = dirs.Get();
        d.inUse = true;
        activeDirs.Add(d);
        return d;
    }

    // Icon for a marauder, read straight from its prefab's SpriteRenderer (cached). Null is fine — the
    // Director still shows its count, so no planned spawn is ever dropped from the preview.
    private Sprite SpriteFor(MarauderSO so)
    {
        if (so == null) return null;
        if (previewSpriteCache.TryGetValue(so, out var s)) return s;
        s = null;
        if (so.prefab != null)
        {
            var sr = so.prefab.GetComponentInChildren<SpriteRenderer>(true);
            if (sr != null) s = sr.sprite;
        }
        previewSpriteCache[so] = s;
        return s;
    }

    /// <summary>Tear down the pre-wave preview (called as the wave is summoned).</summary>
    public void HidePreview()
    {
        previewMode = false;
        previewDirs.ForEach(x =>
        {
            x.inUse = false;
            x.previewOffset = Vector2.zero;
            x.ts.Clear();
            x.gameObject.SetActive(false);
        });
        previewDirs.Clear();
        ClearMarkers();
    }

    private void ClearMarkers()
    {
        foreach (Transform m in previewMarkers)
        {
            if (m) Destroy(m.gameObject);
        }
        previewMarkers.Clear();
    }

}