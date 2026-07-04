using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.Rendering.Universal;

/// <summary>
/// The solid, mineable tile-field that fills the dungeon. Owns a Grid (0.25u cells) plus three
/// tilemaps it creates in code:
///   • render    — every solid cell (so you see the rock); cheap to bulk-paint via SetTilesBlock.
///   • fog        — an opaque overlay cleared within wallPadding tiles of explored space.
///   • collision  — debug-only frontier visual. Movement collision is CODE-BASED (Depenetrate),
///                  because every body in this game is kinematic and kinematic bodies don't collide
///                  with physics colliders. We push the player/enemies out of solid cells each step.
/// Per-cell state lives in a flat CellData[] (no GameObject per tile). Mining is detected by
/// position sampling (NOT collider queries — Physics2D AutoSyncTransforms is OFF and queries read
/// stale geometry in builds).
/// </summary>
public class MineField : MonoBehaviour
{
    public static MineField i;

    [Header("Grid")]
    [Tooltip("World size of one tile.")]
    public float cellSize = 0.5f;

    [Header("Tile art")]
    [Tooltip("Era-0 ore sprites: first 4 = Regular, next 4 = Hard, last 4 = VeryHard. Random pick + rotation per cell.")]
    public Sprite[] d1Tiles;
    [Tooltip("Era-1 ore sprites (same layout as d1Tiles).")]
    public Sprite[] d2Tiles;
    [Tooltip("Era-2 ore sprites (same layout as d1Tiles).")]
    public Sprite[] d3Tiles;

    [Header("Ore in walls (emissive overlay)")]
    [Tooltip("Ore is shown by drawing this overlay (oretileoverlay) ON TOP of an ordinary wall — the wall " +
             "keeps its own hardness look. Auto-loaded from Resources/oretileoverlay if left empty. The overlay " +
             "is rendered per element with the emissive materials Resources/OreMats/Lit{White,Green,Blue,Red}.")]
    public Sprite[] oreOverlaySprites;
    [Tooltip("Sorting order of the ore overlay, relative to the rock (renderSortingOrder). Keep above the " +
             "rock and below the fog.")]
    public int oreOverlaySortingOrder = 1;

    // All wall/ore render tints stay white — the tile art carries the whole look (no code tint).
    public Color regularColor = Color.white;
    public Color hardColor    = Color.white;
    public Color ingotWhiteColor = Color.white;
    public Color ingotGreenColor = Color.white;
    public Color ingotBlueColor  = Color.white;
    public Color ingotRedColor   = Color.white;
    [Tooltip("Fog overlay colour (kept greyscale to stay on-palette).")]
    public Color fogColor = new Color(0.02f, 0.02f, 0.04f, 0.96f);

    [Header("Rendering")]
    public string sortingLayerName = "Default";
    public int renderSortingOrder = 0;
    public int fogSortingOrder = 5;

    // Fog reveal radius is driven by `wallPadding` (see the lighting section) so the revealed area and the
    // lit area always share one edge — there is no separate reveal-radius knob.

    [Header("Floor glow / illumination")]
    [Tooltip("Paint the floor + illuminate the excavated dungeon.")]
    public bool floorGlow = true;
    // The dungeon floor reuses the SAME ground tiles the base uses (Resources/GroundTiles), scaled to the
    // dungeon cell — loaded in EnsureArt, so no per-scene wiring. Same art across every era (d1/d2/d3).
    [Tooltip("Fallback tint if the base ground art is missing (the ground tiles themselves render untinted).")]
    public Color floorColor = new Color(0.55f, 0.36f, 0.22f, 1f);
    public int floorSortingOrder = -1;

    // -------------------------------------------------------------------------------------------------
    //  Dungeon lighting = ONE Sprite Light2D driven by a COOKIE TEXTURE (1 texel per cell) that masks the
    //  excavated shape — explored cells white, everything else transparent. The light samples that texture
    //  per-pixel, so there is NO polygon and NO tessellation: nothing can self-interact, whatever shape you
    //  carve. It's a pure function of which cells you've mined (order/time independent), uniform across the
    //  interior, with a clean ~1-cell soft edge from bilinear filtering.
    //
    //  (History: point lights made it "dance"; a freeform-polygon light fixed that but a concave freeform
    //  mesh self-overlaps its falloff into stray triangles at every concave corner — unfixable. Rasterising
    //  the shape into a cookie sidesteps the tessellator entirely.)
    // -------------------------------------------------------------------------------------------------
    [Tooltip("Colour of the dungeon light (uniform across the whole explored area).")]
    public Color lightColor = Color.white;
    [Tooltip("Brightness of the dungeon light.")]
    public float lightIntensity = 1f;
    [Tooltip("Width (in cells, fractional OK) of the light's falloff into the surrounding walls: the excavated " +
             "cells are full bright, then the mask ramps linearly down to dark over this distance. Also = how far " +
             "the fog reveal reaches. Painted into the cookie, so still no polygon / no artifacts.")]
    public float wallPadding = 2f;
    [Tooltip("Max seconds between the light refitting to the dig (the outline only rebuilds when it changes).")]
    public float lightRebuildInterval = 0.12f;

    // -------------------------------------------------------------------------------------------------
    //  Rock hardness pattern (Dome-Keeper-style): the field is mostly soft rock threaded with Perlin
    //  VEINS of harder stone. A depth term biases everything — near the starter pocket the rock stays
    //  soft; toward the rim the noise blobs cross the Hard threshold and their cores go VeryHard.
    //  Durability scales with the tier (excavateSeconds of drill contact), same tiers the authored pocket walls use.
    // -------------------------------------------------------------------------------------------------
    [Header("Rock hardness pattern")]
    [Tooltip("Perlin feature scale in CELLS — smaller = bigger hard-rock veins/blobs.")]
    public float hardnessNoiseScale = 0.09f;
    [Tooltip("How strongly distance from the entry hardens the rock (adds 0..this to the noise score at the rim).")]
    public float depthHardness = 0.55f;
    [Tooltip("Score (noise*0.65 + depth) above this = Hard rock.")]
    public float hardThreshold = 0.78f;
    [Tooltip("Score above this = VeryHard rock (the cores of the deepest veins).")]
    public float veryHardThreshold = 0.97f;

    // -------------------------------------------------------------------------------------------------
    //  Dungeon boundary — the field ends on a smooth RING, not the square rect. The ring is a radial
    //  envelope R(θ): a base ellipse touching the area's x/y bounds, pushed outward wherever a pocket
    //  extrudes past it (its rect + allowance), then circularly smoothed — i.e. a spline around the
    //  axis extremes and the outer pockets. Cells outside are VOID: invisible, unmineable, but solid,
    //  so the perimeter is organic rather than weirdly-regular square, and radial ore bands reach it.
    // -------------------------------------------------------------------------------------------------
    [Header("Dungeon boundary")]
    [Tooltip("Extra cells of rock kept around a pocket that extrudes past the boundary ring before the " +
             "void begins (the bulge allowance).")]
    public int boundaryAllowance = 5;

    const int BOUNDARY_BINS = 256;   // angular resolution of the boundary envelope
    float[] boundaryR;               // radius (cells) per angular bin

    [Header("Ore clusters (index = era)")]
    [Tooltip("Ore spawns in CLUSTERS, not lone cells: per element, seeds land inside that element's " +
             "distance band (normalized 0=centre..1=edge) and grow into organic blobs whose size scales " +
             "with distance — small pickings near the entry, rich veins at the rim.")]
    public OreEraConfig[] oreEras = { new OreEraConfig(), new OreEraConfig(), new OreEraConfig() };
    [Range(0f, 1f)]
    [Tooltip("How EVENLY each element's clusters spread through its band: 0 = seeds land wherever the " +
             "dice fall (can clump badly), 1 = strongly even spacing (best-candidate/blue-noise " +
             "sampling — each seed picks the spot furthest from its element's existing clusters).")]
    public float oreFairness = 0.4f;

    [Header("Point budget per era (x = min @ difficulty 0, y = max @ difficulty 3)")]
    [Tooltip("The dungeon's point budget — generation keeps adding pockets until this many points are placed " +
             "(there is NO pocket count). Difficulty 0→3 interpolates each Vector2's x→y.")]
    public Vector2 era1Points = new Vector2(100f, 200f);
    public Vector2 era2Points = new Vector2(300f, 600f);
    public Vector2 era3Points = new Vector2(900f, 1800f);

    [Header("Spawner budget per era (x = min @ difficulty 0, y = max @ difficulty 3)")]
    [Tooltip("Points spent placing SPAWNERS: generation samples randomly from the era's authored spawners " +
             "(repeats allowed), spending each pick's points (avg per armed day, forge-overridable) until " +
             "the budget is used up. Difficulty 0→3 interpolates each Vector2's x→y.")]
    public Vector2 era1SpawnerPoints = new Vector2(30f, 60f);
    public Vector2 era2SpawnerPoints = new Vector2(90f, 180f);
    public Vector2 era3SpawnerPoints = new Vector2(270f, 540f);

    [Header("Mining feel")]
    [Tooltip("Seconds of continuous drill CONTACT to excavate a cell, by hardness tier (Regular / Hard / " +
             "VeryHard). No knockback — you hold the drill on the rock until it gives.")]
    public float[] excavateSeconds = { 0.5f, 1f, 2f };

    [Header("Collision (code-based — kinematic bodies ignore physics colliders)")]
    [Tooltip("Player collision radius against the ore.")]
    public float playerRadius = 0.3f;
    [Tooltip("Enemy collision radius against the ore.")]
    public float bodyRadius = 0.25f;

    [Header("Standalone test (Phase 1)")]
    public bool buildOnStart = false;
    public Vector2Int testAreaCells = new Vector2Int(200, 200);
    public Vector2Int testEntryCell = Vector2Int.zero;

    [Header("Debug")]
    [Tooltip("Tint + render the frontier collider tiles so you can SEE exactly where collision exists.")]
    public bool debugShowColliders = true;

    /// <summary>Fires once when the player tunnels into a previously-sealed cavity (a pocket). Cell = a cavity cell.</summary>
    public System.Action<Vector3Int> OnCavityBreached;

    // ----- runtime objects -----
    Grid grid;
    Tilemap renderMap, fogMap, collisionMap, floorMap;
    // One overlay tilemap per ore element (index = orb index: 0 white, 1 green, 2 blue, 3 red), drawn above
    // the rock. Each uses its element's emissive material (LitWhite/Green/Blue/Red) so ore reads as a glowing
    // vein on an otherwise normal wall — the wall keeps its own hardness sprite underneath.
    Tilemap[] oreOverlayMaps;
    static readonly string[] OreMatNames = { "OreMats/LitWhite", "OreMats/LitGreen", "OreMats/LitBlue", "OreMats/LitRed" };
    Transform lightsParent;
    // Every explored EMPTY cell — the excavated dungeon. The light cookie is rasterised from this set,
    // so the illumination is a pure function of shape (order/time independent).
    readonly HashSet<Vector2Int> litCells = new HashSet<Vector2Int>();
    // A single Sprite Light2D masked by a cookie texture (1 texel/cell). No polygon → no self-interaction.
    Light2D maskLight;
    Texture2D maskTex;
    Color32[] maskPx;
    Sprite maskSprite;
    bool lightDirty;      // an explored-empty cell was added since the last refit
    float lightTimer;     // throttles refits to lightRebuildInterval

