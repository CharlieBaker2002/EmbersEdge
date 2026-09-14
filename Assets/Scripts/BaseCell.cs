using UnityEngine;

/// <summary>
/// The base build grid's WORLD-quantised cells, shared by every cell-keyed piece of plumbing
/// (Tube clusters, Belt lines): the same lattice Building.CurrentWorldAnchor uses (cell x spans
/// x·cs … (x+1)·cs), immune to GridManager re-anchoring its origin when the map grows.
/// </summary>
public static class BaseCell
{
    /// <summary>The base grid's cell size (0.25 in World).</summary>
    public static float Cs => GridManager.i != null ? GridManager.i.cellSize : 0.25f;

    public static Vector2Int Of(Vector2 p)
    {
        float cs = Cs;
        return new Vector2Int(Mathf.RoundToInt((p.x - 0.5f * cs) / cs), Mathf.RoundToInt((p.y - 0.5f * cs) / cs));
    }

    public static Vector2 Centre(Vector2Int c)
    {
        float cs = Cs;
        return new Vector2((c.x + 0.5f) * cs, (c.y + 0.5f) * cs);
    }

    /// <summary>A direction snapped to the nearest axis (a belt's up after any R spin).</summary>
    public static Vector2Int Dir(Vector2 v)
    {
        if (Mathf.Abs(v.x) >= Mathf.Abs(v.y))
            return v.x >= 0f ? Vector2Int.right : Vector2Int.left;
        return v.y >= 0f ? Vector2Int.up : Vector2Int.down;
    }
}
