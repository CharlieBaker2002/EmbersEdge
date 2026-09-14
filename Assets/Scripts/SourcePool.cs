using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One energy pool for a shape of connected plumbing — a Tube cluster, a Belt line: every
/// DISTINCT grid source any member tile touches (two tiles beside one pad share it once),
/// re-resolved on a short cadence. "Power along the line": a source touching any one tile
/// powers the whole run. Draws go against each source's own per-frame cap
/// (MaxDrawThisFrame — a battery's surge lands at once, a bare generator streams).
/// </summary>
public class SourcePool
{
    readonly IReadOnlyList<Building> members;
    readonly List<IEnergyAccumulator> sources = new List<IEnergyAccumulator>();
    float refreshedAt = float.NegativeInfinity;

    public SourcePool(IReadOnlyList<Building> members) { this.members = members; }

    /// <summary>The distinct sources, refreshed if stale.</summary>
    public IReadOnlyList<IEnergyAccumulator> Sources { get { Refresh(); return sources; } }

    public void Refresh(bool force = false)
    {
        if (!force && Time.time - refreshedAt < 0.5f) return;
        refreshedAt = Time.time;
        sources.Clear();
        for (int k = 0; k < members.Count; k++)
        {
            var m = members[k];
            if (m == null) continue;
            var list = m.Power.Sources;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && !sources.Contains(list[i])) sources.Add(list[i]);
        }
    }

    /// <summary>Energy banked across the connected sources.</summary>
    public float Energy()
    {
        Refresh();
        float s = 0f;
        for (int i = 0; i < sources.Count; i++) if (sources[i] != null) s += sources[i].Energy;
        return s;
    }

    /// <summary>Sustained energy/sec the connected sources can give (sources with nothing left give 0).</summary>
    public float DrawRate()
    {
        Refresh();
        float s = 0f;
        for (int i = 0; i < sources.Count; i++) if (sources[i] != null) s += sources[i].DrawRate;
        return s;
    }

    public bool Connected { get { Refresh(); return sources.Count > 0; } }

    /// <summary>A connected source with energy.</summary>
    public bool Powered => Connected && Energy() > 1e-3f;

    /// <summary>Side-effect-free offer over <paramref name="dt"/> from every source (fair-share
    /// peek — never MaxDrawThisFrame, which registers a drawer).</summary>
    public float PeekOffer(float dt)
    {
        Refresh();
        float s = 0f;
        for (int i = 0; i < sources.Count; i++) if (sources[i] != null) s += sources[i].PeekMaxDraw(dt);
        return s;
    }

    /// <summary>Take up to <paramref name="want"/> this frame, honestly capped per source. Returns what was paid.</summary>
    public float Draw(float want)
    {
        Refresh(force: true);
        float dt = Time.deltaTime, paid = 0f;
        for (int i = 0; i < sources.Count && want - paid > 1e-5f; i++)
        {
            var s = sources[i];
            if (s == null) continue;
            float cap = s.MaxDrawThisFrame(dt);
            float take = Mathf.Min(cap, want - paid);
            if (take > 1e-5f && s.Use(take)) paid += take;
        }
        return paid;
    }
}
