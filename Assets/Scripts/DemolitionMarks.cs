using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Registry of placed buildings the player has marked for demolition (Delete/Backspace over the
/// building — input lives in BM.Update). Spare repair-kit drones assign THEMSELVES to marked
/// buildings and deconstruct them (Drone.TryDispatchWork is the job board); pressing Delete again
/// before the teardown finishes cancels the mark. Mirrors OreMarks.
/// </summary>
public static class DemolitionMarks
{
    static readonly HashSet<Building> marked = new HashSet<Building>();
    static readonly List<Building> dead = new List<Building>();
    static int lastPruneFrame = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()   // no-domain-reload: statics survive play-stop
    {
        marked.Clear();
        lastPruneFrame = -1;
    }

    public static bool IsMarked(Building b) => b != null && marked.Contains(b);

    public static void Toggle(Building b)
    {
        if (b == null) return;
        if (marked.Remove(b)) b.SetDemolitionMark(false);
        else
        {
            marked.Add(b);
            b.SetDemolitionMark(true);
        }
    }

    public static bool Any
    {
        get
        {
            // Any is polled per idle drone per tick — prune once per rendered frame is plenty
            // (ClaimFor still prunes on every call before handing out a target).
            if (Time.frameCount != lastPruneFrame)
            {
                lastPruneFrame = Time.frameCount;
                Prune();
            }
            return marked.Count > 0;
        }
    }

    /// <summary>Best marked building for <paramref name="d"/>: nearest, but buildings already
    /// claimed by another wrecker are pushed back so a squad spreads over the marks instead of
    /// piling onto one. Claims the result.</summary>
    public static Building ClaimFor(Drone d, Vector2 from)
    {
        Prune();
        Building best = null;
        float bestScore = float.MaxValue;
        foreach (var b in marked)
        {
            float score = ((Vector2)b.transform.position - from).magnitude;
            if (b.demolisher != null && b.demolisher != d) score += 6f;   // taken seat — only share when far cheaper
            if (score < bestScore) { bestScore = score; best = b; }
        }
        if (best != null && d != null) best.demolisher = d;
        return best;
    }

    static void Prune()
    {
        dead.Clear();
        foreach (var b in marked)
            if (b == null || !b.MarkedForDemolition) dead.Add(b);
        for (int k = 0; k < dead.Count; k++) marked.Remove(dead[k]);
    }
}
