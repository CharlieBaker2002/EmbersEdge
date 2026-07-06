using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The walkability/cost seam under the flow-field / LOS / A* machinery, so the same algorithms run
/// over the dungeon's MineField grid AND the base's building grid. Two ideas are deliberately kept
/// separate:
///
///  - <see cref="IPathGrid.IsSolid"/>: does the cell block SIGHT and body movement (LOS rays,
///    diagonal corner-cutting, A*)? Chewable walls are ALWAYS solid in this sense.
///  - <see cref="IPathGrid.EnterCost"/>: what does the flow-field wave pay to cross the cell?
///    1 = open ground, <see cref="PathGrid.BLOCKED"/> = impassable, 2..64 = a chewable wall priced
///    by its HP. A finite-cost solid cell is how "attack the wall if the detour is too long" works:
///    the wave crosses it at a price and the downhill direction steers the enemy INTO the wall,
///    where contact damage (and gate-target acquisition) takes over.
/// </summary>
public interface IPathGrid
{
    bool Ready { get; }
    RectInt CellRect { get; }
    float CellSize { get; }
    Vector3Int WorldToCell(Vector2 world);
    Vector3 CellCenterWorld(Vector3Int c);
    bool IsSolid(Vector3Int c);
    int EnterCost(Vector3Int c);
    /// <summary>May seeds sit ON solid cells? Base: yes (building footprints attract attackers into
    /// contact). Mine: no (seeds snap to open neighbours as before).</summary>
    bool AllowSolidSeeds { get; }
    /// <summary>Chewable-wall identity for gate propagation: -1 = not a wall cell, else a stable
    /// index into <see cref="Walls"/> for this rebuild window.</summary>
    int WallIdAt(Vector3Int c);
    IReadOnlyList<LifeScript> Walls { get; }
}

public static class PathGrid
{
    public const int BLOCKED = int.MaxValue;
}

/// <summary>
/// The DUNGEON's graded wall-padding dial: rings of extra walk cost around ore make the cheapest
/// line through any tunnel its centreline. (The base deliberately does NOT use this — it keeps
/// its original single-ring wallPaddingCost, because deep rings taxed wall crossings into
/// detours and tier changes would rebuild its caches for nothing.) Reach is driven by BODY SIZE
/// tier (<see cref="BodySize"/> → 0.4/0.8/1.6u) and computed only for what is NECESSARY at the
/// current timestep: every <c>Decide</c> call stamps its unit's tier, the active standoff is the
/// largest tier stamped within the last <see cref="ActiveWindow"/> seconds, and it relaxes
/// automatically once the big bodies stop pathing (die/despawn) — nothing is ever built for
/// tiers nobody uses. Cost falls linearly from <see cref="PeakCost"/> at the ore face to 1 at
/// the standoff edge.
/// </summary>
public static class PathPadding
{
    public const int PeakCost = 9;
    const float ActiveWindow = 2f;   // seconds a tier stays active after its last pathing query

    static readonly float[] tierRadius = { 0.4f, 0.8f, 1.6f };   // BodySize.Small/Medium/Large
    static readonly float[] lastSeen = { float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity };
    static int activeTier = -1;      // -1 = nothing pathing → no padding
    static int version;

    // domain reload is off in this project: statics survive play sessions and MUST self-reset
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        for (int k = 0; k < lastSeen.Length; k++) lastSeen[k] = float.NegativeInfinity;
        activeTier = -1;
        version++;
    }

    /// <summary>Stamped by every pathing decision: this tier is in use right now.</summary>
    public static void Report(BodySize s) => lastSeen[(int)s] = Time.time;

    static void Refresh()
    {
        int want = -1;
        for (int k = tierRadius.Length - 1; k >= 0; k--)
            if (Time.time - lastSeen[k] <= ActiveWindow) { want = k; break; }
        if (want != activeTier) { activeTier = want; version++; }
    }

    /// <summary>Bumped whenever the active tier changes — cost-layer caches key on this.</summary>
    public static int RingsVersion { get { Refresh(); return version; } }

    /// <summary>Current aversion reach in world units (0 = nothing pathing → no padding).</summary>
    public static float Standoff { get { Refresh(); return activeTier < 0 ? 0f : tierRadius[activeTier]; } }

    /// <summary>Per-grid ring cost table for the current standoff (index 0 = ring 1 = touching;
    /// empty at standoff 0 — no padding at all).</summary>
    public static int[] BuildTable(float cellSize)
    {
        int rings = Mathf.Max(0, Mathf.CeilToInt(Standoff / Mathf.Max(0.01f, cellSize)));
        var t = new int[rings];
        for (int k = 0; k < rings; k++)
            t[k] = Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(PeakCost, 1f, rings == 1 ? 0f : k / (float)(rings - 1))));
        return t;
    }
}

