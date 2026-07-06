using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Authored mining content (the analogue of WaveAuthoringSO). Edited by Tools > Mine Forge,
/// saved to Assets/Resources/MineAuthoring.asset, Resources.Load-ed by MineDungeonManager.
/// Per era it holds a LIBRARY OF POCKET TEMPLATES plus the generation/ore settings the runtime
/// generator uses. Pure data — no editor dependencies.
/// </summary>
[CreateAssetMenu(fileName = "MineAuthoring", menuName = "ScriptableObjects/Mine Authoring", order = 1)]
public class MineAuthoringSO : ScriptableObject
{
    public const int ERAS = 3;

    [Header("Enemy palettes (per era) — fallback only")]
    [Tooltip("Normally the brush palette is shared from Wave Forge (WaveAuthoring) so the two forges " +
             "stay on the same per-era enemy roster. These arrays are only used if WaveAuthoring is missing.")]
    public MarauderSO[] era1Enemies;
    public MarauderSO[] era2Enemies;
    public MarauderSO[] era3Enemies;

    [Header("Other placeable objects (props, chests, obstacles…) — per era, like the enemy palette")]
    public GameObject[] era1Objects;
    public GameObject[] era2Objects;
    public GameObject[] era3Objects;

    [Header("Status floor-tile prefabs — painted in the Tiles brush, spawned at their cells on discovery")]
    [Tooltip("Indexed Stun, Slow, Speed, Root, Launch (PocketTileType order). Assigned by Build Era-1 Mine Tiles.")]
    public GameObject[] statusTilePrefabs;

    /// <summary>The FloorTile prefab for a painted status tile type (null if unmapped).</summary>
    public GameObject StatusTilePrefab(PocketTileType t)
    {
        int i = (int)t - (int)PocketTileType.Stun;   // Stun=0 … Launch=4
        return statusTilePrefabs != null && i >= 0 && i < statusTilePrefabs.Length ? statusTilePrefabs[i] : null;
    }

    [Header("Per-era mines")]
    public List<EraMine> eras = new List<EraMine>();

    /// <summary>
    /// Per-era enemy brush palette. Shared from Wave Forge (era N == dungeon N) so the mining and wave
    /// content never drift onto different enemy rosters; falls back to the local arrays only when the
    /// WaveAuthoring asset can't be found.
    /// </summary>
    public MarauderSO[] Palette(int era)
    {
        var wave = Resources.Load<WaveAuthoringSO>("WaveAuthoring");
        if (wave != null)
        {
            var shared = wave.Palette(era);
            if (shared != null && shared.Length > 0) return shared;
        }
        switch (era)
        {
            case 0: return era1Enemies;
            case 1: return era2Enemies;
            default: return era3Enemies;
        }
    }

    /// <summary>
    /// Per-era "other objects" brush palette (props/obstacles/extras) for the Mine Forge, mirroring the
    /// per-era enemy <see cref="Palette"/>. Unlike enemies these are mine-specific, so there's no Wave
    /// Forge sharing — each era keeps its own list.
    /// </summary>
    public GameObject[] ObjectsPalette(int era)
    {
        switch (Mathf.Clamp(era, 0, ERAS - 1))
        {
            case 0: return era1Objects;
            case 1: return era2Objects;
            default: return era3Objects;
        }
    }

    /// <summary>Assign the per-era object palette (used by the builders and the palette filler).</summary>
    public void SetObjectsPalette(int era, GameObject[] palette)
    {
        switch (Mathf.Clamp(era, 0, ERAS - 1))
        {
            case 0: era1Objects = palette; break;
            case 1: era2Objects = palette; break;
            default: era3Objects = palette; break;
        }
    }

    /// <summary>Self-seeding accessor (cf. WaveAuthoringSO.GetDungeon): grows the list and guarantees a boss pocket.</summary>
    public EraMine GetEra(int era)
    {
        while (eras.Count <= era) eras.Add(new EraMine());
        eras[era].EnsureBossPocket();
        return eras[era];
    }

