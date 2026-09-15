using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>Keeps track of the buildable grid and its visual overlay.</summary>
public class GridManager : MonoBehaviour
{
    public static GridManager i;

    #region Parameters ––––––––––––––––––––––––––––––––––––––––––––

    [Header("Grid geometry")]
    public int width  = 64;
    public int height = 64;
    public float cellSize = 1f;
    public Vector2 origin = Vector2.zero;
    [Tooltip("Extra world-unit margin, beyond the map's inner inset, that the build grid must stay inside.")]
    public float buildEdgeMargin = 1.5f;

    private Color clearColour     = new Color(0f, 0.4f, 0f, 0.5f); // green – inside constructor range
    private Color filledColour    = new Color(0.4f, 0f, 0f, 1f); // red – occupied
    private Color outColour    = new Color(0.05f, 0.05f, 0.05f, 0.5f); // black – out of range
    private Color brightClearColour   = new Color(0f, 1f, 0f, 1f); // super‑bright green
    private Color brightBlockedColour = new Color(0.8f, 0f, 0f, 1f);  // super‑bright red
    // POWER cells (see PowerReach): a buildable cell a placed pad/hub would power is blue, one inside
    // a pylon's cable reach light blue; the ghost's own projected reach is the bright variant.
    private Color energyColour            = new Color(0.05f, 0.32f, 1f, 0.55f);   // blue – buildable & pad/hub-powered
    private Color energyLightColour       = new Color(0.18f, 0.3f, 0.55f, 0.3f);  // faint blue – buildable & in a pylon's reach (a wide field: keep it quiet)
    private Color brightEnergyColour      = new Color(0.25f, 0.55f, 1f, 1f);      // super-bright blue – the ghost's reach
    private Color brightEnergyLightColour = new Color(0.3f, 0.48f, 0.8f, 0.65f);  // a ghost pylon's reach — brighter than placed, still soft

    [SerializeField] Transform buildingGrid;
    [SerializeField] SpriteRenderer block;
    [Tooltip("Per-frame time budget (ms) for (re)building overlay squares, so a map-grow spreads over a few frames instead of hitching in one.")]
    [SerializeField] float buildBudgetMs = 2f;

    #endregion

    #region Runtime state –––––––––––––––––––––––––––––––––––––––––

    bool[,] occupied;              // placed buildings
    bool[,] inRange;               // within range of a constructor this frame
    Color[,] baseColour;           // cache of the colour each tile should have when *not* highlighted
    SpriteRenderer[,] overlay;     // sprite for each cell (filled in progressively by BuildOverlayRoutine)

    // Overlay squares are pooled and reused across grows so a rebuild instantiates only the *extra* cells
    // and otherwise just repositions/recolours existing renderers — both spread over frames.
    private readonly List<SpriteRenderer> squarePool = new List<SpriteRenderer>();
    private Coroutine buildRoutine;

    Vector2Int lastAnchor = new(int.MinValue, int.MinValue);
    Vector2Int lastSize   = Vector2Int.one;
    readonly List<Vector2Int> lastPower = new List<Vector2Int>();   // the ghost's reach cells highlighted last frame

    // cells the PLACED sources power (pads/hubs blue, pylons light blue) — rebuilt by RefreshEnergyCells
    readonly HashSet<Vector2Int> padPower = new HashSet<Vector2Int>();
    readonly HashSet<Vector2Int> pylonPower = new HashSet<Vector2Int>();
    readonly List<Vector2Int> reachScratch = new List<Vector2Int>();

    private bool stopDeactivate = false;
    private bool deactivating   = false;

    #endregion

    #region Init ––––––––––––––––––––––––––––––––––––––––––––––––––

    void Awake()
    {
        i = this;
        buildingGrid.gameObject.SetActive(false);
    }

    IEnumerator Start()
    {
        // Pre-warm the grid in the background once the map has been built + scaled (MapManager scales its
        // boundary one frame into its own Start). Building it ahead of time, spread over frames, means the
        // first time the player opens build mode there's nothing to instantiate — no hitch.
        yield return null;
        yield return null;
        while (MapManager.MapBounds().size.x <= 0f) yield return null;
        EnsureGridFitsMap();
        // sources coming, going, dying, rotating → recolour the power cells while the grid is up
        if (EnergyManager.i != null) EnergyManager.i.OnPadsChanged += OnPylonChanged;
    }

