using UnityEngine;

/// <summary>
/// What a single mine cell is made of. Stored densely in MineField's CellData[].
/// </summary>
public enum CellType : byte
{
    Empty = 0,      // mined out / carved cavity — walkable, no collider
    Regular = 1,    // plain rock you tunnel through (no orb)
    Hard = 2,       // tougher rock (more chips to break)
    VeryHard = 7,   // hardest authored pocket walls (PocketTileType.VeryHardWall)
    IngotWhite = 3, // yields a white orb on break
    IngotGreen = 4, // yields a green orb on break
    IngotBlue = 5,  // yields a blue orb on break  (ResourceManager index 2 — internally "grey")
    IngotRed = 6,   // yields a red orb on break
}

/// <summary>
/// One mine cell. ~4 bytes — kept in a flat array so a 150k-cell dungeon is &lt;1 MB and
/// never spawns a GameObject per tile.
/// </summary>
public struct CellData
{
    public CellType type;     // hardness of the wall (Regular/Hard/VeryHard), or Empty when mined out
    public ushort durability; // remaining chips before the cell breaks
    public bool explored;     // the player has reached this cell (drives fog + frontier colliders)
    public bool voidCell;     // OUTSIDE the dungeon's boundary ring: invisible, unmineable, but solid
                              // (collision holds) — the world simply ends there.
    public sbyte ore;         // -1 = no ore; else orb index 0..3 (white/green/blue/red). Drawn as an
                              // emissive overlay ON the wall — the wall keeps its own hardness look.

    public bool IsSolid => type != CellType.Empty;
    public bool HasOre => ore >= 0;
}

/// <summary>Static lookups for cell types (no per-cell MonoBehaviour — this replaces Ore.cs for the dungeon).</summary>
public static class MineCells
{
    /// <summary>Orb index (into ResourceManager.orbs: white/green/grey/red) a cell yields on break, or -1 for none.</summary>
    public static int OrbIndex(CellType t)
    {
        switch (t)
        {
            case CellType.IngotWhite: return 0;
            case CellType.IngotGreen: return 1;
            case CellType.IngotBlue:  return 2; // index 2 is "grey" in ResourceManager, "blue" in the palette
            case CellType.IngotRed:   return 3;
            default: return -1;
        }
    }

    public static bool IsIngot(CellType t) => OrbIndex(t) >= 0;
}