    // ----- override-aware point helpers (cf. WaveAuthoringSO.EnemyPoints / DayAutoPoints) -----
    public static float EnemyPoints(MarauderSO so) => so != null ? so.price : 0f;

    public static float PocketAutoPoints(PocketTemplate p)
    {
        float s = 0f;
        if (p != null)
            foreach (var e in p.AllPlaced())
                if (e.enemy != null) s += EnemyPoints(e.enemy);
        return s;
    }

    public static float PocketPoints(PocketTemplate p)
        => (p != null && p.costOverridden) ? p.costOverride : PocketAutoPoints(p);

    /// <summary>A spawner's auto cost = its AVERAGE enemy points per armed day (days with no enemies don't count).</summary>
    public static float SpawnerAutoPoints(MineSpawnerTemplate st)
    {
        if (st == null || st.days == null) return 0f;
        float sum = 0f; int armed = 0;
        foreach (var d in st.days)
        {
            if (d == null || !d.HasAnyEnemy()) continue;
            armed++;
            foreach (var c in d.cells) if (c.enemy != null) sum += EnemyPoints(c.enemy);
        }
        return armed > 0 ? sum / armed : 0f;
    }

    public static float SpawnerPoints(MineSpawnerTemplate st)
        => (st != null && st.costOverridden) ? st.costOverride : SpawnerAutoPoints(st);
}

[System.Serializable]
public class EraMine
{
    public List<PocketTemplate> pockets = new List<PocketTemplate>();

    [Header("Spawners")]
    [Tooltip("Enemy spawn zones scattered in the rock. Each renews daily at an unpredictable time and " +
             "spawns its authored day-plan into the nearby excavation once the dig comes within range.")]
    public List<MineSpawnerTemplate> spawners = new List<MineSpawnerTemplate>();

    [Header("Combi-pockets")]
    [Tooltip("Add-on pockets rolled onto host pockets that carry a combi-tile — they grow outward from " +
             "that tile to make the host bigger/harder. Authored like normal pockets (space + waves).")]
    public List<PocketTemplate> combiPockets = new List<PocketTemplate>();

    // NOTE: generation parameters (dungeon area, ore, hardness) intentionally do NOT live here —
    // the authoring asset is Mine-Forge CONTENT only. Tuning lives in the scene, on the combined
    // MineDungeonManager object (dungeon areas) and its MineField component (ore clusters, hardness).

    public void EnsureBossPocket()
    {
        foreach (var p in pockets) if (p.isBoss) return;
        pockets.Add(new PocketTemplate { name = "Boss", isBoss = true, width = 12, height = 12 });
    }

    public PocketTemplate Boss()
    {
        foreach (var p in pockets) if (p.isBoss) return p;
        return null;
    }
}

/// <summary>
/// What a painted pocket cell is. Empty (the default for any unpainted cell) is open cavity. Wall/HardWall/
/// VeryHardWall are SOLID obstacles of rising drill-durability. The status types (Stun…Launch) are OPEN
/// cavity cells that ALSO carry a permanent floor-effect entity. (Heal/Orb are NOT tiles — they're placed
/// as Objects/extras, since they're one-shot pickups rather than persistent flooring.)
/// </summary>
public enum PocketTileType : byte
{
    Empty = 0,
    Wall = 1, HardWall = 2, VeryHardWall = 3,
    Stun = 10, Slow = 11, Speed = 12, Root = 13, Launch = 14,
}

[System.Serializable]
public struct PocketTile
{
    public Vector2Int cell;
    public PocketTileType type;
    public PocketTile(Vector2Int c, PocketTileType t) { cell = c; type = t; }
}

public static class PocketTiles
{
    public static bool IsWall(PocketTileType t) =>
        t == PocketTileType.Wall || t == PocketTileType.HardWall || t == PocketTileType.VeryHardWall;
    public static bool IsStatus(PocketTileType t) => (int)t >= 10;
    /// <summary>Wall drill-durability tier: Wall=1, HardWall=2, VeryHardWall=3 (0 for non-walls).</summary>
    public static int Hardness(PocketTileType t) =>
        t == PocketTileType.Wall ? 1 : t == PocketTileType.HardWall ? 2 : t == PocketTileType.VeryHardWall ? 3 : 0;
}

