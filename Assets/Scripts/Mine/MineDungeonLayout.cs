using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The concrete result of one dungeon generation pass — what MineDungeonManager hands to
/// MineField.Build and to the Pocket runtime objects. Plain data (no MonoBehaviour).
/// </summary>
public class MineDungeonLayout
{
    public int era;                              // 0-based era index (drives tile art selection)
    public BoundsInt areaCells;                 // whole dungeon in cell space (0.25u cells)
    public Vector3Int entryCell;                // player entry cavity centre (carved + explored)
    public int entryCavityRadius = 6;           // cells
    public List<PocketInstance> pockets = new List<PocketInstance>();
}

/// <summary>One placed pocket: its carved rect, the template it came from, and its budget.</summary>
public class PocketInstance
{
    public RectInt cellRect;          // cavity in cell space
    public PocketTemplate template;
    public float weight;              // random draw within template.weightRange
    public float normalizedPoints;    // weight * target / calculatedSum
    public int pocketIndex;           // index into MineField's pocketCells/pocketOpened — set in MineField.Build order

    public Vector3Int CentreCell => new Vector3Int(
        cellRect.xMin + cellRect.width / 2,
        cellRect.yMin + cellRect.height / 2,
        0);
}
