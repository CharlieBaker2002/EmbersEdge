using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Which grid cells a power source can serve — the cells a consumer could stand on and be
/// powered. Shared by the build-grid overlay (GridManager: blue "power here" cells) for both
/// placed sources and the ghost being placed:
///   • an Energy Pad — the cardinal ring around its footprint (exactly the cells BuildingPower probes);
///   • an Energy Hub — only the strip directly in front of its facing, ONE cell deep (its width × 1);
///   • a pylon — the ring around its footprint plus every cell inside its cable radius (light blue);
///   • generators / soul generators — nothing: they must be connected TO, they don't project power.
/// </summary>
public static class PowerReach
{
    public enum Kind { None, Pad, Pylon }

    /// <summary>A hub powers only what touches its front edge: one cell deep. Deliberately not a
    /// serialized knob — a field default gets baked into imported prefabs and can't be trusted.</summary>
    public const int HubForwardDepth = 1;

    /// <summary>Cells <paramref name="b"/> would power standing on <paramref name="anchor"/>/<paramref name="size"/>
    /// (its own footprint excluded). Returns the tint class, None for non-sources.</summary>
    public static Kind CellsFor(Building b, Vector2Int anchor, Vector2Int size, List<Vector2Int> into)
    {
        if (size.x <= 0 || size.y <= 0) size = Vector2Int.one;
        if (b is EnergyPad pad)
        {
            if (pad.hub) EnergyManager.HubForwardCells(pad.transform.up, anchor, size, HubForwardDepth, into);
            else RingCells(anchor, size, into);
            return Kind.Pad;
        }
        if (b is EnergyPylon pylon)
        {
            RingCells(anchor, size, into);
            RadiusCells(anchor, size, pylon.CableRadius, into);
            return Kind.Pylon;
        }
        return Kind.None;
    }

    /// <summary>The 4-neighbour ring around a footprint.</summary>
    public static void RingCells(Vector2Int anchor, Vector2Int size, List<Vector2Int> into)
    {
        for (int x = 0; x < size.x; x++)
        {
            into.Add(new Vector2Int(anchor.x + x, anchor.y - 1));
            into.Add(new Vector2Int(anchor.x + x, anchor.y + size.y));
        }
        for (int y = 0; y < size.y; y++)
        {
            into.Add(new Vector2Int(anchor.x - 1, anchor.y + y));
            into.Add(new Vector2Int(anchor.x + size.x, anchor.y + y));
        }
    }

    /// <summary>Every cell whose centre lies within <paramref name="radius"/> of the footprint's
    /// centre (the footprint itself excluded) — a pylon's cable reach.</summary>
    public static void RadiusCells(Vector2Int a, Vector2Int s, float radius, List<Vector2Int> into)
    {
        var gm = GridManager.i;
        if (gm == null || radius <= 0f) return;
        Vector2 lo = gm.GridToWorld(a), hi = gm.GridToWorld(a + s - Vector2Int.one);
        Vector2 centre = (lo + hi) * 0.5f;
        float r2 = radius * radius;
        int span = Mathf.CeilToInt(radius / Mathf.Max(0.01f, gm.cellSize)) + 1;
        Vector2Int mid = gm.WorldToGrid(centre);
        for (int dx = -span; dx <= span; dx++)
            for (int dy = -span; dy <= span; dy++)
            {
                var c = new Vector2Int(mid.x + dx, mid.y + dy);
                if (c.x >= a.x && c.x < a.x + s.x && c.y >= a.y && c.y < a.y + s.y) continue;
                Vector2 wc = gm.GridToWorld(c);
                if ((wc - centre).sqrMagnitude <= r2) into.Add(c);
            }
    }
}