[System.Serializable]
public class PocketTemplate
{
    public string name = "Pocket";
    public bool enabled = true;
    [Tooltip("Bounding size of the pocket's authoring canvas, in cells.")]
    public int width = 8;
    public int height = 8;
    [Tooltip("Exactly one per era — contains the boss + a core, advances the era when cleared.")]
    public bool isBoss = false;
    public CorePlacement core = new CorePlacement();
    /// <summary>A core pocket = the boss pocket, or any pocket you've placed a Core marker into. No separate
    /// toggle: dropping a Core in the pocket is what makes it a confined wave-arena that grants the core.</summary>
    public bool hasCore => isBoss || core.present;
    [Tooltip("Random weight range. Small weight => placed near the entry, large => toward the edge.")]
    public Vector2 weightRange = new Vector2(1f, 3f);
    [Tooltip("EMBER sent home when this pocket is fully harvested with the tether intact (1 for every pocket by default).")]
    public int ember = 1;

    /// <summary>Effective ember payout. Pockets serialized before `ember` existed read back as 0 —
    /// treat that as the default 1 (same legacy pattern as PlacedObject.SpawnChance).</summary>
    public int EmberValue => ember <= 0 ? 1 : ember;
    public bool costOverridden = false;
    public float costOverride = 0f;
    [Tooltip("Painted cells: Wall/HardWall/VeryHardWall are SOLID obstacles (rising durability); Stun/Slow/" +
             "Speed/Root/Launch are OPEN cavity cells that spawn a permanent floor-effect tile. Unpainted = " +
             "Empty cavity. The carved cavity (where enemies/extras spawn) is the rect MINUS the solid walls.")]
    public List<PocketTile> tiles = new List<PocketTile>();
    [Tooltip("PER-POCKET extra objects (props/obstacles). Spawn INSTANTLY when the pocket is discovered " +
             "(not per wave).")]
    public List<PlacedObject> extras = new List<PlacedObject>();
    [Tooltip("PER-POCKET combi-tiles — perimeter cells where add-on combi-pockets may attach + grow " +
             "outward (place at most one per direction).")]
    public List<Vector2Int> combiTiles = new List<Vector2Int>();
    [Tooltip("Each wave holds its own free-form ENEMY placements (later waves can reuse the same spots).")]
    public List<PocketWave> waves = new List<PocketWave>();

    bool InRect(Vector2Int c) => c.x >= 0 && c.x < width && c.y >= 0 && c.y < height;

    /// <summary>The painted tile type at a cell (Empty if unpainted).</summary>
    public PocketTileType TileAt(Vector2Int c)
    {
        if (tiles != null)
            for (int i = 0; i < tiles.Count; i++)
                if (tiles[i].cell == c) return tiles[i].type;
        return PocketTileType.Empty;
    }

    /// <summary>Is this a SOLID wall cell (obstacle)? Status tiles are open cavity, not solid.</summary>
    public bool IsTile(Vector2Int c) => PocketTiles.IsWall(TileAt(c));

    /// <summary>Wall durability tier at a cell (1/2/3 for Wall/HardWall/VeryHardWall, 0 otherwise).</summary>
    public int HardnessAt(Vector2Int c) => PocketTiles.Hardness(TileAt(c));

    /// <summary>Can something spawn / be placed here? Inside the rect and NOT a solid wall.</summary>
    public bool InSpace(Vector2Int c) => InRect(c) && !IsTile(c);

    public bool InSpace(Vector2 cellPos)
        => InSpace(new Vector2Int(Mathf.FloorToInt(cellPos.x), Mathf.FloorToInt(cellPos.y)));