    // ----- cell data (flat) -----
    CellData[] data;
    int xMin, yMin, w, h;
    // Version-stamped scratch grid for batch dedup (fog dilation, frontier collection, light rings):
    // bumping the version "clears" the whole grid in O(1), so the batch passes never touch a HashSet.
    int[] mark;
    int markVersion;
    // Mirror of the fog map's state (true = fog cleared) + how many fogged cells remain. Batch reveals
    // rewrite fog with ONE SetTilesBlock over the changed rect, and once the map is fully defogged
    // (guaranteed at build when wallPadding covers the map, e.g. 300) every fog pass exits instantly.
    bool[] fogClear;
    int fogRemaining;

    // ----- pockets (hidden until breached) -----
    // Pocket cells stay SOLID ORE (indistinguishable from the surrounding rock) until the player mines
    // into one of them; then the whole pocket opens to empty space and its enemies spawn.
    int[] pocketIdOfCell;                 // -1 = no pocket, else index into pocketCells
    List<List<Vector3Int>> pocketCells;   // per-pocket cell list
    bool[] pocketOpened;
    bool[] pocketRevealed;                // showPockets cheat: geometry already open, still waiting on real excavation to spawn

    // ----- spawner footprints (mineable) -----
    // Mining out ANY ONE cell of a spawner's footprint destroys it — a single chip through the rock
    // kills it. Cells outside any footprint simply aren't in the dictionary.
    readonly Dictionary<int, MineSpawner> spawnerOfCell = new Dictionary<int, MineSpawner>();

    /// <summary>Track a spawner's footprint so mining out any one of its cells destroys it. Every
    /// footprint cell is force-set to VeryHard rock — whatever it replaced — so digging one out is a
    /// real (if slower) alternative to just killing the spawner directly.</summary>
    public void RegisterSpawnerFootprint(BoundsInt rect, MineSpawner ms)
    {
        for (int y = rect.yMin; y < rect.yMax; y++)
            for (int x = rect.xMin; x < rect.xMax; x++)
            {
                var c = new Vector3Int(x, y, 0);
                if (!InBounds(c)) continue;
                int idx = Idx(c);
                data[idx].type = CellType.VeryHard;
                data[idx].durability = DurabilityFor(CellType.VeryHard);
                spawnerOfCell[idx] = ms;
            }
    }

    // ----- runtime tiles -----
    Tile[] renderTiles;         // indexed by (int)CellType — shared tiles for ingots/fog/etc.
    Tile[] regularPool;         // 16 tiles: 4 sprites × 4 rotations, for CellType.Regular
    Tile[] hardPool;            // same for CellType.Hard
    Tile[] veryHardPool;        // same for CellType.VeryHard
    Tile[] oreOverlayPool;      // 16 overlay tiles (sprite × rotation) drawn over any ore-bearing wall
    Tile[] floorPool;           // the base ground tiles (Resources/GroundTiles), scaled to the dungeon cell
    int currentEra;
    Tile fogTile, collisionTile;
    Sprite solidSprite;
    Sprite[] groundSprites;     // base ground art, loaded from Resources/GroundTiles

    static readonly Vector3Int[] N4 =
    {
        new Vector3Int(1, 0, 0), new Vector3Int(-1, 0, 0),
        new Vector3Int(0, 1, 0), new Vector3Int(0, -1, 0),
    };

    void Awake()
    {
        i = this;
    }

    void Start()
    {
        if (buildOnStart)
        {
            var layout = new MineDungeonLayout
            {
                areaCells = new BoundsInt(-testAreaCells.x / 2, -testAreaCells.y / 2, 0, testAreaCells.x, testAreaCells.y, 1),
                entryCell = new Vector3Int(testEntryCell.x, testEntryCell.y, 0),
            };
            Build(layout);
        }
    }

    // =====================================================================================
    //  Code-based collision — kinematic bodies don't collide with physics colliders, so we push
    //  the player + registered enemies out of solid cells each physics step (circle-vs-tile).
    // =====================================================================================

    readonly List<Rigidbody2D> bodies = new List<Rigidbody2D>();

    public void Register(Rigidbody2D rb) { if (rb != null && !bodies.Contains(rb)) bodies.Add(rb); }
    public void Unregister(Rigidbody2D rb) { bodies.Remove(rb); }

    // Grace window during which the player's body counts as "pressed against the ore" — mining is
    // gated on THIS (the body's circle collider), never on any weapon-wall collision.
    const float ORE_CONTACT_GRACE = 0.15f;
    float playerOreContactT;

    /// <summary>Is the player's body currently (within a small grace window) pressed against solid
    /// ore? Melee mining triggers off this — the body invokes mining, weapons never touch walls.</summary>
    public bool PlayerTouchingOre => playerOreContactT > 0f;

    void FixedUpdate()
    {
        if (data == null) return;
        playerOreContactT -= Time.fixedDeltaTime;
        if (GS.AS != null && Depenetrate(GS.AS.rb, playerRadius))
            playerOreContactT = ORE_CONTACT_GRACE;
        for (int k = bodies.Count - 1; k >= 0; k--)
        {
            if (bodies[k] == null) { bodies.RemoveAt(k); continue; }
            Depenetrate(bodies[k], bodyRadius);
        }
    }

    // Returns true when the body actually overlapped solid ore this step (i.e. real wall contact).
    bool Depenetrate(Rigidbody2D rb, float radius)
    {
        if (rb == null) return false;
        Vector2 sep = SeparationAt(rb.position, radius);
        if (sep == Vector2.zero) return false;
        rb.position += sep;       // POSITION correction — can't be overcome by held movement input
        CancelInto(rb, sep);
        return true;
    }

    // Circle-vs-tile push-out for a point of the given radius. Shared by the body collision and the
    // drill-tip hard stop. Returns the separation that lifts the circle out of any solid cells.
    Vector2 SeparationAt(Vector2 pos, float radius)
    {
        Vector3Int c0 = grid.WorldToCell(pos);
        if (!InBounds(c0)) return Vector2.zero; // not in the mine region

        float half = cellSize * 0.5f;
        Vector2 sep = Vector2.zero;

        if (data[Idx(c0)].IsSolid)
            sep += NearestOpenDir(c0) * (radius + half); // safety: centre buried -> shove toward open

        int rng = Mathf.CeilToInt(radius / cellSize) + 1;
        for (int dx = -rng; dx <= rng; dx++)
            for (int dy = -rng; dy <= rng; dy++)
            {
                var cell = new Vector3Int(c0.x + dx, c0.y + dy, 0);
                if (!IsSolid(cell)) continue;
                Vector2 cc = grid.GetCellCenterWorld(cell);
                Vector2 closest = new Vector2(Mathf.Clamp(pos.x, cc.x - half, cc.x + half),
                                              Mathf.Clamp(pos.y, cc.y - half, cc.y + half));
                Vector2 d = pos - closest;
                float dist = d.magnitude;
                if (dist < radius && dist > 0.0001f)
                    sep += d / dist * (radius - dist);
            }
        return sep;
    }

    void CancelInto(Rigidbody2D rb, Vector2 sep)
    {
        Vector2 nrm = sep.normalized;
        Vector2 v = rb.linearVelocity;
        float into = Vector2.Dot(v, -nrm);
        if (into > 0f) rb.linearVelocity = v + nrm * into; // kill into-wall component, keep sliding
    }

    // (No drill-tip hard stop any more: weapons have NO wall collision at all — only the player's
    // body circle collides with the ore, and mining is gated on that contact via PlayerTouchingOre.)

    Vector2 NearestOpenDir(Vector3Int c)
    {
        foreach (var d in N4)
        {
            var nb = c + d;
            if (InBounds(nb) && !data[Idx(nb)].IsSolid) return new Vector2(d.x, d.y);
        }
        return Vector2.up;
    }

    // =====================================================================================
    //  Build
    // =====================================================================================

    public void Build(MineDungeonLayout layout)
    {
        EnsureSetup();

        currentEra = layout.era;

        xMin = layout.areaCells.xMin;
        yMin = layout.areaCells.yMin;
        w = layout.areaCells.size.x;
        h = layout.areaCells.size.y;
        data = new CellData[w * h];
        mark = new int[w * h];
        markVersion = 0;
        fogClear = new bool[w * h];   // PaintAllBlocks fogs everything; cleared cells flip true
        fogRemaining = w * h;

        renderMap.ClearAllTiles();
        if (oreOverlayMaps != null)
            for (int e = 0; e < oreOverlayMaps.Length; e++) oreOverlayMaps[e].ClearAllTiles();
        fogMap.ClearAllTiles();
        collisionMap.ClearAllTiles();
        floorMap.ClearAllTiles();
        ResetDungeonLight();

        // 0. the boundary ring: base ellipse on the area bounds, bulged around extruding pockets.
        ComputeBoundary(layout.pockets);

        // 1. fill everything solid with the PATTERNED hardness (Perlin veins, depth-biased — see the
        //    hardness section). Ore is laid down separately in clusters (step 3b), on top of whatever
        //    hardness a cell rolled, so an ore cell is just a normal wall with ore on it. Cells outside
        //    the boundary ring become VOID (invisible, unmineable, solid).
        float nx = Random.Range(0f, 1000f), ny = Random.Range(0f, 1000f);   // fresh vein layout per build
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int idx = x + y * w;
                if (!InsideBoundary(xMin + x, yMin + y))
                {
                    data[idx].type = CellType.VeryHard;
                    data[idx].durability = ushort.MaxValue;
                    data[idx].voidCell = true;
                    data[idx].explored = false;
                    data[idx].ore = -1;
                    continue;
                }
                CellType t = RollHardnessAt(x, y, nx, ny);
                data[idx].type = t;
                data[idx].durability = DurabilityFor(t);
                data[idx].explored = false;
                data[idx].voidCell = false;
                data[idx].ore = -1;
            }

        // 2. TAG pocket cells but do NOT carve them — a pocket looks like ordinary ore until the player
        //    digs into it (BreakCell -> OpenPocket clears the whole pocket + spawns). The painted space
        //    is the pocket's cells; otherwise the whole rect.
        pocketIdOfCell = new int[w * h];
        for (int k = 0; k < pocketIdOfCell.Length; k++) pocketIdOfCell[k] = -1;
        pocketCells = new List<List<Vector3Int>>();
        pocketOpened = new bool[layout.pockets.Count];
        pocketRevealed = new bool[layout.pockets.Count];
        for (int pi = 0; pi < layout.pockets.Count; pi++)
        {
            var p = layout.pockets[pi];
            p.pocketIndex = pi;
            var cells = new List<Vector3Int>();
            // The cavity (rect minus painted tiles) is the pocket; painted tiles stay solid as obstacles.
            var tmpl = p.template;
            if (tmpl != null)
                foreach (var sc in tmpl.Cells())
                    TagPocketCell(cells, pi, new Vector3Int(p.cellRect.xMin + sc.x, p.cellRect.yMin + sc.y, 0));
            pocketCells.Add(cells);
        }

