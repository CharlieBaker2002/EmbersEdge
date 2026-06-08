using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Authored, semi-procedural wave content edited by the Wave Forge window (Tools > Wave Forge) and
/// consumed at runtime by SpawnManager.BuildFullPlan. Structure:
///   dungeon (era 0/1/2) -> collection ("Main" + N) -> days (6/10/14) -> subwaves (time + grid enemies)
///                                                   -> clusters (grid enemies, no time; budget fillers)
/// Positions are authored on a virtual grid; the column maps to a boundary-relative t-offset so the
/// formation follows its core. One asset lives at Resources/WaveAuthoring.asset.
/// </summary>
[CreateAssetMenu(fileName = "WaveAuthoring", menuName = "ScriptableObjects/Wave Authoring", order = 0)]
public class WaveAuthoringSO : ScriptableObject
{
    [Header("Grid / placement")]
    public int gridCols = 14;
    public int gridRows = 6;
    [Tooltip("World-unit spacing along the rim between adjacent grid columns. Authored in world units " +
             "(not a fraction of the perimeter) so a formation keeps the same physical width as the map grows.")]
    public float unitsPerColumn = 1.5f;

    [Header("Budget")]
    [Tooltip("Each extra (non-main) core adds this fraction of the Main collection's day price to the day's credit budget.")]
    [Range(0f, 2f)] public float budgetPerExtraCore = 0.4f;

    [Header("Enemy palettes (per dungeon / era)")]
    public MarauderSO[] dungeon1Enemies;
    public MarauderSO[] dungeon2Enemies;
    public MarauderSO[] dungeon3Enemies;

    [Header("Authored content")]
    public List<DungeonWaves> dungeons = new List<DungeonWaves>();

    public const string MAIN = "Main";
    public static readonly int[] DayCounts = { 6, 10, 14 };
    public static int DayCount(int dungeon) => DayCounts[Mathf.Clamp(dungeon, 0, 2)];

    public MarauderSO[] Palette(int dungeon)
    {
        switch (Mathf.Clamp(dungeon, 0, 2))
        {
            case 0: return dungeon1Enemies;
            case 1: return dungeon2Enemies;
            default: return dungeon3Enemies;
        }
    }

    public DungeonWaves GetDungeon(int dungeon)
    {
        dungeon = Mathf.Clamp(dungeon, 0, 2);
        while (dungeons.Count <= dungeon) dungeons.Add(new DungeonWaves());
        dungeons[dungeon].EnsureMain(dungeon);
        return dungeons[dungeon];
    }

    public WaveCollection GetCollection(int dungeon, string name)
    {
        if (string.IsNullOrEmpty(name)) name = MAIN;
        foreach (var c in GetDungeon(dungeon).collections)
            if (c.name == name) return c;
        return null;
    }

    /// <summary>
    /// Random collection name in a dungeon (the same one may be returned for several cores). When
    /// excluding "Main" and there are no non-main collections, returns null — a non-main core is never
    /// assigned the Main collection.
    /// </summary>
    public string RandomCollectionName(int dungeon, bool excludeMain)
    {
        var pool = new List<string>();
        foreach (var c in GetDungeon(dungeon).collections)
            if (!excludeMain || c.name != MAIN) pool.Add(c.name);
        if (pool.Count > 0) return pool[Random.Range(0, pool.Count)];
        return excludeMain ? null : MAIN;
    }

    public DayPlan GetDay(WaveCollection coll, int dungeon, int dayIndex)
    {
        if (coll == null) return null;
        int count = DayCount(dungeon);
        coll.EnsureDays(count);
        return coll.days[Mathf.Clamp(dayIndex, 0, count - 1)];
    }

    /// <summary>
    /// Grid column -> boundary-t offset relative to the core (centre column = on the core).
    /// <paramref name="perimeter"/> is the boundary's current world arc length; dividing the world-unit
    /// column spacing by it yields the perimeter fraction (the t-offset), so the formation keeps a fixed
    /// world width as the map scales. Pass <c>MapManager.i.BoundaryPerimeter()</c>.
    /// </summary>
    public float ColToTOffset(int col, float perimeter)
    {
        if (perimeter <= 0f) return 0f;
        float centre = (gridCols - 1) * 0.5f;
        return (col - centre) * unitsPerColumn / perimeter;
    }