    void OnEnable()  { MapManager.OnUpdateMap += OnMapRebuilt; }
    void OnDisable()
    {
        MapManager.OnUpdateMap -= OnMapRebuilt;
        if (EnergyManager.i != null) EnergyManager.i.OnPadsChanged -= OnPylonChanged;
    }

    // When the map grows (e.g. a new core expands the boundary), resize the grid in the background so it's
    // ready before the player next enters build mode. No-op if the map still fits the current grid.
    void OnMapRebuilt() { EnsureGridFitsMap(); }

    /// <summary>
    /// Size the grid to cover the whole map. Builds it on first use and grows it (never shrinks, so placed
    /// buildings stay addressable) when the map has expanded. A no-op when the current grid already fits,
    /// so it's cheap to call on every build-mode entry. The map's bounding box drives the cell count
    /// instead of a fixed width/height that could cut off before the edge.
    /// </summary>
    void EnsureGridFitsMap()
    {
        Bounds b = MapManager.MapBounds();
        if (b.size.x <= 0f || b.size.y <= 0f)
        {
            if (overlay == null) RebuildGrid(origin, width, height); // no map yet — fall back to authored size
            return;
        }

        float margin = cellSize * 2f;
        float minX = b.min.x - margin, minY = b.min.y - margin;
        float maxX = b.max.x + margin, maxY = b.max.y + margin;

        // Union with the existing coverage so a grow never drops cells that already hold buildings.
        if (overlay != null)
        {
            minX = Mathf.Min(minX, origin.x);
            minY = Mathf.Min(minY, origin.y);
            maxX = Mathf.Max(maxX, origin.x + width * cellSize);
            maxY = Mathf.Max(maxY, origin.y + height * cellSize);
        }

        // Snap origin to the cell lattice so the origin shift between grows is a whole number of cells —
        // that keeps the occupied remap an exact integer index offset.
        Vector2 newOrigin = new Vector2(Mathf.Floor(minX / cellSize) * cellSize, Mathf.Floor(minY / cellSize) * cellSize);
        int newW = Mathf.CeilToInt((maxX - newOrigin.x) / cellSize);
        int newH = Mathf.CeilToInt((maxY - newOrigin.y) / cellSize);

        if (overlay != null && newOrigin == origin && newW == width && newH == height) return; // already fits

        RebuildGrid(newOrigin, newW, newH);
    }

    /// <summary>
    /// (Re)allocate the grid at a new origin/size, carrying placed-building occupancy across. The logical
    /// arrays are allocated immediately (cheap, so placement works right away), but the visual overlay
    /// squares are (re)built over several frames by <see cref="BuildOverlayRoutine"/> to avoid a hitch.
    /// </summary>
    void RebuildGrid(Vector2 newOrigin, int newW, int newH)
    {
        var newOccupied = new bool[newW, newH];
        if (occupied != null)
        {
            int ox = Mathf.RoundToInt((origin.x - newOrigin.x) / cellSize);
            int oy = Mathf.RoundToInt((origin.y - newOrigin.y) / cellSize);
            for (int x = 0; x < width; ++x)
                for (int y = 0; y < height; ++y)
                {
                    if (!occupied[x, y]) continue;
                    int nx = x + ox, ny = y + oy;
                    if (nx >= 0 && ny >= 0 && nx < newW && ny < newH) newOccupied[nx, ny] = true;
                }

            // anchorCell is a grid-frame coordinate: re-anchoring the origin moves EVERY placed
            // building's frame. Carry the anchors (and the energy grid's registered cells) across
            // like the occupancy remap above — otherwise a building placed before a grow and one
            // placed after live in different frames, and energy adjacency (plus the OnDestroy
            // cell-free at anchorCell) misses by exactly the origin shift.
            if (ox != 0 || oy != 0)
            {
                var d = new Vector2Int(ox, oy);
                for (int i = 0; i < Building.buildings.Count; i++)
                    if (Building.buildings[i] != null) Building.buildings[i].anchorCell += d;
                EnergyManager.i?.ShiftFrame(d);
            }
        }

        origin = newOrigin; width = newW; height = newH;
        occupied   = newOccupied;
        inRange    = new bool[width, height];
        baseColour = new Color[width, height];
        overlay    = new SpriteRenderer[width, height];

        StampExistingBuildings();

        if (buildRoutine != null) StopCoroutine(buildRoutine);
        buildRoutine = StartCoroutine(BuildOverlayRoutine());
    }