    /// <summary>Runtime-merged templates (root + combis) record their EXACT open cells here: an L-shaped
    /// merge's bounding rect contains plain rock that must NOT count as cavity. Never serialized —
    /// authored templates leave it null and fall back to rect-minus-walls.</summary>
    [System.NonSerialized] public HashSet<Vector2Int> runtimeCavity;

    /// <summary>The carved cavity = every rect cell that ISN'T a solid wall (Empty + status tiles) —
    /// or the exact merged cell set when this is a runtime-merged template.</summary>
    public IEnumerable<Vector2Int> Cells()
    {
        if (runtimeCavity != null)
        {
            foreach (var c in runtimeCavity) yield return c;
            yield break;
        }
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                var c = new Vector2Int(x, y);
                if (!IsTile(c)) yield return c;
            }
    }

    /// <summary>Painted status tiles (open cavity cells carrying a permanent floor-effect entity).</summary>
    public IEnumerable<PocketTile> StatusTiles()
    {
        if (tiles != null)
            for (int i = 0; i < tiles.Count; i++)
                if (PocketTiles.IsStatus(tiles[i].type)) yield return tiles[i];
    }

    /// <summary>Painted SOLID walls (incl. multi-thickness borders, which can sit outside [0,width)×[0,height)).
    /// The generator stamps these as tougher cells around the cavity (empty cavity wins; hardest wall wins).</summary>
    public IEnumerable<PocketTile> WallTiles()
    {
        if (tiles != null)
            for (int i = 0; i < tiles.Count; i++)
                if (PocketTiles.IsWall(tiles[i].type)) yield return tiles[i];
    }

    /// <summary>Every wave enemy (for point/score sums and SO sampling). Extras have no point cost.</summary>
    public IEnumerable<PlacedObject> AllPlaced()
    {
        if (waves != null)
            foreach (var w in waves)
                if (w?.placed != null)
                    foreach (var po in w.placed)
                        yield return po;
    }

    /// <summary>Guarantee at least one wave (so there's always somewhere to place enemies).</summary>
    public PocketWave EnsureWave()
    {
        if (waves == null) waves = new List<PocketWave>();
        if (waves.Count == 0) waves.Add(new PocketWave());
        return waves[0];
    }
}

[System.Serializable]
public struct PlacedObject
{
    public MarauderSO enemy;   // if set => spawn this enemy
    public GameObject prefab;  // else => instantiate this generic object
    public Vector2 pos;        // FREE-FORM position in cell units within the pocket [0..width]×[0..height]
    [Range(0f, 1f)] public float chance;  // per-placement spawn probability (the ONLY chance source — nothing self-scatters)

    /// <summary>Effective spawn probability. Legacy placements (serialized before `chance` existed) read
    /// back as 0 — treat that as "always" so they keep spawning; new paints always write an explicit value.</summary>
    public float SpawnChance => chance <= 0f ? 1f : chance;
}

[System.Serializable]
public class PocketWave
{
    [Tooltip("Seconds over which this wave's enemies trickle in.")]
    public float spawnDuration = 3f;
    [Tooltip("Seconds to wait before the next wave begins.")]
    public float delayAfter = 2f;
    [Tooltip("Only start the next wave once every enemy from this one is dead.")]
    public bool waitForClear = true;
    [Tooltip("Spawn this wave's placements in a random order rather than top-to-bottom list order.")]
    public bool randomOrder = true;
    [Tooltip("This wave's enemies/objects, placed free-form within the pocket space.")]
    public List<PlacedObject> placed = new List<PlacedObject>();
}

[System.Serializable]
public struct CorePlacement
{
    public bool present;
    public Vector2 pos;        // free-form position in cell units within the pocket
}

/// <summary>
/// One authored spawner: a zone in the rock that spawns enemies into the excavation. Its look is a raw
/// PNG (world size derives from the pixels: 16×16 px = 1×1 units, 32×32 = 2×2, …). It only operates while
/// the dungeon's excavated space is within <see cref="activationRange"/> of it; any further and it is
/// inactive. Per DAY it holds one wave-grid (see <see cref="SpawnerDay"/>).
/// </summary>
[System.Serializable]
public class MineSpawnerTemplate
{
    /// <summary>Pixels per world unit for the spawner PNG (16 px = 1 unit).</summary>
    public const float PixelsPerUnit = 16f;

