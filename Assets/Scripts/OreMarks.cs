using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Registry of base ore tiles the player has marked for deconstruction (click-hold 1s on the
/// tile — input lives in DroneManager.Update). Drill drones assign THEMSELVES to marked ore;
/// the player never orders a unit directly. Marked tiles are tinted grey on their resource
/// tilemap; the tint reverts on unmark and dies naturally with the tile when mined out.
/// </summary>
public static class OreMarks
{
    static readonly HashSet<Ore> marked = new HashSet<Ore>();
    static readonly List<Ore> dead = new List<Ore>();

    static readonly Color MarkTint = new Color(0.5f, 0.5f, 0.55f, 1f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()   // no-domain-reload: statics survive play-stop
    {
        marked.Clear();
        lastPruneFrame = -1;
    }

    public static bool IsMarked(Ore o) => o != null && marked.Contains(o);

    public static void Toggle(Ore o)
    {
        if (o == null || o.Depleted) return;
        if (marked.Remove(o)) Tint(o, false);
        else
        {
            marked.Add(o);
            Tint(o, true);
        }
    }

    static int lastPruneFrame = -1;

    public static bool Any
    {
        get
        {
            // Any is polled per drill drone per tick — prune once per rendered frame is plenty
            // (ClaimFor still prunes on every call before handing out a target).
            if (Time.frameCount != lastPruneFrame)
            {
                lastPruneFrame = Time.frameCount;
                Prune();
            }
            return marked.Count > 0;
        }
    }

    /// <summary>Best marked tile for <paramref name="d"/>: nearest, but tiles already claimed by
    /// another living miner are pushed back so a squad spreads over the marks instead of piling
    /// onto one. Claims the result.</summary>
    public static Ore ClaimFor(Drone d, Vector2 from)
    {
        Prune();
        Ore best = null;
        float bestScore = float.MaxValue;
        foreach (var o in marked)
        {
            float score = ((Vector2)o.transform.position - from).magnitude;
            if (o.miner != null && o.miner != d) score += 6f;   // taken seat — only share when far cheaper
            if (score < bestScore) { bestScore = score; best = o; }
        }
        if (best != null && d != null) best.miner = d;
        return best;
    }

    static void Prune()
    {
        dead.Clear();
        foreach (var o in marked)
            if (o == null || o.Depleted) dead.Add(o);
        for (int k = 0; k < dead.Count; k++) marked.Remove(dead[k]);
    }

    static void Tint(Ore o, bool on)
    {
        if (o == null) return;
        int t = o.orbType;
        Tilemap map = t >= 0 && t < TilemapResource.m.Length ? TilemapResource.m[t] : null;
        if (map == null) return;
        Vector3Int cell = map.WorldToCell(o.transform.position);
        map.SetTileFlags(cell, TileFlags.None);   // RuleTiles lock colour by default
        map.SetColor(cell, on ? MarkTint : Color.white);
    }
}