        // 2b. Stamp authored pocket WALLS as tougher cells (durability rises with hardness). Priority:
        //     a pocket cavity cell is NEVER overwritten (empty wins); where walls overlap, the HARDEST wins.
        //     A wall that would land on a neighbour's cavity is simply skipped — not an error.
        for (int pi = 0; pi < layout.pockets.Count; pi++)
        {
            var p = layout.pockets[pi];
            if (p.template == null) continue;
            foreach (var wt in p.template.WallTiles())
            {
                var cell = new Vector3Int(p.cellRect.xMin + wt.cell.x, p.cellRect.yMin + wt.cell.y, 0);
                if (!InBounds(cell)) continue;
                int idx = Idx(cell);
                if (data[idx].voidCell) continue;                 // beyond the boundary ring — void wins
                if (pocketIdOfCell[idx] != -1) continue;          // a pocket cavity claims it — empty wins
                int hard = PocketTiles.Hardness(wt.type);
                ushort dur = ExcavateMs(hard);
                if (dur > data[idx].durability)                   // tougher than the ore / a softer overlapping wall
                {
                    data[idx].type = hard >= 3 ? CellType.VeryHard : hard >= 2 ? CellType.Hard : CellType.Regular;
                    data[idx].durability = dur;
                }
            }
        }

        // 3. carve the entry cavity (explored after the flood below)
        CarveDisc(layout.entryCell, Mathf.Max(2, layout.entryCavityRadius));

        // 3b. lay the ore down in clusters (after pocket tagging + the entry carve, so blobs never
        //     land on a pocket cavity or the entry)
        ScatterOreClusters();

        // 4. bulk-paint render + fog in one block op each (fast even at 100k+ cells)
        PaintAllBlocks();