    public string name = "Spawner";
    public bool enabled = true;
    [Tooltip("The spawner's look — a raw PNG. World size = pixels / 16 (16×16 = 1×1 units, 32×32 = 2×2…).")]
    public Texture2D image;
    [Tooltip("Max distance (world units) from the nearest excavated cell at which this spawner operates. " +
             "Further than this from the dig = fully inactive.")]
    public float activationRange = 8f;
    [Tooltip("One entry per era-day this spawner is armed. Each day carries its own wave-grid.")]
    public List<SpawnerDay> days = new List<SpawnerDay>();
    [Tooltip("Cost against the era's spawner budget. Auto = average enemy points per armed day; " +
             "tick to hand-set instead.")]
    public bool costOverridden = false;
    public float costOverride = 0f;

    /// <summary>World size (units) of the spawner, derived from its PNG (0 if no image).</summary>
    public Vector2 WorldSize => image == null
        ? Vector2.zero
        : new Vector2(image.width / PixelsPerUnit, image.height / PixelsPerUnit);

    /// <summary>The authored plan for a 1-based era day (null if this spawner has nothing that day).</summary>
    public SpawnerDay PlanForDay(int day)
    {
        if (days != null)
            foreach (var d in days)
                if (d != null && d.day == day && d.HasAnyEnemy()) return d;
        return null;
    }
}

/// <summary>
/// One day's wave-grid for a spawner. ONE grid maps every wave of the day: each ROW is a wave (top row =
/// wave 1) and every enemy on that row spawns during that wave — the column NEVER affects where or when
/// an enemy spawns, it's just authoring space. Grid dimensions are author-set.
/// </summary>
[System.Serializable]
public class SpawnerDay
{
    [Tooltip("1-based day within the era this plan runs on.")]
    public int day = 1;
    [Tooltip("Grid height — one row = one wave (top row spawns first).")]
    public int rows = 1;
    [Tooltip("Grid width — purely authoring space for laying out different enemy counts per wave.")]
    public int cols = 10;
    [Tooltip("Seconds over which each wave's enemies trickle in.")]
    public float timePerWave = 3f;
    [Tooltip("Seconds between one wave and the next.")]
    public float interWaveWait = 6f;
    public List<SpawnerCell> cells = new List<SpawnerCell>();

    public bool HasAnyEnemy()
    {
        if (cells != null)
            foreach (var c in cells)
                if (c.enemy != null) return true;
        return false;
    }

    /// <summary>Enemies on one row = the enemies of that wave (row 0 = wave 1).</summary>
    public List<MarauderSO> WaveEnemies(int row)
    {
        var list = new List<MarauderSO>();
        if (cells != null)
            foreach (var c in cells)
                if (c.row == row && c.enemy != null) list.Add(c.enemy);
        return list;
    }

    public MarauderSO CellAt(int row, int col)
    {
        if (cells != null)
            foreach (var c in cells)
                if (c.row == row && c.col == col) return c.enemy;
        return null;
    }

    public void SetCell(int row, int col, MarauderSO enemy)
    {
        if (cells == null) cells = new List<SpawnerCell>();
        cells.RemoveAll(c => c.row == row && c.col == col);
        if (enemy != null) cells.Add(new SpawnerCell { row = row, col = col, enemy = enemy });
    }

    /// <summary>Drop cells that no longer fit the grid (after a resize).</summary>
    public void PruneToGrid()
    {
        if (cells != null) cells.RemoveAll(c => c.row < 0 || c.row >= rows || c.col < 0 || c.col >= cols);
    }
}

[System.Serializable]
public struct SpawnerCell
{
    public int row;   // row = which wave (0 = wave 1)
    public int col;   // authoring space only — never affects spawn position/time
    public MarauderSO enemy;
}