    // ---- Points / "score" (override-aware: the auto value is just a suggestion) ----
    public static float EnemyPoints(MarauderSO so) => so != null ? so.price : 0f;

    public static float SubwavePoints(Subwave sw)
    {
        float p = 0f;
        if (sw != null) foreach (var e in sw.enemies) p += EnemyPoints(e.so);
        return p;
    }

    public static float DayAutoPoints(DayPlan d)
    {
        float p = 0f;
        if (d != null) foreach (var sw in d.subwaves) p += SubwavePoints(sw);
        return p;
    }

    public static float ClusterAutoPoints(ClusterPlan c)
    {
        float p = 0f;
        if (c != null) foreach (var e in c.enemies) p += EnemyPoints(e.so);
        return p;
    }

    /// <summary>Day "score" used for budgeting — the designer override when set, else the auto sum.</summary>
    public static float DayPoints(DayPlan d) => d != null && d.scoreOverridden ? d.scoreOverride : DayAutoPoints(d);

    /// <summary>Cluster cost used for affordability — the designer override when set, else the auto sum.</summary>
    public static float ClusterPoints(ClusterPlan c) => c != null && c.scoreOverridden ? c.scoreOverride : ClusterAutoPoints(c);

    /// <summary>How long a day's authored wave lasts: max over its subwaves of (start time + duration).</summary>
    public static float DayWaveDuration(DayPlan d)
    {
        float max = 0f;
        if (d != null) foreach (var sw in d.subwaves) max = Mathf.Max(max, sw.time + sw.duration);
        return max;
    }
}

[System.Serializable]
public class DungeonWaves
{
    public List<WaveCollection> collections = new List<WaveCollection>();

    public void EnsureMain(int dungeon)
    {
        foreach (var c in collections) if (c.name == WaveAuthoringSO.MAIN) return;
        collections.Insert(0, new WaveCollection(WaveAuthoringSO.MAIN, dungeon));
    }
}

[System.Serializable]
public class WaveCollection
{
    public string name = "Collection";
    public List<DayPlan> days = new List<DayPlan>();
    public List<ClusterPlan> clusters = new List<ClusterPlan>(); // collection-specific (era-specific), not per-day
    // How many times this collection appears per round-robin cycle when filling leftover budget with
    // clusters. 1 = normal. Higher = picked proportionally more often, but spread evenly through the
    // cycle (never necessarily back-to-back). See SpawnManager.FillClusters.
    public int clusterFavour = 1;

    public WaveCollection() { }
    public WaveCollection(string n, int dungeon) { name = n; EnsureDays(WaveAuthoringSO.DayCount(dungeon)); }

    public void EnsureDays(int count)
    {
        while (days.Count < count)
        {
            var dp = new DayPlan();
            dp.subwaves.Add(new Subwave()); // every day starts with one subwave by default
            days.Add(dp);
        }
    }
}

[System.Serializable]
public class DayPlan
{
    public List<Subwave> subwaves = new List<Subwave>();
    public bool scoreOverridden;   // when true, the day's budget score is the manual value, not the auto sum
    public float scoreOverride;
}

[System.Serializable]
public class Subwave
{
    public float time = 0f;      // seconds after the wave starts (when this subwave begins)
    public float duration = 2f;  // seconds over which this subwave's enemies are spread (price-weighted)
    public List<PlacedEnemy> enemies = new List<PlacedEnemy>();
}

[System.Serializable]
public class ClusterPlan
{
    public string name = "Cluster";
    public float duration = 3f;  // seconds over which the cluster's enemies are spread (price-weighted)
    public List<PlacedEnemy> enemies = new List<PlacedEnemy>();
    public bool scoreOverridden;   // when true, the cluster's cost is the manual value, not the auto sum
    public float scoreOverride;
}

[System.Serializable]
public struct PlacedEnemy
{
    public MarauderSO so;
    public int gridX; // column -> boundary position
    public int gridY; // row   -> stacking / intra-group order

    public PlacedEnemy(MarauderSO so, int x, int y) { this.so = so; gridX = x; gridY = y; }
}
