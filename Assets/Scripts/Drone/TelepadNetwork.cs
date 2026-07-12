using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pairs telepads across dimensions by BUILD ORDER: base pad #N links to dungeon pad #N.
/// Slots are tombstoned (nulled, never compacted) on true destruction so surviving pads keep
/// their numbers; the next pad built on that side takes the lowest free slot. Static registry
/// with domain-reload-off ⇒ explicit reset.
/// </summary>
public static class TelepadNetwork
{
    static readonly List<Telepad> basePads = new List<Telepad>();
    static readonly List<Telepad> dungeonPads = new List<Telepad>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        basePads.Clear();
        dungeonPads.Clear();
    }

    /// <summary>Claim the lowest free slot on the pad's side and return its index.</summary>
    public static int Register(Telepad t)
    {
        var list = ListFor(t);
        for (int k = 0; k < list.Count; k++)
            if (list[k] == null) { list[k] = t; return k; }
        list.Add(t);
        return list.Count - 1;
    }

    public static void Unregister(Telepad t)
    {
        var list = ListFor(t);
        int idx = list.IndexOf(t);
        if (idx >= 0) list[idx] = null;
        while (list.Count > 0 && list[list.Count - 1] == null)
            list.RemoveAt(list.Count - 1);
    }

    /// <summary>Standing drill-drone demand across live base pads (tombstones skipped) —
    /// registry walk instead of scanning every Building.</summary>
    public static int BaseDrillDemand()
    {
        int n = 0;
        for (int k = 0; k < basePads.Count; k++)
        {
            var tp = basePads[k];
            if (tp != null && tp.IsOperational) n += tp.reqDrill;
        }
        return n;
    }

    /// <summary>The same-numbered pad on the other side; null when none exists (yet).</summary>
    public static Telepad LinkOf(Telepad t)
    {
        if (t == null || t.index < 0) return null;
        var other = t.IsDungeonSide ? basePads : dungeonPads;
        return t.index < other.Count ? other[t.index] : null;
    }

    /// <summary>Snapshot of the live dungeon pads (era regeneration tears these down).</summary>
    public static List<Telepad> DungeonPadsSnapshot()
    {
        var outList = new List<Telepad>();
        foreach (var t in dungeonPads)
            if (t != null) outList.Add(t);
        return outList;
    }

    static List<Telepad> ListFor(Telepad t) => t.IsDungeonSide ? dungeonPads : basePads;
}