    /// <summary>
    /// Assign a (pooled, reused) square renderer to every cell, position/scale/recolour it, spreading the
    /// work across frames under a per-frame time budget. New squares are instantiated only when the pool
    /// runs short (i.e. the grid grew past any previous size); otherwise existing ones are just moved.
    /// </summary>
    IEnumerator BuildOverlayRoutine()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int poolIndex = 0;

        for (int x = 0; x < width; ++x)
            for (int y = 0; y < height; ++y)
            {
                SpriteRenderer sq;
                if (poolIndex < squarePool.Count)
                {
                    sq = squarePool[poolIndex];
                }
                else
                {
                    sq = Instantiate(block, buildingGrid);
                    squarePool.Add(sq);
                }
                poolIndex++;

                var t = sq.transform;
                t.position   = GridToWorld(new Vector2Int(x, y));
                t.localScale = Vector3.one * cellSize * 0.99f;
                sq.color = baseColour[x, y];
                if (!sq.gameObject.activeSelf) sq.gameObject.SetActive(true);
                overlay[x, y] = sq;

                if (sw.Elapsed.TotalMilliseconds >= buildBudgetMs)
                {
                    yield return null;
                    sw.Restart();
                }
            }

        // Park any pooled squares left over from a previously larger grid (we currently only ever grow,
        // so this is just defensive).
        for (int k = poolIndex; k < squarePool.Count; ++k)
            if (squarePool[k] != null) squarePool[k].gameObject.SetActive(false);