        // 5. reveal the entry and build the initial frontier shell
        FloodExplore(layout.entryCell);
        RebuildDungeonLight();     // light the entry cavity immediately (FloodExplore marked it dirty)
    }

    void EnsureSetup()
    {
        if (grid != null) return;

        BuildRuntimeTiles();

        var gridGo = new GameObject("MineGrid");
        gridGo.transform.SetParent(transform, false);
        grid = gridGo.AddComponent<Grid>();
        grid.cellSize = new Vector3(cellSize, cellSize, 0f);

        floorMap = CreateMap("FloorMap", floorSortingOrder, render: true, collide: false);  // under the ore
        // The revealed floor is UNLIT: it renders at full sprite brightness regardless of the 2D point lights,
        // so an excavated area is illuminated the instant it's dug and NEVER dims as you mine elsewhere or the
        // light pool recycles. The warm point lights are now just atmosphere on the walls/player/enemies.
        var floorMat = UnlitFloorMaterial();
        if (floorMat != null) floorMap.GetComponent<TilemapRenderer>().material = floorMat;
        renderMap = CreateMap("RenderMap", renderSortingOrder, render: true, collide: false);

        // One overlay map per element, just above the rock — each carries its element's emissive material
        // (LitWhite/Green/Blue/Red) so the ore glow is the element's colour.
        oreOverlayMaps = new Tilemap[4];
        for (int e = 0; e < 4; e++)
        {
            oreOverlayMaps[e] = CreateMap($"OreOverlay{e}", renderSortingOrder + oreOverlaySortingOrder,
                                          render: true, collide: false);
            var mat = Resources.Load<Material>(OreMatNames[e]);
            if (mat != null) oreOverlayMaps[e].GetComponent<TilemapRenderer>().material = mat;
            else Debug.LogWarning($"[MineField] ore material '{OreMatNames[e]}' not found in Resources.");
        }

        fogMap = CreateMap("FogMap", fogSortingOrder, render: true, collide: false);
        collisionMap = CreateMap("CollisionMap", 0, render: false, collide: true);

        var lp = new GameObject("FloorLights");
        lp.transform.SetParent(grid.transform, false);
        lightsParent = lp.transform;
    }

    // An unlit sprite material so the floor ignores the 2D lights and always shows at full brightness. Cached
    // because every dungeon rebuild re-creates the floor map. Sprite-Unlit-Default is already used elsewhere
    // (VFX), so it survives the build shader strip; falls back to Sprites/Default if it's somehow missing.
    static Material _unlitFloorMat;
    static Material UnlitFloorMaterial()
    {
        if (_unlitFloorMat != null) return _unlitFloorMat;
        var sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ?? Shader.Find("Sprites/Default");
        if (sh != null) _unlitFloorMat = new Material(sh);
        return _unlitFloorMat;
    }

    Tilemap CreateMap(string n, int sortingOrder, bool render, bool collide)
    {
        var go = new GameObject(n);
        go.transform.SetParent(grid.transform, false);
        go.layer = gameObject.layer;

        var map = go.AddComponent<Tilemap>();
        var r = go.AddComponent<TilemapRenderer>();
        r.enabled = render;
        r.sortingOrder = sortingOrder;
        if (!string.IsNullOrEmpty(sortingLayerName)) r.sortingLayerName = sortingLayerName;

        if (collide)
        {
            // No physics collider: every body in this game is KINEMATIC, and kinematic bodies don't
            // collide with static/kinematic colliders. Movement collision is code-based (Depenetrate).
            // This tilemap stays only to visualise the collision frontier when debugging.
            if (debugShowColliders) r.enabled = true;
        }
        return map;
    }

    // Load the dungeon art from Resources when it isn't wired in the Inspector. This keeps the field-art
    // working without scene wiring (editing scene YAML by hand gets clobbered while the editor is open).
    void EnsureArt()
    {
        if (oreOverlaySprites == null || oreOverlaySprites.Length == 0)
        {
            var slices = Resources.LoadAll<Sprite>("oretileoverlay");
            if (slices != null && slices.Length > 0) oreOverlaySprites = slices;
        }
        if (groundSprites == null || groundSprites.Length == 0)
            groundSprites = Resources.LoadAll<Sprite>("GroundTiles");   // the base ground art (reused as-is)
        // Era ore sheets self-load too (Resources/D1Tiles…D3Tiles) so the combined dungeon object needs
        // zero scene wiring; inspector-assigned arrays still win if present.
        if (d1Tiles == null || d1Tiles.Length == 0) d1Tiles = LoadStripNumeric("D1Tiles");
        if (d2Tiles == null || d2Tiles.Length == 0) d2Tiles = LoadStripNumeric("D2Tiles");
        if (d3Tiles == null || d3Tiles.Length == 0) d3Tiles = LoadStripNumeric("D3Tiles");
    }

    // Load a sliced sheet from Resources in NUMERIC slice order (a plain name sort puts _10 before _2).
    static Sprite[] LoadStripNumeric(string res)
    {
        var all = Resources.LoadAll<Sprite>(res);
        var list = new List<(int n, Sprite s)>();
        string prefix = res + "_";
        foreach (var s in all)
            if (s.name.StartsWith(prefix) && int.TryParse(s.name.Substring(prefix.Length), out int n))
                list.Add((n, s));
        list.Sort((a, b) => a.n.CompareTo(b.n));
        var arr = new Sprite[list.Count];
        for (int k = 0; k < list.Count; k++) arr[k] = list[k].s;
        return arr;
    }

    void BuildRuntimeTiles()
    {
        EnsureArt();
        solidSprite = MakeSolidSprite();
        Sprite art = solidSprite;   // generic fallback when a specific sprite/sheet isn't available

        renderTiles = new Tile[8];
        renderTiles[(int)CellType.Empty] = null;
        renderTiles[(int)CellType.Regular] = MakeTile(art, regularColor, Tile.ColliderType.None);
        renderTiles[(int)CellType.Hard] = MakeTile(art, hardColor, Tile.ColliderType.None);
        renderTiles[(int)CellType.IngotWhite] = MakeTile(art, ingotWhiteColor, Tile.ColliderType.None);
        renderTiles[(int)CellType.IngotGreen] = MakeTile(art, ingotGreenColor, Tile.ColliderType.None);
        renderTiles[(int)CellType.IngotBlue] = MakeTile(art, ingotBlueColor, Tile.ColliderType.None);
        renderTiles[(int)CellType.IngotRed] = MakeTile(art, ingotRedColor, Tile.ColliderType.None);
        renderTiles[(int)CellType.VeryHard] = MakeTile(art, hardColor, Tile.ColliderType.None);

        Sprite[] sheet = currentEra == 0 ? d1Tiles : currentEra == 1 ? d2Tiles : d3Tiles;
        regularPool  = BuildOrePool(sheet,  0, regularColor);
        hardPool     = BuildOrePool(sheet,  4, hardColor);
        veryHardPool = BuildOrePool(sheet,  8, hardColor);

        // Ore overlay pool: each oretileoverlay slice × 4 rotations. Drawn white — the per-element emissive
        // material on each overlay map (LitWhite/Green/Blue/Red) supplies the glow colour.
        int variants = (oreOverlaySprites != null && oreOverlaySprites.Length > 0) ? oreOverlaySprites.Length : 1;
        oreOverlayPool = new Tile[variants * 4];
        for (int s = 0; s < variants; s++)
        {
            Sprite sp = (oreOverlaySprites != null && oreOverlaySprites.Length > 0) ? oreOverlaySprites[s] : art;
            for (int r = 0; r < 4; r++)
            {
                var t = ScriptableObject.CreateInstance<Tile>();
                t.sprite = sp;
                t.color = Color.white;
                t.colliderType = Tile.ColliderType.None;
                t.transform = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, 0f, r * 90f), Vector3.one);
                oreOverlayPool[s * 4 + r] = t;
            }
        }

        fogTile = MakeTile(solidSprite, fogColor, Tile.ColliderType.None);
        Color colVis = debugShowColliders ? new Color(1f, 0.25f, 0.25f, 0.4f) : new Color(1f, 1f, 1f, 0f);
        collisionTile = MakeTile(solidSprite, colVis, Tile.ColliderType.Grid);
        // Exposed floor: the SAME ground tiles the base uses (Resources/GroundTiles), rendered untinted.
        // Base tiles are authored at 1u; scale each to the dungeon cell so the art matches base exactly. One
        // Tile per variant, picked at random per cell in PaintAllBlocks. Falls back to the tinted ore art.
        if (groundSprites != null && groundSprites.Length > 0)
        {
            floorPool = new Tile[groundSprites.Length];
            for (int s = 0; s < groundSprites.Length; s++)
            {
                var sp = groundSprites[s];
                float natural = sp.pixelsPerUnit > 0f ? sp.rect.width / sp.pixelsPerUnit : cellSize;  // base tile world size
                float scale = natural > 0.0001f ? cellSize / natural : 1f;                            // shrink to the dungeon cell
                var t = ScriptableObject.CreateInstance<Tile>();
                t.sprite = sp;
                t.color = Color.white;
                t.colliderType = Tile.ColliderType.None;
                t.transform = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
                floorPool[s] = t;
            }
        }
        else
        {
            floorPool = new[] { MakeTile(art, floorColor, Tile.ColliderType.None) };
        }
    }

    // A random floor variant (the base ground tiles), for per-cell variety like the base map.
    Tile RandomFloorTile() => floorPool[floorPool.Length == 1 ? 0 : Random.Range(0, floorPool.Length)];

    // Builds 16 Tile instances (4 sprites × 4 rotations) from a slice of a sprite sheet.
    // Falls back to the shared renderTile for that type if the sheet/sprites are missing.
    Tile[] BuildOrePool(Sprite[] sheet, int offset, Color tint)
    {
        var pool = new Tile[16];
        bool hasSheet = sheet != null && sheet.Length >= offset + 4;
        for (int s = 0; s < 4; s++)
        {
            Sprite sp = hasSheet ? sheet[offset + s] : solidSprite;
            for (int r = 0; r < 4; r++)
            {
                var t = ScriptableObject.CreateInstance<Tile>();
                t.sprite = sp;
                t.color = tint;
                t.colliderType = Tile.ColliderType.None;
                t.transform = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, 0f, r * 90f), Vector3.one);
                pool[s * 4 + r] = t;
            }
        }
        return pool;
    }

    static Tile MakeTile(Sprite s, Color c, Tile.ColliderType ct)
    {
        var t = ScriptableObject.CreateInstance<Tile>();
        t.sprite = s;
        t.color = c;
        t.colliderType = ct;
        return t;
    }

    Sprite MakeSolidSprite()
    {
        var tex = new Texture2D(4, 4) { filterMode = FilterMode.Point };
        var px = new Color32[16];
        for (int k = 0; k < px.Length; k++) px[k] = new Color32(255, 255, 255, 255);
        tex.SetPixels32(px);
        tex.Apply();
        // 4 px / cellSize units => fills exactly one cell
        return Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f / cellSize);
    }

    void PaintAllBlocks()
    {
        var bounds = new BoundsInt(xMin, yMin, 0, w, h, 1);

        var render = new TileBase[w * h];
        var fog = new TileBase[w * h];
        var floor = new TileBase[w * h];
        var overlays = new TileBase[4][];
        for (int e = 0; e < 4; e++) overlays[e] = new TileBase[w * h];
        int voidCount = 0;
        for (int k = 0; k < data.Length; k++)
        {
            if (data[k].voidCell)
            {
                // outside the boundary ring: nothing rendered, nothing fogged — the world just ends
                fogClear[k] = true;
                voidCount++;
                continue;
            }
            // floor only under OPEN cells — SetFloor paints it the moment a wall is mined / a pocket
            // breached. Pre-painting it under every wall left unlit-bright floor edges peeking past the
            // fog at the boundary ring (the jagged outline artifact on the minimap).
            if (!data[k].IsSolid) floor[k] = RandomFloorTile();
            if (data[k].IsSolid)
            {
                // Base wall look = the cell's own hardness (NOT random).
                render[k] = HardnessPool(data[k].type)[Random.Range(0, 16)];
                // Ore is just an emissive overlay laid on that wall — the wall keeps its hardness sprite. It
                // goes on the map for its element so it glows in that element's colour (white/green/blue/red).
                if (data[k].HasOre)
                    overlays[data[k].ore][k] = oreOverlayPool[Random.Range(0, oreOverlayPool.Length)];
            }
            fog[k] = fogTile; // everything starts fogged; FloodExplore clears the entry
        }
        fogRemaining = w * h - voidCount;   // only real cells count toward "fully defogged"
        renderMap.SetTilesBlock(bounds, render);
        for (int e = 0; e < 4; e++) oreOverlayMaps[e].SetTilesBlock(bounds, overlays[e]);
        floorMap.SetTilesBlock(bounds, floor);
        fogMap.SetTilesBlock(bounds, fog);
    }

    // The wall sprite pool for a given hardness.
    Tile[] HardnessPool(CellType ct) =>
        ct == CellType.Hard ? hardPool : ct == CellType.VeryHard ? veryHardPool : regularPool;

    // =====================================================================================
    //  Generation helpers
    // =====================================================================================

    // Build the boundary envelope R(θ): a base ellipse touching the (slightly inset) area bounds,
    // pushed outward around every pocket rect (+ boundaryAllowance), dilated + circularly smoothed so
    // the bulges read as organic spline lobes, then re-maxed with the raw requirement so no pocket can
    // ever poke through the smoothed curve.
    void ComputeBoundary(List<PocketInstance> pockets)
    {
        float a = w * 0.5f - 1f, b = h * 0.5f - 1f;
        var raw = new float[BOUNDARY_BINS];
        for (int k = 0; k < BOUNDARY_BINS; k++)
        {
            float th = k * (Mathf.PI * 2f) / BOUNDARY_BINS;
            float ct = Mathf.Cos(th), st = Mathf.Sin(th);
            raw[k] = a * b / Mathf.Sqrt(b * ct * (b * ct) + a * st * (a * st));   // ellipse radius at θ
        }

        // every pocket pushes the envelope out to its inflated rect's perimeter
        if (pockets != null)
            foreach (var p in pockets)
            {
                if (p == null) continue;
                RectInt r = p.cellRect;
                int x0 = r.xMin - boundaryAllowance, x1 = r.xMax + boundaryAllowance;
                int y0 = r.yMin - boundaryAllowance, y1 = r.yMax + boundaryAllowance;
                for (int x = x0; x <= x1; x++) { PushBoundary(raw, x, y0); PushBoundary(raw, x, y1); }
                for (int y = y0; y <= y1; y++) { PushBoundary(raw, x0, y); PushBoundary(raw, x1, y); }
            }

        // dilate (so bulges keep their crests through the blur), blur circularly, never dip below raw
        var dil = new float[BOUNDARY_BINS];
        for (int k = 0; k < BOUNDARY_BINS; k++)
        {
            float m = 0f;
            for (int o = -2; o <= 2; o++) m = Mathf.Max(m, raw[(k + o + BOUNDARY_BINS) % BOUNDARY_BINS]);
            dil[k] = m;
        }
        boundaryR = new float[BOUNDARY_BINS];
        for (int pass = 0; pass < 2; pass++)
        {
            var src = pass == 0 ? dil : boundaryR;
            var dst = pass == 0 ? boundaryR : dil;
            for (int k = 0; k < BOUNDARY_BINS; k++)
            {
                float s = 0f;
                for (int o = -3; o <= 3; o++) s += src[(k + o + BOUNDARY_BINS) % BOUNDARY_BINS];
                dst[k] = s / 7f;
            }
        }
        for (int k = 0; k < BOUNDARY_BINS; k++) boundaryR[k] = Mathf.Max(dil[k], raw[k]);
    }

    static void PushBoundary(float[] bins, float cx, float cy)
    {
        float d = Mathf.Sqrt(cx * cx + cy * cy);
        int k = Mathf.RoundToInt(Mathf.Atan2(cy, cx) / (Mathf.PI * 2f) * BOUNDARY_BINS);
        k = ((k % BOUNDARY_BINS) + BOUNDARY_BINS) % BOUNDARY_BINS;
        if (d > bins[k]) bins[k] = d;
    }

    // Is a cell (coords relative to the entry at the origin) inside the boundary ring?
    bool InsideBoundary(float cx, float cy)
    {
        if (boundaryR == null) return true;
        float f = Mathf.Atan2(cy, cx) / (Mathf.PI * 2f) * BOUNDARY_BINS;
        int k0 = Mathf.FloorToInt(f);
        float t = f - k0;
        k0 = ((k0 % BOUNDARY_BINS) + BOUNDARY_BINS) % BOUNDARY_BINS;
        float rAt = Mathf.Lerp(boundaryR[k0], boundaryR[(k0 + 1) % BOUNDARY_BINS], t);
        return cx * cx + cy * cy <= rAt * rAt;
    }

    /// <summary>Inside the playable dungeon (in bounds AND not boundary void)? Used e.g. to keep
    /// spawners out of the void ring.</summary>
    public bool IsInsideBoundary(Vector3Int c) => InBounds(c) && !data[Idx(c)].voidCell;

    /// <summary>Plain replaceable WALL cell — solid, in-boundary, ORE-FREE and not a disguised pocket
    /// cavity. Spawner placement: spawners replace regular walls, never ore or pocket space.</summary>
    public bool IsPlainWall(Vector3Int c)
    {
        if (!InBounds(c)) return false;
        int idx = Idx(c);
        return data[idx].IsSolid && !data[idx].voidCell && data[idx].ore < 0 &&
               (pocketIdOfCell == null || pocketIdOfCell[idx] == -1);
    }

    /// <summary>Blank the wall art under a spawner's footprint — the spawner PNG becomes the look of
    /// those walls. CellData is untouched: the cells still block and mine exactly like the regular
    /// walls they replace.</summary>
    public void ClearWallArt(BoundsInt rect)
    {
        for (int y = rect.yMin; y < rect.yMax; y++)
            for (int x = rect.xMin; x < rect.xMax; x++)
            {
                var c = new Vector3Int(x, y, 0);
                renderMap.SetTile(c, null);
                // keep floor visible around the spawner PNG (walls no longer carry pre-painted floor)
                if (floorMap.GetTile(c) == null) floorMap.SetTile(c, RandomFloorTile());
            }
    }

    // The Dome-Keeper-style hardness roll: a Perlin score (two octaves — big veins + rough edges),
    // biased harder with distance from the entry, cut by the two thresholds. Soft rock dominates near
    // the starter pocket; deeper, the noise blobs cross into Hard and their cores into VeryHard.
    CellType RollHardnessAt(int x, int y, float nx, float ny)
    {
        float cx = xMin + x, cy = yMin + y;   // entry cavity is at the origin
        float d = Mathf.Clamp01(Mathf.Sqrt(cx * cx + cy * cy) / (0.5f * Mathf.Min(w, h)));
        float n = Mathf.PerlinNoise(x * hardnessNoiseScale + nx, y * hardnessNoiseScale + ny);
        n = n * 0.85f + 0.15f * Mathf.PerlinNoise(x * hardnessNoiseScale * 3.7f + ny, y * hardnessNoiseScale * 3.7f + nx);
        float score = n * 0.65f + d * depthHardness;
        if (score > veryHardThreshold) return CellType.VeryHard;
        if (score > hardThreshold) return CellType.Hard;
        return CellType.Regular;
    }

    // Durability is CONTACT TIME, stored as milliseconds: how long the drill must grind a cell before
    // it breaks, by hardness tier (excavateSeconds — same tiers authored pocket walls stamp).
    ushort ExcavateMs(int tier)
    {
        float s = (excavateSeconds != null && excavateSeconds.Length > 0)
            ? excavateSeconds[Mathf.Clamp(tier - 1, 0, excavateSeconds.Length - 1)]
            : 0.5f * tier;
        return (ushort)Mathf.Clamp(Mathf.RoundToInt(s * 1000f), 1, 60000);
    }

    ushort DurabilityFor(CellType t)
        => ExcavateMs(t == CellType.VeryHard ? 3 : t == CellType.Hard ? 2 : 1);

    OreEraConfig OreCfg(int era)
        => (oreEras != null && oreEras.Length > 0) ? oreEras[Mathf.Clamp(era, 0, oreEras.Length - 1)] : null;

    // Ore spawns in CLUSTERS: per element, seeds land inside that element's authored distance band
    // (normalized from the centre) and grow into organic blobs whose size scales with the seed's
    // distance — small pickings near the entry, rich veins out at the rim.
    void ScatterOreClusters()
    {
        var cfg = OreCfg(currentEra);
        if (cfg == null) return;
        float radius = 0.5f * Mathf.Min(w, h);
        // Fairness = best-candidate (blue-noise) sampling: each seed considers several valid candidate
        // spots and takes the one FURTHEST from its element's already-placed clusters. oreFairness scales
        // the candidate count — 0 → 1 candidate (pure random, clumps allowed), 1 → 16 (strongly even).
        int candN = 1 + Mathf.RoundToInt(Mathf.Clamp01(oreFairness) * 15f);
        var seeds = new List<Vector2>(24);

        for (int e = 0; e < 4; e++)
        {
            OreElementConfig el = cfg.Element(e);
            if (el == null || el.clusters <= 0) continue;
            Vector2 band = el.range;
            seeds.Clear();
            for (int c = 0; c < el.clusters; c++)
            {
                Vector3Int best = default;
                float bestScore = -1f, bestD = 0f;
                for (int cand = 0; cand < candN; cand++)
                {
                    // rejection-sample one valid candidate inside the band
                    Vector3Int cell = default;
                    float d = 0f;
                    bool ok = false;
                    for (int a = 0; a < 64 && !ok; a++)
                    {
                        int x = Random.Range(0, w), y = Random.Range(0, h);
                        float cx = xMin + x, cy = yMin + y;
                        // clamp to 1 so rock in the boundary bulges (beyond the base ring) counts as rim
                        d = Mathf.Min(1f, Mathf.Sqrt(cx * cx + cy * cy) / radius);
                        if (d < band.x || d > band.y) continue;
                        cell = new Vector3Int(xMin + x, yMin + y, 0);
                        if (!OreableCell(cell)) continue;
                        ok = true;
                    }
                    if (!ok) continue;
                    // score = distance to the nearest existing cluster of this element (bigger = fairer)
                    float score = float.MaxValue;
                    for (int s = 0; s < seeds.Count; s++)
                    {
                        float dx2 = cell.x - seeds[s].x, dy2 = cell.y - seeds[s].y;
                        float sq = dx2 * dx2 + dy2 * dy2;
                        if (sq < score) score = sq;
                    }
                    if (seeds.Count == 0) score = Random.value;   // first seed: nothing to be far from
                    if (score > bestScore) { bestScore = score; best = cell; bestD = d; }
                }
                if (bestScore < 0f) continue;
                seeds.Add(new Vector2(best.x, best.y));
                // size lerps across the ELEMENT'S OWN band: near edge -> x, far edge -> y
                float t = Mathf.InverseLerp(band.x, Mathf.Max(band.x + 0.0001f, band.y), bestD);
                int size = Mathf.Max(1, Mathf.RoundToInt(
                    Mathf.Lerp(el.clusterCells.x, el.clusterCells.y, t) * Random.Range(0.75f, 1.25f)));
                GrowOreBlob(best, (sbyte)e, size);
            }
        }
    }

    // Solid, ore-free, not boundary void, and not a disguised POCKET CAVITY cell — cavity ore would
    // vanish the moment the pocket opens. Pocket WALLS are never cavity-tagged, so blobs may (and do)
    // run over them: that ore survives the pocket opening and stays minable in the pocket's walls.
    bool OreableCell(Vector3Int c)
    {
        if (!InBounds(c)) return false;
        int idx = Idx(c);
        return data[idx].IsSolid && !data[idx].voidCell && data[idx].ore < 0 &&
               (pocketIdOfCell == null || pocketIdOfCell[idx] == -1);
    }

    // Organic blob: grow from the seed by pulling RANDOM cells off the frontier until size is reached.
    void GrowOreBlob(Vector3Int seed, sbyte element, int size)
    {
        var frontier = new List<Vector3Int> { seed };
        var seen = new HashSet<Vector3Int> { seed };
        int placed = 0;
        while (placed < size && frontier.Count > 0)
        {
            int pick = Random.Range(0, frontier.Count);
            Vector3Int c = frontier[pick];
            frontier[pick] = frontier[frontier.Count - 1];
            frontier.RemoveAt(frontier.Count - 1);
            if (!OreableCell(c)) continue;
            data[Idx(c)].ore = element;
            placed++;
            foreach (var dN in N4)
            {
                var n = c + dN;
                if (seen.Add(n)) frontier.Add(n);
            }
        }
    }

    void CarveRect(RectInt rect)
    {
        for (int x = rect.xMin; x < rect.xMax; x++)
            for (int y = rect.yMin; y < rect.yMax; y++)
                CarveCell(new Vector3Int(x, y, 0));
    }

    void CarveDisc(Vector3Int centre, int radius)
    {
        int r2 = radius * radius;
        for (int dx = -radius; dx <= radius; dx++)
            for (int dy = -radius; dy <= radius; dy++)
                if (dx * dx + dy * dy <= r2)
                    CarveCell(new Vector3Int(centre.x + dx, centre.y + dy, 0));
    }

    void CarveCell(Vector3Int c)
    {
        if (!InBounds(c)) return;
        int idx = Idx(c);
        data[idx].type = CellType.Empty;
        data[idx].durability = 0;
        data[idx].explored = false;
        data[idx].voidCell = false;
        data[idx].ore = -1;
    }

    // =====================================================================================
    //  Mining (position sampled — never a collider query)
    // =====================================================================================

    /// <summary>
    /// Grind every exposed solid cell within a cone of <paramref name="range"/> world units and
    /// ±<paramref name="halfAngleDeg"/> of the aim direction, crediting <paramref name="contactSeconds"/>
    /// of drill contact to each (cells break after their tier's excavateSeconds of total contact —
    /// no knockback). Returns true if anything was touched.
    /// </summary>
    public bool TryMineArc(Vector2 origin, Quaternion aimQ, float range, float halfAngleDeg,
                           float contactSeconds, out int cellsChipped, out int cellsBroken)
    {
        cellsChipped = 0;
        cellsBroken = 0;
        if (data == null) return false;

        Vector2 aim = GS.QTV(aimQ);
        if (aim.sqrMagnitude < 0.0001f) return false;
        aim.Normalize();
        float cos = Mathf.Cos(halfAngleDeg * Mathf.Deg2Rad);

        Vector3Int oc = grid.WorldToCell(origin);
        int cr = Mathf.CeilToInt(range / cellSize) + 1;

        // collect first so chipping (which mutates exposure) doesn't change the scan mid-loop
        List<Vector3Int> targets = null;
        for (int dx = -cr; dx <= cr; dx++)
        {
            for (int dy = -cr; dy <= cr; dy++)
            {
                var cell = new Vector3Int(oc.x + dx, oc.y + dy, 0);
                if (!InBounds(cell)) continue;
                if (!data[Idx(cell)].IsSolid || data[Idx(cell)].voidCell) continue;   // void = unmineable
                if (!IsExposed(cell)) continue;

                // Cone-test the point of the cell CLOSEST to the aim ray — not just its centre. Aiming
                // diagonally at the seam/corner between two tiles must grind BOTH: their centres sit
                // outside the cone, but the seam itself lies dead on the ray.
                Vector2 cc = grid.GetCellCenterWorld(cell);
                float half = cellSize * 0.5f;
                float tProj = Mathf.Max(0f, Vector2.Dot(cc - origin, aim));
                Vector2 onRay = origin + aim * tProj;
                Vector2 q = new Vector2(Mathf.Clamp(onRay.x, cc.x - half, cc.x + half),
                                        Mathf.Clamp(onRay.y, cc.y - half, cc.y + half));
                Vector2 to = q - origin;
                float dist = to.magnitude;
                if (dist > range) continue;
                if (dist > 0.001f && Vector2.Dot(to / dist, aim) < cos) continue;

                (targets ??= new List<Vector3Int>()).Add(cell);
            }
        }

        if (targets == null) return false;
        int ms = Mathf.Max(1, Mathf.RoundToInt(contactSeconds * 1000f));
        foreach (var cell in targets)
        {
            cellsChipped++;
            if (ChipCell(cell, ms) > 0) cellsBroken++;
        }
        // NO recoil/knockback — the body's own collision keeps you at the wall face; excavation is
        // purely "hold the drill on the rock for its tier's time".
        return cellsChipped > 0;
    }

    // Orbs granted per broken ore cell, by element index (0 white, 1 green, 2 blue, 3 red).
    static readonly int[] OreYield = { 12, 6, 3, 1 };

    /// <summary>Grind <paramref name="amount"/> MILLISECONDS of contact off a cell's remaining
    /// excavation time; break it (and drop ore) when it reaches 0. Returns 1 if it broke.</summary>
    public int ChipCell(Vector3Int cell, int amount)
    {
        if (!InBounds(cell)) return 0;
        int idx = Idx(cell);
        if (!data[idx].IsSolid || data[idx].voidCell) return 0;   // the boundary void can't be chipped

        data[idx].durability = (ushort)Mathf.Max(0, data[idx].durability - amount);
        SpawnChipFX(cell);

        if (data[idx].durability <= 0)
        {
            BreakCell(cell);
            return 1;
        }
        return 0;
    }

    void BreakCell(Vector3Int cell)
    {
        int idx = Idx(cell);

        int orb = data[idx].ore;   // -1 = no ore; else orb index
        if (orb >= 0)
        {
            // Per-element yield: rarer elements (higher index) drop fewer per ore.
            var drop = new int[4];
            drop[orb] = OreYield[orb];
            GS.CallSpawnOrbs(grid.GetCellCenterWorld(cell), drop);
        }

        data[idx].type = CellType.Empty;
        data[idx].durability = 0;
        data[idx].explored = true;
        data[idx].ore = -1;

        if (spawnerOfCell.TryGetValue(idx, out var minedSpawner))
        {
            spawnerOfCell.Remove(idx);
            if (minedSpawner != null) minedSpawner.MinedOut();
        }

        renderMap.SetTile(cell, null);
        ClearOverlay(cell);
        SetCollision(cell, false);     // mined cell no longer blocks
        ClearFog(cell);
        SetFloor(cell);                // paint floor + mark this cell for the dungeon light outline
        RevealAround(cell);

        // newly-mined cell exposes its solid neighbours
        int borderingRevealedPocket = -1;
        Vector3Int borderingPocketCell = default;
        foreach (var d in N4)
        {
            var n = cell + d;
            if (!InBounds(n)) continue;
            int ni = Idx(n);
            if (data[ni].IsSolid) UpdateCollisionCell(n);
            else if (!data[ni].explored) FloodExplore(n);

            // showPockets cheat: this pocket's own cells are already open (RevealPocketGeometry), so
            // mining a wall bordering it is the "dungeon excavates into the pocket" trigger — not the
            // player's own position (see MineDungeonManager.showPockets / Pocket.RevealNow).
            int npid = pocketIdOfCell[ni];
            if (npid >= 0 && pocketRevealed[npid] && !pocketOpened[npid])
            {
                borderingRevealedPocket = npid;
                borderingPocketCell = n;
            }
        }

        // Did the player just dig INTO a pocket? Open the whole pocket (it was disguised as ore).
        int pid = pocketIdOfCell != null ? pocketIdOfCell[idx] : -1;
        if (pid >= 0 && !pocketOpened[pid]) OpenPocket(pid, cell);
        else if (borderingRevealedPocket >= 0)
        {
            // geometry's already cleared — just flip the flag and fire the same discovery event OpenPocket would.
            pocketOpened[borderingRevealedPocket] = true;
            OnCavityBreached?.Invoke(borderingPocketCell);
        }
    }

    void TagPocketCell(List<Vector3Int> cells, int pi, Vector3Int c)
    {
        if (!InBounds(c)) return;
        pocketIdOfCell[Idx(c)] = pi;
        cells.Add(c);
    }

    // Reveal a pocket the player just breached: clear ALL its cells to empty space at once and fire the
    // discovery event (which spawns the enemies). The cells looked like ordinary ore until this moment.
    void OpenPocket(int pi, Vector3Int breachCell)
    {
        pocketOpened[pi] = true;
        ClearPocketCellsBatch(new List<int> { pi });   // batched: big pockets no longer hitch on breach
        OnCavityBreached?.Invoke(breachCell);   // -> MineDungeonManager -> Pocket.OnDiscover (spawn)
    }

    /// <summary>
    /// Debug/cheat hook (showPockets): clears a pocket's cells to empty space — same visual/geometry
    /// result as a real breach — but deliberately does NOT set <see cref="pocketOpened"/> and does NOT
    /// fire <see cref="OnCavityBreached"/>. So the pocket becomes visible and walkable immediately, while
    /// enemy spawning still waits for real excavation to reach it — <see cref="BreakCell"/> flips
    /// <see cref="pocketOpened"/> and fires the discovery event itself once a bordering wall is mined
    /// (see the <c>pocketRevealed</c> check in its neighbour loop).
    /// </summary>
    public void RevealPocketGeometry(int pi) => RevealPocketsGeometry(new List<int> { pi });

    /// <summary>
    /// Batch form of <see cref="RevealPocketGeometry"/> — reveal MANY pockets in one shot (the
    /// showPockets trigger). All the tilemap work lands in a handful of SetTiles array calls instead
    /// of per-cell SetTile, so revealing every pocket of an era costs well under a millisecond.
    /// </summary>
    public void RevealPocketsGeometry(List<int> pids)
    {
        if (pocketCells == null || pids == null) return;
        List<int> open = null;
        foreach (int pi in pids)
            if (pi >= 0 && pi < pocketCells.Count && !pocketOpened[pi] && !pocketRevealed[pi])
            {
                pocketRevealed[pi] = true;
                (open ??= new List<int>()).Add(pi);
            }
        if (open != null) ClearPocketCellsBatch(open);
    }

    // Clear whole pockets to open space in ONE batched pass: flat CellData writes, then one SetTiles
    // call per tilemap (render / ore overlays / collision / fog / frontier). The old per-cell SetTile +
    // per-cell fog-disc version burned ~20 tilemap calls per cell; this issues 8 calls TOTAL, with the
    // fog dilation and frontier scan deduped by the version-stamped `mark` grid (no HashSets, no
    // re-clearing). Used by real breaches (OpenPocket) and the showPockets cheat alike.
    void ClearPocketCellsBatch(List<int> pids)
    {
        var cells = new List<Vector3Int>(256);
        foreach (int pi in pids)
            foreach (var cell in pocketCells[pi])
                if (InBounds(cell)) cells.Add(cell);
        if (cells.Count == 0) return;

        // flat data writes + light registration — no engine calls in this loop
        foreach (var cell in cells)
        {
            int idx = Idx(cell);
            data[idx].type = CellType.Empty;
            data[idx].durability = 0;
            data[idx].explored = true;
            data[idx].voidCell = false;
            data[idx].ore = -1;
            if (floorGlow) litCells.Add(new Vector2Int(cell.x, cell.y));
        }
        if (floorGlow) lightDirty = true;

        // one batched clear per map
        var arr = cells.ToArray();
        var nulls = new TileBase[arr.Length];
        var floorTiles = new TileBase[arr.Length];
        for (int k = 0; k < arr.Length; k++)
            floorTiles[k] = floorMap.GetTile(arr[k]) ?? RandomFloorTile();
        floorMap.SetTiles(arr, floorTiles);   // cells become open here — give them their floor
        renderMap.SetTiles(arr, nulls);
        if (oreOverlayMaps != null)
            for (int e = 0; e < oreOverlayMaps.Length; e++) oreOverlayMaps[e].SetTiles(arr, nulls);
        collisionMap.SetTiles(arr, nulls);

        // fog: the cavity dilated by the reveal radius (== wallPadding), grown as a RING-BFS on the mark
        // grid — cost scales with the AREA actually touched, never with wallPadding² per cell (a
        // wallPadding like 300 made the old per-cell disc loop iterate 600M+ times ≈ 1.7s). The changed
        // rect is then rewritten with one SetTilesBlock. Skipped outright once the map is fully defogged.
        int fogged = 0;
        if (fogRemaining > 0)
        {
            int ri = Mathf.CeilToInt(Mathf.Max(0f, wallPadding));
            int bx0 = int.MaxValue, by0 = int.MaxValue, bx1 = int.MinValue, by1 = int.MinValue;
            markVersion++;
            var bfs = new List<int>(arr.Length);
            void Take(int px, int py)
            {
                int ni = px + py * w;
                if (mark[ni] == markVersion) return;
                mark[ni] = markVersion;
                bfs.Add(ni);
                if (!fogClear[ni])
                {
                    fogClear[ni] = true;
                    fogRemaining--;
                    fogged++;
                    if (px < bx0) bx0 = px; if (px > bx1) bx1 = px;
                    if (py < by0) by0 = py; if (py > by1) by1 = py;
                }
            }
            foreach (var cell in cells) Take(cell.x - xMin, cell.y - yMin);
            for (int ring = 1; ring <= ri && bfs.Count > 0; ring++)
            {
                var next = new List<int>(bfs.Count);
                var cur = bfs;
                bfs = next;
                foreach (int idx in cur)
                {
                    int px = idx % w, py = idx / w;
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx2 = px + dx, ny2 = py + dy;
                            if (nx2 < 0 || nx2 >= w || ny2 < 0 || ny2 >= h) continue;
                            int ni = nx2 + ny2 * w;
                            if (mark[ni] == markVersion) continue;
                            mark[ni] = markVersion;
                            bfs.Add(ni);
                            if (!fogClear[ni])
                            {
                                fogClear[ni] = true;
                                fogRemaining--;
                                fogged++;
                                if (nx2 < bx0) bx0 = nx2; if (nx2 > bx1) bx1 = nx2;
                                if (ny2 < by0) by0 = ny2; if (ny2 > by1) by1 = ny2;
                            }
                        }
                }
            }
            if (fogged > 0)
            {
                int bw = bx1 - bx0 + 1, bh = by1 - by0 + 1;
                var block = new TileBase[bw * bh];
                for (int py = 0; py < bh; py++)
                    for (int px = 0; px < bw; px++)
                        block[px + py * bw] = fogClear[(bx0 + px) + (by0 + py) * w] ? null : fogTile;
                fogMap.SetTilesBlock(new BoundsInt(xMin + bx0, yMin + by0, 0, bw, bh, 1), block);
            }
        }

        // frontier: solid neighbours of the new cavity become collision frontier, deduped the same way
        markVersion++;
        var frontier = new List<Vector3Int>();
        foreach (var cell in cells)
            foreach (var d in N4)
            {
                var n = cell + d;
                if (!InBounds(n)) continue;
                int ni = Idx(n);
                if (mark[ni] == markVersion) continue;
                mark[ni] = markVersion;
                if (data[ni].IsSolid && HasExploredOpenNeighbour(n)) frontier.Add(n);
            }
        if (frontier.Count > 0)
        {
            var ft = new TileBase[frontier.Count];
            for (int k = 0; k < ft.Length; k++) ft[k] = collisionTile;
            collisionMap.SetTiles(frontier.ToArray(), ft);
        }
    }

    // =====================================================================================
    //  Dungeon lighting — a Freeform Light2D fitted to the OUTLINE of the excavated space.
    //
    //  The old system dropped point lights as you dug (a coarse trail + a light that followed the dig
    //  frontier), so the illumination shifted and popped depending on WHERE and WHEN you mined — it
    //  "danced". This replaces it with a purely SHAPE-driven light: every explored empty cell is tracked
    //  in `litCells`; we trace the boundary of that set into closed polygons and hand each one to a
    //  Freeform Light2D. A freeform light's interior is FLAT (its falloff only extends outward past the
    //  edge), so the entire dug-out area sits at one uniform brightness. The output is a pure function of
    //  the excavated shape — identical no matter the order or timing of how it was mined.
    // =====================================================================================

    // Paints the floor under a cell the moment it becomes open (PaintAllBlocks only pre-paints floor
    // under cells that START open) and registers it as excavated so the light cookie covers it.
    void SetFloor(Vector3Int cell)
    {
        if (floorMap.GetTile(cell) == null) floorMap.SetTile(cell, RandomFloorTile());
        if (!floorGlow) return;
        litCells.Add(new Vector2Int(cell.x, cell.y));   // this cell now belongs to the lit outline
        lightDirty = true;
    }

    void Update()
    {
        if (!lightDirty || !floorGlow) return;
        lightTimer -= Time.deltaTime;
        if (lightTimer > 0f) return;                     // refit at most every lightRebuildInterval
        lightTimer = Mathf.Max(0f, lightRebuildInterval);
        RebuildDungeonLight();
    }

    // Rasterise the excavated shape into the light's cookie texture. Deterministic: depends only on litCells.
    static readonly Color32 DarkTexel = new Color32(0, 0, 0, 0);
    void RebuildDungeonLight()
    {
        lightDirty = false;
        if (lightsParent == null || data == null) return;
        if (!floorGlow || litCells.Count == 0) { if (maskLight != null) maskLight.enabled = false; return; }

        EnsureMaskLight();

        // Paint the mask as a distance ramp: the excavated cells are full bright, then each successive wall
        // ring fades linearly to dark, so the falloff width == wallPadding. One texel per cell — no polygon,
        // so nothing can self-interact whatever the shape. (rgb AND alpha carry the ramp so it works whether
        // the sprite light samples colour or alpha.)
        //
        // Ring 0 = the excavated cells (full bright); rings 1..pad ramp down to 0 at pad+1 (the first
        // unpainted cell), so bilinear filtering fades cleanly to black just past the padding. The BFS
        // runs on FLAT texel indices deduped by the version-stamped mark grid — no HashSets, no boxing —
        // so a full era-3 repaint is a fraction of the old cost.
        for (int k = 0; k < maskPx.Length; k++) maskPx[k] = DarkTexel;
        float pad = Mathf.Max(0f, wallPadding);
        int rings = Mathf.CeilToInt(pad);         // how many wall rings the (possibly fractional) ramp covers

        markVersion++;
        var frontier = new List<int>(litCells.Count);
        var white = new Color32(255, 255, 255, 255);
        foreach (var c in litCells)
        {
            int px = c.x - xMin, py = c.y - yMin;
            if (px < 0 || px >= w || py < 0 || py >= h) continue;
            int idx = px + py * w;
            mark[idx] = markVersion;
            maskPx[idx] = white;
            frontier.Add(idx);
        }
        for (int ring = 1; ring <= rings && frontier.Count > 0; ring++)
        {
            byte b = (byte)Mathf.RoundToInt(Mathf.Clamp01(1f - ring / (pad + 1f)) * 255f);
            var col = new Color32(b, b, b, b);
            var next = new List<int>(frontier.Count);
            foreach (int idx in frontier)
            {
                int px = idx % w, py = idx / w;
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx2 = px + dx, ny2 = py + dy;
                        if (nx2 < 0 || nx2 >= w || ny2 < 0 || ny2 >= h) continue;
                        int ni = nx2 + ny2 * w;
                        if (mark[ni] == markVersion) continue;
                        mark[ni] = markVersion;
                        maskPx[ni] = col;
                        next.Add(ni);
                    }
            }
            frontier = next;
        }

        maskTex.SetPixels32(maskPx);
        maskTex.Apply(false);
        maskLight.enabled = true;
    }

    // Create (or resize) the single Sprite Light2D + its cookie texture. The sprite is placed so one texel
    // maps exactly to one cell, covering the whole dungeon rect.
    void EnsureMaskLight()
    {
        if (maskTex != null && maskTex.width == w && maskTex.height == h && maskLight != null)
        {
            maskLight.color = lightColor;
            maskLight.intensity = lightIntensity;
            return;
        }
        if (maskLight != null) Destroy(maskLight.gameObject);

        maskTex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,   // ~1-cell soft edge between lit/unlit texels, for free
            wrapMode = TextureWrapMode.Clamp,
        };
        maskPx = new Color32[w * h];
        // pixelsPerUnit = 1/cellSize → each texel spans one cell; pivot bottom-left so texel (0,0) sits on the
        // min-cell corner when the light is placed there.
        maskSprite = Sprite.Create(maskTex, new Rect(0, 0, w, h), Vector2.zero, 1f / cellSize);

        var go = new GameObject("DungeonLightMask");
        go.transform.SetParent(lightsParent, false);
        go.transform.position = grid.CellToWorld(new Vector3Int(xMin, yMin, 0));   // world corner of the min cell
        maskLight = go.AddComponent<Light2D>();
        maskLight.lightCookieSprite = maskSprite;                 // set cookie BEFORE the type switch
        maskLight.lightType = Light2D.LightType.Sprite;           // masks the light by the cookie, per-pixel
        maskLight.color = lightColor;
        maskLight.intensity = lightIntensity;
        ApplyAllSortingLayers(maskLight);
    }

    void ResetDungeonLight()
    {
        litCells.Clear();
        lightDirty = false;
        lightTimer = 0f;
        maskLight = null;
        maskTex = null;
        maskPx = null;
        maskSprite = null;
        if (lightsParent == null) return;
        for (int i = lightsParent.childCount - 1; i >= 0; i--) Destroy(lightsParent.GetChild(i).gameObject);
    }


    // Runtime-created Light2Ds default to targeting NO sorting layers (so they'd light nothing). Point them
    // at every sorting layer so they illuminate the floor, walls, player and enemies.
    static int[] _allSortingLayers;
    static void ApplyAllSortingLayers(Light2D light)
    {
        if (_allSortingLayers == null)
        {
            var layers = SortingLayer.layers;
            _allSortingLayers = new int[layers.Length];
            for (int i = 0; i < layers.Length; i++) _allSortingLayers[i] = layers[i].id;
        }
        light.targetSortingLayers = _allSortingLayers;   // public setter in URP 17 (no reflection needed)
    }

    void SpawnChipFX(Vector3Int cell)
    {
        var fx = Resources.Load("ChipFX");
        if (fx == null) return;
        Object.Instantiate(fx, grid.GetCellCenterWorld(cell),
            Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)), transform);
    }

    // =====================================================================================
    //  Exploration, fog, frontier colliders
    // =====================================================================================

    /// <summary>Mark a connected sealed cavity as reached: explore it, reveal it, frontier its walls.</summary>
    void FloodExplore(Vector3Int start)
    {
        if (!InBounds(start)) return;
        int si = Idx(start);
        if (data[si].IsSolid || data[si].explored) return;

        var q = new Queue<Vector3Int>();
        q.Enqueue(start);
        while (q.Count > 0)
        {
            var c = q.Dequeue();
            if (!InBounds(c)) continue;
            int ci = Idx(c);
            if (data[ci].IsSolid || data[ci].explored) continue;

            data[ci].explored = true;
            ClearFog(c);
            SetFloor(c);
            RevealAround(c);

            foreach (var d in N4)
            {
                var n = c + d;
                if (!InBounds(n)) continue;
                int ni = Idx(n);
                if (!data[ni].IsSolid) { if (!data[ni].explored) q.Enqueue(n); }
                else UpdateCollisionCell(n); // wall of the cavity becomes frontier
            }
        }
        // No cavity-breach fired here: pockets are now disguised as solid ore and discovered by mining
        // into them (BreakCell -> OpenPocket), not by flooding into a pre-carved cavity.
    }

    void UpdateCollisionCell(Vector3Int c)
    {
        if (!InBounds(c)) return;
        bool frontier = data[Idx(c)].IsSolid && HasExploredOpenNeighbour(c);
        SetCollision(c, frontier);
    }

    void SetCollision(Vector3Int c, bool on)
    {
        collisionMap.SetTile(c, on ? collisionTile : null);
        // TilemapCollider2D regenerates its geometry automatically when tiles change.
    }

    void ClearFog(Vector3Int c)
    {
        if (!InBounds(c)) return;
        int idx = Idx(c);
        if (fogClear != null && fogClear[idx]) return;   // already clear — skip the tilemap call
        if (fogClear != null) { fogClear[idx] = true; fogRemaining--; }
        fogMap.SetTile(c, null);
    }

    // Clear the ore glow on a cell (only one element map ever has a tile here, but clearing all is cheap).
    void ClearOverlay(Vector3Int c)
    {
        if (oreOverlayMaps == null) return;
        for (int e = 0; e < oreOverlayMaps.Length; e++) oreOverlayMaps[e].SetTile(c, null);
    }

    void RevealAround(Vector3Int c)
    {
        // Fog clears exactly as far as the light reaches — reveal radius IS wallPadding, so the visible
        // (un-fogged) area and the lit area share one edge and one knob.
        if (fogRemaining <= 0) return;   // whole map already defogged (big wallPadding) — nothing to do
        float r = Mathf.Max(0f, wallPadding);
        int ri = Mathf.CeilToInt(r);
        float r2 = r * r;
        for (int dx = -ri; dx <= ri; dx++)
        {
            if (fogRemaining <= 0) return;
            for (int dy = -ri; dy <= ri; dy++)
                if (dx * dx + dy * dy <= r2)
                    ClearFog(new Vector3Int(c.x + dx, c.y + dy, 0));
        }
    }

    // a solid cell is mineable / collides only where it borders space the player has reached
    bool IsExposed(Vector3Int c) => HasExploredOpenNeighbour(c);

    bool HasExploredOpenNeighbour(Vector3Int c)
    {
        foreach (var d in N4)
        {
            var n = c + d;
            if (!InBounds(n)) continue;
            int ni = Idx(n);
            if (!data[ni].IsSolid && data[ni].explored) return true;
        }
        return false;
    }

    // =====================================================================================
    //  Queries / conversions
    // =====================================================================================

    /// <summary>
    /// Bounce a projectile off the ore. Like the player/enemy depenetration, this is CODE-BASED:
    /// kinematic projectiles never raise physics collisions against the tilemap, so ProjectileScript
    /// polls this each FixedUpdate. If <paramref name="pos"/> sits inside a solid cell, reflects the
    /// velocity off the (axis-aligned) wall normal and nudges the projectile back into open space.
    /// </summary>
    // Reflect a projectile that moved from `fromPos` to `toPos` this frame off any solid ore tile its path
    // crossed. The whole segment is sampled (not just the end cell) so fast projectiles can't tunnel through a
    // thin tile wall between physics steps — they bounce like they do off physics "Walls".
    public bool TryReflectProjectile(Vector2 fromPos, Vector2 toPos, Vector2 vel, out Vector2 newDir, out Vector2 correctedPos)
    {
        newDir = vel;
        correctedPos = toPos;
        if (data == null) return false;

        Vector2 seg = toPos - fromPos;
        int steps = Mathf.Max(1, Mathf.CeilToInt(seg.magnitude / (cellSize * 0.5f)));   // ≤ half a cell per sample
        Vector2 lastOpen = fromPos;
        Vector3Int lastOpenCell = grid.WorldToCell(fromPos);
        bool haveOpen = !IsSolid(lastOpenCell);
        for (int s = 0; s <= steps; s++)
        {
            Vector2 sample = fromPos + seg * ((float)s / steps);
            Vector3Int c = grid.WorldToCell(sample);
            if (!IsSolid(c)) { lastOpen = sample; lastOpenCell = c; haveOpen = true; continue; }

            Vector2 v = vel.sqrMagnitude > 0.0001f ? vel : (toPos - fromPos);
            // TRUE face normal: from the exact open-cell -> solid-cell crossing when we have it,
            // else the old velocity-sign guess (projectile began inside the rock).
            Vector2 n = haveOpen ? FaceNormal(lastOpen, sample, lastOpenCell, c, v) : WallNormal(c, v);
            // Stop just outside the wall: the last open spot we passed, or — if we somehow began inside
            // the ore — shoved into the open neighbour cell.
            correctedPos = haveOpen ? lastOpen : ((Vector2)grid.GetCellCenterWorld(c) + n * cellSize);
            newDir = Vector2.Reflect(v, n).normalized;     // angle of incidence = angle of reflection
            // Inner corners can reflect straight into MORE rock (grinding along the wall) — if the
            // bounce is still aimed at a solid cell right ahead, send it dead back instead.
            if (IsSolidWorld(correctedPos + newDir * (cellSize * 0.6f)))
                newDir = -v.normalized;
            return true;
        }
        return false;
    }

    // The face of solid cell B the segment openPos->hitPos (coming from open cell A) actually pierced:
    // straight neighbour = that shared face; diagonal step = whichever axis boundary the segment crossed
    // LAST on its way into B (a perfect corner hit returns the diagonal, bouncing the shot back on itself).
    Vector2 FaceNormal(Vector2 openPos, Vector2 hitPos, Vector3Int A, Vector3Int B, Vector2 v)
    {
        int dxc = B.x - A.x, dyc = B.y - A.y;
        if (dxc == 0 && dyc == 0) return WallNormal(B, v);                    // degenerate
        if (dxc != 0 && dyc == 0) return new Vector2(-Mathf.Sign(dxc), 0f);   // crossed a vertical face
        if (dyc != 0 && dxc == 0) return new Vector2(0f, -Mathf.Sign(dyc));   // crossed a horizontal face

        // diagonal: compare the crossing parameters of the two boundaries between A and B
        Vector2 d = hitPos - openPos;
        float bx = grid.CellToWorld(new Vector3Int(Mathf.Max(A.x, B.x), 0, 0)).x;   // shared vertical boundary
        float by = grid.CellToWorld(new Vector3Int(0, Mathf.Max(A.y, B.y), 0)).y;   // shared horizontal boundary
        float tx = Mathf.Abs(d.x) > 1e-6f ? (bx - openPos.x) / d.x : float.NegativeInfinity;
        float ty = Mathf.Abs(d.y) > 1e-6f ? (by - openPos.y) / d.y : float.NegativeInfinity;
        if (Mathf.Abs(tx - ty) < 1e-5f)
            return new Vector2(-Mathf.Sign(dxc), -Mathf.Sign(dyc)).normalized;      // clean corner hit
        return tx > ty ? new Vector2(-Mathf.Sign(dxc), 0f) : new Vector2(0f, -Mathf.Sign(dyc));
    }

    // Surface normal of the solid cell `c` for a body arriving with velocity `vel`. The struck face is the one
    // OPPOSITE the body's motion that has an OPEN cell behind it: moving +x means it crossed the cell's LEFT
    // face (normal -x), +y the BOTTOM face (normal -y); a corner hit returns both. This is what makes the
    // bounce obey angle-in = angle-out on axis-aligned tile walls, instead of NearestOpenDir's arbitrary guess.
    Vector2 WallNormal(Vector3Int c, Vector2 vel)
    {
        Vector2 n = Vector2.zero;
        int sx = vel.x > 0f ? -1 : (vel.x < 0f ? 1 : 0);   // back-face direction on the x axis
        int sy = vel.y > 0f ? -1 : (vel.y < 0f ? 1 : 0);   // back-face direction on the y axis
        if (sx != 0 && !IsSolid(new Vector3Int(c.x + sx, c.y, 0))) n.x = sx;   // open behind the x face → it's the hit
        if (sy != 0 && !IsSolid(new Vector3Int(c.x, c.y + sy, 0))) n.y = sy;   // open behind the y face → it's the hit
        if (n == Vector2.zero) n = NearestOpenDir(c);      // fallback (buried, or both back-faces solid)
        return n.normalized;
    }

    public bool IsSolid(Vector3Int c) => InBounds(c) && data[Idx(c)].IsSolid;

    /// <summary>Is this cell part of the excavated dungeon (open AND explored)?</summary>
    public bool IsExcavated(Vector3Int c) => InBounds(c) && !data[Idx(c)].IsSolid && data[Idx(c)].explored;

    /// <summary>
    /// Any excavated cell within <paramref name="range"/> world units of a point? (Spawner activation
    /// test — a bounded square scan, cheap at the ranges spawners use.)
    /// </summary>
    public bool AnyExcavatedWithin(Vector2 world, float range)
    {
        if (data == null) return false;
        Vector3Int c0 = grid.WorldToCell(world);
        int cr = Mathf.CeilToInt(range / cellSize);
        float r2 = range * range;
        for (int dx = -cr; dx <= cr; dx++)
            for (int dy = -cr; dy <= cr; dy++)
            {
                var c = new Vector3Int(c0.x + dx, c0.y + dy, 0);
                if (!IsExcavated(c)) continue;
                if (((Vector2)grid.GetCellCenterWorld(c) - world).sqrMagnitude <= r2) return true;
            }
        return false;
    }

    /// <summary>
    /// A random excavated cell-centre near a point, biased to the CLOSEST edge of the dig (where a
    /// spawner's enemies emerge). Null when no excavated cell is within range.
    /// </summary>
    public Vector2? RandomExcavatedNear(Vector2 world, float range)
    {
        if (data == null) return null;
        Vector3Int c0 = grid.WorldToCell(world);
        int cr = Mathf.CeilToInt(range / cellSize);
        float r2 = range * range;
        var candidates = new List<(float d2, Vector2 pos)>();
        for (int dx = -cr; dx <= cr; dx++)
            for (int dy = -cr; dy <= cr; dy++)
            {
                var c = new Vector3Int(c0.x + dx, c0.y + dy, 0);
                if (!IsExcavated(c)) continue;
                Vector2 p = grid.GetCellCenterWorld(c);
                float d2 = (p - world).sqrMagnitude;
                if (d2 <= r2) candidates.Add((d2, p));
            }
        if (candidates.Count == 0) return null;
        candidates.Sort((a, b) => a.d2.CompareTo(b.d2));
        int take = Mathf.Max(1, Mathf.Min(8, candidates.Count / 4));   // among the nearest few
        return candidates[Random.Range(0, take)].pos;
    }
    /// <summary>
    /// A random excavated cell-centre within range that HUGS A WALL (≥1 solid 4-neighbour) — where a
    /// spawner's enemies emerge. Uniform across every wall-hugging floor cell in range; falls back to
    /// <see cref="RandomExcavatedNear"/> if the dig in range has no wall-adjacent cell.
    /// </summary>
    public Vector2? RandomExcavatedWallHugNear(Vector2 world, float range)
    {
        if (data == null) return null;
        Vector3Int c0 = grid.WorldToCell(world);
        int cr = Mathf.CeilToInt(range / cellSize);
        float r2 = range * range;
        var candidates = new List<Vector2>();
        for (int dx = -cr; dx <= cr; dx++)
            for (int dy = -cr; dy <= cr; dy++)
            {
                var c = new Vector3Int(c0.x + dx, c0.y + dy, 0);
                if (!IsExcavated(c)) continue;
                Vector2 p = grid.GetCellCenterWorld(c);
                if ((p - world).sqrMagnitude > r2) continue;
                bool hugsWall = false;
                foreach (var d in N4)
                    if (IsSolid(c + d)) { hugsWall = true; break; }
                if (hugsWall) candidates.Add(p);
            }
        if (candidates.Count == 0) return RandomExcavatedNear(world, range);
        return candidates[Random.Range(0, candidates.Count)];
    }

    public bool IsSolidWorld(Vector2 world) => IsSolid(grid.WorldToCell(world));
    /// <summary>Has a dungeon actually been generated into this field yet?</summary>
    public bool Built => data != null;
    /// <summary>The current dungeon's cell-coordinate rectangle (for pathfinding array sizing).</summary>
    public RectInt CellRect => new RectInt(xMin, yMin, w, h);
    public Vector3Int WorldToCell(Vector2 world) => grid.WorldToCell(world);
    public Vector3 CellCenterWorld(Vector3Int c) => grid.GetCellCenterWorld(c);
    public float CellSize => cellSize;

    /// <summary>Exact world-space extent of the current dungeon's cell area — for framing a camera to it.</summary>
    public Bounds WorldBounds
    {
        get
        {
            Vector3 min = grid.CellToWorld(new Vector3Int(xMin, yMin, 0));
            Vector3 max = grid.CellToWorld(new Vector3Int(xMin + w, yMin + h, 0));
            Vector3 center = (min + max) * 0.5f;
            Vector3 size = max - min;
            return new Bounds(center, size);
        }
    }

    /// <summary>The (min @ difficulty 0, max @ difficulty 3) point-budget range for a (0-based) era.</summary>
    public Vector2 PointsRange(int era)
    {
        switch (era)
        {
            case 0:  return era1Points;
            case 1:  return era2Points;
            default: return era3Points;
        }
    }

    /// <summary>The dungeon's point budget for an era — generation keeps adding pockets until this many
    /// points are placed. Interpolates the era's <see cref="PointsRange"/> by difficulty (0→3).</summary>
    public float PointsForEra(int era, float difficulty)
    {
        var r = PointsRange(era);
        return Mathf.Lerp(r.x, r.y, Mathf.Clamp01(difficulty / 3f));
    }

    /// <summary>The (min @ difficulty 0, max @ difficulty 3) spawner-budget range for a (0-based) era.</summary>
    public Vector2 SpawnerPointsRange(int era)
    {
        switch (era)
        {
            case 0:  return era1SpawnerPoints;
            case 1:  return era2SpawnerPoints;
            default: return era3SpawnerPoints;
        }
    }

    /// <summary>The dungeon's spawner budget for an era — generation keeps sampling spawners until this
    /// many points are placed. Interpolates the era's <see cref="SpawnerPointsRange"/> by difficulty (0→3).</summary>
    public float SpawnerPointsForEra(int era, float difficulty)
    {
        var r = SpawnerPointsRange(era);
        return Mathf.Lerp(r.x, r.y, Mathf.Clamp01(difficulty / 3f));
    }

    int Idx(Vector3Int c) => (c.x - xMin) + (c.y - yMin) * w;
    bool InBounds(Vector3Int c) => data != null && c.x >= xMin && c.x < xMin + w && c.y >= yMin && c.y < yMin + h;
}

