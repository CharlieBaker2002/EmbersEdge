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

/// <summary>MineField as an IPathGrid: solid = impassable (enemies can't mine), open = cost 1, no
/// chewable walls. With every cost 1 the weighted expansion degenerates to the original BFS, so
/// dungeon behavior is unchanged by the seam.</summary>
public sealed class MineGridAdapter : IPathGrid
{
    public static readonly MineGridAdapter i = new MineGridAdapter();
    static readonly List<LifeScript> noWalls = new List<LifeScript>();

    public bool Ready => MineField.i != null && MineField.i.Built;
    public RectInt CellRect => MineField.i.CellRect;
    public float CellSize => MineField.i.CellSize;
    public Vector3Int WorldToCell(Vector2 world) => MineField.i.WorldToCell(world);
    public Vector3 CellCenterWorld(Vector3Int c) => MineField.i.CellCenterWorld(c);
    public bool IsSolid(Vector3Int c) => MineField.i.IsSolid(c);
    public int EnterCost(Vector3Int c) => MineField.i.IsSolid(c) ? PathGrid.BLOCKED : 1;
    public bool AllowSolidSeeds => false;
    public int WallIdAt(Vector3Int c) => -1;
    public IReadOnlyList<LifeScript> Walls => noWalls;
}