        buildRoutine = null;
    }

    /// <summary>
    /// Re-stamp every live BUILT building's footprint straight from its transform onto the freshly
    /// allocated occupancy array. Starter buildings register in their Start against whatever grid
    /// exists at that moment (often the pre-map fallback, or a frame that later re-anchors), so
    /// without this sweep their cells read free and new buildings can be placed on top of them.
    /// Same footprint math as Building.RegisterGridOccupancy; anchorCell/gridSize are refreshed so
    /// demolition frees exactly these cells.
    /// </summary>
    void StampExistingBuildings()
    {
        for (int k = 0; k < Building.buildings.Count; k++)
        {
            var b = Building.buildings[k];
            if (b == null || !b.builtYet || !PathZone.AtBase(b.transform.position)) continue;

            var sizeCells = new Vector2Int(
                Mathf.Max(1, Mathf.RoundToInt(b.size.x / cellSize)),
                Mathf.Max(1, Mathf.RoundToInt(b.size.y / cellSize)));
            Vector2Int a = WorldToGrid(b.transform.position)
                           - new Vector2Int(sizeCells.x / 2, sizeCells.y / 2);
            b.anchorCell = a;
            b.gridSize = sizeCells;
            for (int x = 0; x < sizeCells.x; x++)
                for (int y = 0; y < sizeCells.y; y++)
                    if (Inside(a.x + x, a.y + y) && b.OccupiesFootprintCell(x, y, sizeCells)) occupied[a.x + x, a.y + y] = true;
        }
    }

    #endregion

    #region Public API ––––––––––––––––––––––––––––––––––––––––––––

    /// <summary>Has the grid been sized to the map yet (origin/width/height meaningful)? Pathfinding
    /// (BasePathGrid) gates on this — before the pre-warm, base queries fall back to no-walls behavior.</summary>
    public bool Built => occupied != null;

    public Vector2Int WorldToGrid(Vector3 worldPos)
    {
        var local = (Vector2)worldPos - origin;
        return new Vector2Int(
            Mathf.FloorToInt(local.x / cellSize),
            Mathf.FloorToInt(local.y / cellSize));
    }

    public Vector3 GridToWorld(Vector2Int gridPos)
        => new Vector3(
            origin.x + (gridPos.x + 0.5f) * cellSize,
            origin.y + (gridPos.y + 0.5f) * cellSize,
            0f);

    /// <summary>True if every cell in the given rectangle is both un‑occupied *and* inside constructor range.</summary>
    /// <summary>Build the grid on demand if something touches it before the first build-mode entry
    /// (e.g. a pre-placed building registering occupancy at game start).</summary>
    void EnsureBuilt() { if (overlay == null) EnsureGridFitsMap(); }

    public bool AreaClear(Vector2Int anchor, Vector2Int size)
    {
        EnsureBuilt();
        for (int y = 0; y < size.y; ++y)
            for (int x = 0; x < size.x; ++x)
            {
                int gx = anchor.x + x, gy = anchor.y + y;
                if (!Inside(gx, gy) || occupied[gx, gy] || !inRange[gx, gy])
                    return false;
            }
        return true;
    }

    public void SetArea(Vector2Int anchor, Vector2Int size, bool state)
    {
        EnsureBuilt();
        for (int y = 0; y < size.y; ++y)
            for (int x = 0; x < size.x; ++x)
            {
                int gx = anchor.x + x, gy = anchor.y + y;
                if (!Inside(gx, gy)) continue;

                occupied[gx, gy] = state;
                Color c = state || !inRange[gx, gy] ? filledColour : clearColour;
                baseColour[gx, gy] = c;
                if (overlay[gx, gy] != null) overlay[gx, gy].color = c;
            }
        
        // Refresh energy cells when buildings are placed/removed
        RefreshEnergyCells();
    }

    /// <summary>
    /// Paints / updates the preview for the current frame. Only the cells that changed since the last call are touched → cheap.
    /// • Un‑buildable cells (occupied or out of range) go bright red.
    /// • All cells in a fully‑valid footprint go bright green.
    /// • If the ghost is a power source (<paramref name="ghost"/>: pad, hub, pylon), the buildable cells
    ///   it WOULD power go bright blue (pylon reach: bright light blue) — see <see cref="PowerReach"/>.
    /// </summary>
    public void PreviewArea(Vector2Int anchor, Vector2Int size, bool valid, Building ghost = null)
    {
        // 1) Restore colours where the cursor was previously
        if (lastAnchor.x != int.MinValue)
        {
            for (int y = 0; y < lastSize.y; ++y)
                for (int x = 0; x < lastSize.x; ++x)
                {
                    int gx = lastAnchor.x + x;
                    int gy = lastAnchor.y + y;
                    if (Inside(gx, gy) && overlay[gx, gy] != null)
                    {
                        overlay[gx, gy].color = baseColour[gx, gy];
                        overlay[gx, gy].sortingLayerID = SortingLayer.NameToID("Default");
                    }
                }
        }
        for (int k = 0; k < lastPower.Count; ++k)
        {
            var c = lastPower[k];
            if (Inside(c.x, c.y) && overlay[c.x, c.y] != null)
            {
                overlay[c.x, c.y].color = baseColour[c.x, c.y];
                overlay[c.x, c.y].sortingLayerID = SortingLayer.NameToID("Default");
            }
        }
        lastPower.Clear();

        // 1b) The ghost's projected reach: every buildable cell it would power, before it's built
        if (ghost != null)
        {
            reachScratch.Clear();
            var kind = PowerReach.CellsFor(ghost, anchor, size, reachScratch);
            if (kind != PowerReach.Kind.None)
            {
                Color bright = kind == PowerReach.Kind.Pad ? brightEnergyColour : brightEnergyLightColour;
                for (int k = 0; k < reachScratch.Count; ++k)
                {
                    var c = reachScratch[k];
                    if (!Inside(c.x, c.y) || overlay[c.x, c.y] == null) continue;
                    if (occupied[c.x, c.y] || !inRange[c.x, c.y]) continue;   // red/black cells keep their meaning
                    overlay[c.x, c.y].color = bright;
                    overlay[c.x, c.y].sortingLayerID = SortingLayer.NameToID("Buildings");
                    lastPower.Add(c);
                }
            }
        }

        // 2) Highlight current footprint
        for (int y = 0; y < size.y; ++y)
            for (int x = 0; x < size.x; ++x)
            {
                int gx = anchor.x + x, gy = anchor.y + y;
                if (!Inside(gx, gy) || overlay[gx, gy] == null) continue;

                bool blocked = occupied[gx, gy] || !inRange[gx, gy];

                if (valid)
                {
                    overlay[gx, gy].color = brightClearColour;           // whole footprint valid
                }
                else if (blocked)
                {
                    overlay[gx, gy].color = brightBlockedColour;         // only the offending tiles
                }
                overlay[gx, gy].sortingLayerID = SortingLayer.NameToID("Buildings");
            }

        lastAnchor = anchor;
        lastSize   = size;
    }

    /// <summary>Called by <see cref="BM"/> when entering build mode.</summary>
    public void ActivateGrid()
    {
        if (deactivating) stopDeactivate = true;
        EnsureGridFitsMap();    // size to the (possibly grown) map before painting
        RebuildRangeCache();    // expensive work done once on entry
        buildingGrid.gameObject.SetActive(true);
        RefreshEnergyCells();   // Also refresh energy cells when grid is activated
    }

    public void DeactivateGrid()
    {
        if (deactivating) return;
        deactivating = true;
        StartCoroutine(DoDeactivate());

        IEnumerator DoDeactivate()
        {
            yield return null;  // allow one frame for BM to finish up
            yield return null;

            if (stopDeactivate)
            {
                deactivating   = false;
                stopDeactivate = false;
                yield break;
            }

            BM.i.ChangeBuildingColour(true);
            buildingGrid.gameObject.SetActive(false);
            lastAnchor = new Vector2Int(int.MinValue, int.MinValue); // forget cached highlight
            lastPower.Clear();
            deactivating = false;
        }
    }

    /// <summary>Called when pylons are added or removed to update energy display.</summary>
    public void OnPylonChanged()
    {
        if (buildingGrid.gameObject.activeSelf)
        {
            RefreshEnergyCells();
        }
    }

    #endregion

    #region Internals –––––––––––––––––––––––––––––––––––––––––––––––

    bool Inside(int x, int y) => x >= 0 && y >= 0 && x < width && y < height;

    /// <summary>
    /// Re‑computes which tiles are inside any constructor's radius and caches the base grid colours.
    /// This runs *once* on entering build mode, so the grid can be repainted very cheaply each frame.
    /// </summary>
    public void RebuildRangeCache()
    {
        if (overlay == null) return; // grid not built yet

        // The buildable grid is the map's inner inset (pulled in by buildEdgeMargin) — nothing else.
        // The Constructor's radius used to gate it too; the Constructor is retired (user call
        // 2026-09-14: "outdated technology" — buildings are ore-built now), so the whole base is
        // buildable right up to the margin. Computed once here, then a cheap point-in-polygon per cell.
        Vector2[] buildable = MapManager.GetBuildableBoundary(buildEdgeMargin);

        for (int gx = 0; gx < width; ++gx)
            for (int gy = 0; gy < height; ++gy)
            {
                Vector3 cellWorld = GridToWorld(new Vector2Int(gx, gy));
                bool range = buildable == null || MapManager.PointInPoly(cellWorld, buildable);

                inRange[gx, gy] = range;
                bool blocked = occupied[gx, gy];
                Color col = blocked ? filledColour : !range ? outColour : clearColour;
                baseColour[gx, gy] = col;
                if (overlay[gx, gy] != null) overlay[gx, gy].color = col;
            }
    }

    #endregion

    /// <summary>Re‑computes which buildable cells the placed power sources reach (pads/hubs → blue,
    /// pylon cable reach → light blue; generators reach nothing — they're connected TO) and recolours
    /// the overlay. Placed-but-unbuilt sources count too, so a hub still waiting on its ore already
    /// shows where it will power; dead (rebuilding) ones don't.</summary>
    public void RefreshEnergyCells()
    {
        if (overlay == null) return; // grid not built yet; ActivateGrid will refresh once it is
        CollectPower();
        for (int gx = 0; gx < width; ++gx)
            for (int gy = 0; gy < height; ++gy)
            {
                Color targetColour;
                if (occupied[gx, gy]) targetColour = filledColour;            // red – occupied
                else if (!inRange[gx, gy]) targetColour = outColour;          // black – out of range
                else
                {
                    var cell = new Vector2Int(gx, gy);
                    targetColour = padPower.Contains(cell) ? energyColour          // blue – powered by a pad/hub
                        : pylonPower.Contains(cell) ? energyLightColour           // light blue – a pylon can cable here
                        : clearColour;                                            // green – buildable, no power
                }
                baseColour[gx, gy] = targetColour;
                if (overlay[gx, gy] != null) overlay[gx, gy].color = targetColour;
            }
    }

    void CollectPower()
    {
        padPower.Clear();
        pylonPower.Clear();
        for (int k = 0; k < Building.buildings.Count; ++k)
        {
            var b = Building.buildings[k];
            if (b == null || (b.builtYet && !b.enabled)) continue;             // dead / rebuilding
            if (!PathZone.AtBase(b.transform.position)) continue;
            reachScratch.Clear();
            var kind = PowerReach.CellsFor(b, b.anchorCell, b.gridSize, reachScratch);
            if (kind == PowerReach.Kind.Pad) padPower.UnionWith(reachScratch);
            else if (kind == PowerReach.Kind.Pylon) pylonPower.UnionWith(reachScratch);
        }
    }
}