/// <summary>
/// One element's cluster tuning within one era: where its clusters seed (normalized distance band
/// from the dungeon centre, 0 = centre, 1 = edge), how many, and how big (size lerps across ITS band
/// from x at the band's near edge to y at its far edge — deeper veins run richer).
/// </summary>
[System.Serializable]
public class OreElementConfig
{
    public Vector2 range = new Vector2(0f, 1f);
    [Tooltip("Clusters seeded for this element in this era's dungeon.")]
    public int clusters = 12;
    [Tooltip("Cluster size in CELLS at the near edge of the band (x), growing to its far edge (y).")]
    public Vector2 clusterCells = new Vector2(3f, 14f);

    public OreElementConfig() { }
    public OreElementConfig(Vector2 rangeP, int clustersP, Vector2 cellsP)
    { range = rangeP; clusters = clustersP; clusterCells = cellsP; }
}

/// <summary>
/// Per-era ore-cluster tuning (lives on MineField in the scene — NOT in the authoring asset). Every
/// element carries its OWN band / cluster count / cluster size per dungeon.
/// </summary>
[System.Serializable]
public class OreEraConfig
{
    public OreElementConfig white = new OreElementConfig(new Vector2(0f, 0.55f), 14, new Vector2(2f, 8f));
    public OreElementConfig green = new OreElementConfig(new Vector2(0.15f, 0.75f), 12, new Vector2(3f, 10f));
    public OreElementConfig blue  = new OreElementConfig(new Vector2(0.45f, 1f), 10, new Vector2(4f, 12f));
    public OreElementConfig red   = new OreElementConfig(new Vector2(0.7f, 1f), 8, new Vector2(5f, 14f));

    /// <summary>Config for an orb element (0 white, 1 green, 2 blue, 3 red).</summary>
    public OreElementConfig Element(int e) => e == 0 ? white : e == 1 ? green : e == 2 ? blue : red;
}