/// <summary>
/// Which pathing world a position lives in. The base is centred on the world origin; dungeons are
/// generated far away — same convention as <see cref="GS.InDungeon"/> (keep the constant in sync).
/// Dispatch is ALWAYS by querier position, never by global state, because both dimensions can be
/// live at once (frozen dungeon + base fight, and vice versa).
/// </summary>
public static class PathZone
{
    public const float DungeonSqrDistance = 600000f;
    public static bool AtBase(Vector2 pos) => pos.sqrMagnitude <= DungeonSqrDistance;
}

/// <summary>MineField as an IPathGrid: solid = impassable (enemies can't mine), open = cost 1 plus
/// the graded wall padding (same size-driven <see cref="PathPadding"/> rings as the base grid,
/// over a chebyshev distance transform keyed to <see cref="MineField.SolidVersion"/>). The dungeon
/// is nothing BUT corridors, so the padding is what keeps routes — and every enemy following them —
/// off the tunnel faces wherever the width allows; centred routes cost (almost) what unweighted
/// BFS charged, so distances keep their meaning.</summary>
public sealed class MineGridAdapter : IPathGrid
{
    public static readonly MineGridAdapter i = new MineGridAdapter();
    static readonly List<LifeScript> noWalls = new List<LifeScript>();

    byte[] padRing;                    // chebyshev ring distance to nearest ore (0 = solid, capped)
    RectInt padRect;
    int padVersion = int.MinValue;
    int[] padTable;
    int padRings = int.MinValue;       // PathPadding.RingsVersion the table/DT were built with

    public bool Ready => MineField.i != null && MineField.i.Built;
    public RectInt CellRect => MineField.i.CellRect;
    public float CellSize => MineField.i.CellSize;
    public Vector3Int WorldToCell(Vector2 world) => MineField.i.WorldToCell(world);
    public Vector3 CellCenterWorld(Vector3Int c) => MineField.i.CellCenterWorld(c);
    public bool IsSolid(Vector3Int c) => MineField.i.IsSolid(c);
    public int EnterCost(Vector3Int c) => MineField.i.IsSolid(c) ? PathGrid.BLOCKED : 1 + PadCost(c);
    public bool AllowSolidSeeds => false;
    public int WallIdAt(Vector3Int c) => -1;
    public IReadOnlyList<LifeScript> Walls => noWalls;

    /// <summary>Padding cost of one open cell (0 when solid/off-rect/beyond the last ring). Public
    /// for the route gizmo's heatmap/dump.</summary>
    public int PadCost(Vector3Int c)
    {
        var f = MineField.i;
        if (f == null || !f.Built) return 0;
        if (padRing == null || padVersion != f.SolidVersion || padRings != PathPadding.RingsVersion
            || !padRect.Equals(f.CellRect)) RebuildPad(f);
        if (padRing == null) return 0;
        int x = c.x - padRect.xMin, y = c.y - padRect.yMin;
        if (x < 0 || y < 0 || x >= padRect.width || y >= padRect.height) return 0;
        int ring = padRing[x + y * padRect.width];
        return ring > 0 && ring <= padTable.Length ? padTable[ring - 1] : 0;
    }

    // Same two-pass chamfer transform as the base grid's pass 2.5; O(cells), runs only when the
    // mine's solidity actually changed (mining ticks bump SolidVersion), so a quiet dungeon is free.
    void RebuildPad(MineField f)
    {
        padVersion = f.SolidVersion;
        padRings = PathPadding.RingsVersion;
        padTable = PathPadding.BuildTable(CellSize);
        padRect = f.CellRect;
        int W = padRect.width, H = padRect.height, n = W * H;
        if (n <= 0) { padRing = null; return; }
        if (padRing == null || padRing.Length != n) padRing = new byte[n];
        int maxRing = padTable.Length + 1;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int idx = x + y * W;
                int best = f.IsSolid(new Vector3Int(padRect.xMin + x, padRect.yMin + y, 0)) ? 0 : maxRing;
                if (best > 0)
                {
                    if (x > 0) best = Mathf.Min(best, padRing[idx - 1] + 1);
                    if (y > 0)
                    {
                        best = Mathf.Min(best, padRing[idx - W] + 1);
                        if (x > 0) best = Mathf.Min(best, padRing[idx - W - 1] + 1);
                        if (x < W - 1) best = Mathf.Min(best, padRing[idx - W + 1] + 1);
                    }
                }
                padRing[idx] = (byte)Mathf.Min(best, maxRing);
            }
        for (int y = H - 1; y >= 0; y--)
            for (int x = W - 1; x >= 0; x--)
            {
                int idx = x + y * W;
                int best = padRing[idx];
                if (best == 0) continue;
                if (x < W - 1) best = Mathf.Min(best, padRing[idx + 1] + 1);
                if (y < H - 1)
                {
                    best = Mathf.Min(best, padRing[idx + W] + 1);
                    if (x < W - 1) best = Mathf.Min(best, padRing[idx + W + 1] + 1);
                    if (x > 0) best = Mathf.Min(best, padRing[idx + W - 1] + 1);
                }
                padRing[idx] = (byte)best;
            }
    }
}
