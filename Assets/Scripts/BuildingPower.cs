using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A consumer building's view onto the energy grid: aggregates the IEnergyAccumulator
/// sources (battery-holding pads, generators with internal storage, pylons relaying
/// upstream supply) in the 4-cardinal cells around its footprint. Use() splits cost
/// equally across sources that have any energy; Add() splits equally across sources
/// with any room. Owned by Building.cs; consumers reach it as building.Power.
/// </summary>
public class BuildingPower : IEnergyAccumulator
{
    private readonly Building owner;
    private readonly List<IEnergyAccumulator> sources = new();
    private bool dirty = true;

    /// <summary>Adjacent sources resolved on demand. Exposed so relays (pylons) can read the same view.</summary>
    public IReadOnlyList<IEnergyAccumulator> Sources
    {
        get { RefreshIfDirty(); return sources; }
    }

    private Action<float> _onUpdate;
    /// <summary>Fires when energy or adjacency changes. New subscribers immediately receive the current Energy on subscribe so they don't miss the initial state.</summary>
    public event Action<float> OnUpdate
    {
        add    { _onUpdate += value; value?.Invoke(Energy); }
        remove { _onUpdate -= value; }
    }
    public event Action OnUse;

    public BuildingPower(Building owner)
    {
        this.owner = owner;
        if (EnergyManager.i != null)
        {
            EnergyManager.i.OnPadsChanged += MarkDirty;
        }
    }

    public void Detach()
    {
        if (EnergyManager.i != null)
        {
            EnergyManager.i.OnPadsChanged -= MarkDirty;
        }
    }

    void MarkDirty()
    {
        dirty = true;
        // Proactively notify listeners (e.g., a turret watching HasAmmo) so they pick up
        // adjacency changes even if no consumer has read Power yet this frame.
        _onUpdate?.Invoke(Energy);
    }

    /// <summary>The owner's footprint moved (rotation swapped its cells) — re-resolve adjacency.</summary>
    public void Invalidate() => MarkDirty();

    void RefreshIfDirty()
    {
        if (!dirty) return;
        dirty = false;

        UnsubscribeSources();
        sources.Clear();

        if (EnergyManager.i == null || GridManager.i == null || owner == null) return;

        // For each cell of the footprint, look at its 4-cardinal neighbours.
        // Skip neighbours that fall back inside the footprint.
        var anchor = owner.anchorCell;
        var size = owner.gridSize;
        if (size.x <= 0 || size.y <= 0) size = Vector2Int.one;

        var seen = new HashSet<Vector2Int>();
        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                var c = new Vector2Int(anchor.x + x, anchor.y + y);
                TryAdd(c + Vector2Int.up, anchor, size, seen);
                TryAdd(c + Vector2Int.down, anchor, size, seen);
                TryAdd(c + Vector2Int.left, anchor, size, seen);
                TryAdd(c + Vector2Int.right, anchor, size, seen);
            }
        }
    }

    void TryAdd(Vector2Int cell, Vector2Int anchor, Vector2Int size, HashSet<Vector2Int> seen)
    {
        // Skip cells inside our own footprint.
        if (cell.x >= anchor.x && cell.x < anchor.x + size.x &&
            cell.y >= anchor.y && cell.y < anchor.y + size.y) return;
        if (!seen.Add(cell)) return;
        var cellSources = EnergyManager.i.SourcesAt(cell);
        for (int i = 0; i < cellSources.Count; i++)
        {
            var src = cellSources[i];
            // Source-level dedup: a multi-cell source can be reached via several neighbour cells.
            if (src == null || sources.Contains(src)) continue;
            // Don't list ourselves — a relay would otherwise see its own claimed cells.
            if (ReferenceEquals(src, owner)) continue;
            // Hubs only accept consumers aligned with their forward column.
            if (src is EnergyPad pad && pad.hub && !EnergyManager.HubAccessibleFrom(pad, anchor, size)) continue;
            sources.Add(src);
            src.OnUpdate += ForwardUpdate;
        }
    }

    void UnsubscribeSources()
    {
        for (int i = 0; i < sources.Count; i++)
        {
            if (sources[i] != null) sources[i].OnUpdate -= ForwardUpdate;
        }
    }

    void ForwardUpdate(float _) => _onUpdate?.Invoke(Energy);

    public float Energy
    {
        get
        {
            RefreshIfDirty();
            float s = 0f;
            for (int i = 0; i < sources.Count; i++) if (sources[i] != null) s += sources[i].Energy;
            return s;
        }
    }

    public float MaxEnergy
    {
        get
        {
            RefreshIfDirty();
            float s = 0f;
            for (int i = 0; i < sources.Count; i++) if (sources[i] != null) s += sources[i].MaxEnergy;
            return s;
        }
    }

    /// <summary>Combined energy/sec drawable from adjacent sources right now.</summary>
    public float DrawRate
    {
        get
        {
            RefreshIfDirty();
            float s = 0f;
            for (int i = 0; i < sources.Count; i++) if (sources[i] != null) s += sources[i].DrawRate;
            return s;
        }
    }

    public float MaxDrawThisFrame(float dt)
    {
        RefreshIfDirty();
        float s = 0f;
        for (int i = 0; i < sources.Count; i++) if (sources[i] != null) s += sources[i].MaxDrawThisFrame(dt);
        return s;
    }

    /// <summary>Side-effect-free MaxDrawThisFrame (no fair-share query registration) — gauge bars only.</summary>
    public float PeekMaxDraw(float dt)
    {
        RefreshIfDirty();
        float s = 0f;
        for (int i = 0; i < sources.Count; i++) if (sources[i] != null) s += sources[i].PeekMaxDraw(dt);
        return s;
    }

    /// <summary>
    /// Coroutine that drains <paramref name="amount"/> over time. Each frame we figure out
    /// how much each source can deliver this tick — capped by its DrawRate*dt and its
    /// remaining Energy — then split the demand across sources max-min fair (water-filling):
    /// every source gets an equal share of what we want; sources whose cap is below the share
    /// are pinned to their cap and the leftover is redistributed to the rest. Result: with
    /// rates (1, 1, 3) and demand 5 the slow sources max at 1 and the fat source absorbs the
    /// remaining 3; with rates (2, 2, 2) and demand 5 each delivers 1.667.
    /// Stalls (yields without consuming) when no source has anything to give.
    /// </summary>
    public IEnumerator DrawEnergy(float amount, Action<bool> onDone = null)
    {
        if (amount <= 0f) { onDone?.Invoke(true); yield break; }

        float remaining = amount;
        float[] alloc = null;
        float[] cap = null;
        bool[] capped = null;
        while (remaining > 1e-5f)
        {
            RefreshIfDirty();
            int N = sources.Count;
            if (N == 0) { yield return null; continue; }

            if (alloc == null || alloc.Length < N)
            {
                int sz = Mathf.Max(8, N);
                alloc = new float[sz];
                cap = new float[sz];
                capped = new bool[sz];
            }
            float dt = Time.deltaTime;
            float totalAvail = 0f;
            for (int i = 0; i < N; i++)
            {
                alloc[i] = 0f;
                capped[i] = false;
                var s = sources[i];
                if (s == null) { capped[i] = true; cap[i] = 0f; continue; }
                // Per-source per-frame cap — includes instabuffer for sources that have one.
                float c = s.MaxDrawThisFrame(dt);
                if (c <= 1e-6f) { capped[i] = true; cap[i] = 0f; continue; }
                cap[i] = c;
                totalAvail += c;
            }
            if (totalAvail <= 1e-6f) { yield return null; continue; }

            float toAllocate = Mathf.Min(remaining, totalAvail);

            // Water-filling: repeatedly hand each uncapped source an equal share of what's
            // left to allocate. Sources whose cap is below the share take their cap and
            // drop out; the share grows for the rest. Bounded by N pin-events.
            int safety = N + 2;
            while (toAllocate > 1e-6f && safety-- > 0)
            {
                int activeN = 0;
                for (int i = 0; i < N; i++) if (!capped[i]) activeN++;
                if (activeN == 0) break;
                float share = toAllocate / activeN;
                bool pinnedAny = false;
                for (int i = 0; i < N; i++)
                {
                    if (capped[i]) continue;
                    float room = cap[i] - alloc[i];
                    if (room <= share + 1e-6f)
                    {
                        alloc[i] += room;
                        toAllocate -= room;
                        capped[i] = true;
                        pinnedAny = true;
                    }
                }
                if (!pinnedAny)
                {
                    for (int i = 0; i < N; i++)
                    {
                        if (capped[i]) continue;
                        alloc[i] += share;
                        toAllocate -= share;
                    }
                    break;
                }
            }

            // Apply allocations directly to each source — bypassing BuildingPower.Use so
            // we don't re-split the cost generically and lose the rate-aware shape.
            float drained = 0f;
            for (int i = 0; i < N; i++)
            {
                if (alloc[i] <= 1e-6f) continue;
                if (sources[i] != null && sources[i].Use(alloc[i])) drained += alloc[i];
            }
            if (drained > 0f)
            {
                remaining -= drained;
                OnUse?.Invoke();
                _onUpdate?.Invoke(Energy);
            }
            yield return null;
        }
        onDone?.Invoke(true);
    }

    public bool Use(float cost)
    {
        if (cost <= 0f) return true;
        RefreshIfDirty();
        if (Energy < cost) return false;

        // One-shot Use now respects per-source per-frame caps too. If insta is depleted on
        // every source and rate*dt across them isn't enough, the draw fails outright — a
        // big burst can't quietly bypass the instabuffer ceiling. Distribution is the same
        // max-min fair water-fill DrawEnergy uses, just synchronous.
        // PEEK, deliberately: the fair-share demand registration belongs to the CALLER's
        // budget check (DrawStep's MaxDrawThisFrame — which registers even when the offer is
        // 0, keeping a starved consumer counted). Registering again here would double-count
        // every drawer and halve the offered slices.
        float dt = Time.deltaTime;
        int N = sources.Count;
        if (N == 0) return false;

        var alloc = new float[N];
        var cap = new float[N];
        var capped = new bool[N];
        float totalCap = 0f;
        for (int i = 0; i < N; i++)
        {
            var s = sources[i];
            if (s == null) { capped[i] = true; continue; }
            float c = s.PeekMaxDraw(dt);
            if (c <= 1e-6f) { capped[i] = true; continue; }
            cap[i] = c;
            totalCap += c;
        }
        if (totalCap + 1e-5f < cost) return false;

        float toAllocate = cost;
        int safety = N + 2;
        while (toAllocate > 1e-6f && safety-- > 0)
        {
            int activeN = 0;
            for (int i = 0; i < N; i++) if (!capped[i]) activeN++;
            if (activeN == 0) break;
            float share = toAllocate / activeN;
            bool pinnedAny = false;
            for (int i = 0; i < N; i++)
            {
                if (capped[i]) continue;
                float room = cap[i] - alloc[i];
                if (room <= share + 1e-6f)
                {
                    alloc[i] += room;
                    toAllocate -= room;
                    capped[i] = true;
                    pinnedAny = true;
                }
            }
            if (!pinnedAny)
            {
                for (int i = 0; i < N; i++)
                {
                    if (capped[i]) continue;
                    alloc[i] += share;
                    toAllocate -= share;
                }
                break;
            }
        }

        for (int i = 0; i < N; i++)
        {
            if (alloc[i] <= 1e-6f) continue;
            if (sources[i] != null) sources[i].Use(alloc[i]);
        }

        OnUse?.Invoke();
        _onUpdate?.Invoke(Energy);
        return true;
    }

    public void Add(float amount)
    {
        if (amount <= 0f) return;
        RefreshIfDirty();
        if (sources.Count == 0) return;

        int safety = 8;
        while (amount > 1e-5f && safety-- > 0)
        {
            int n = 0;
            for (int i = 0; i < sources.Count; i++)
            {
                if (sources[i] != null && sources[i].Energy < sources[i].MaxEnergy) n++;
            }
            if (n == 0) break;
            float share = amount / n;
            for (int i = 0; i < sources.Count; i++)
            {
                var s = sources[i];
                if (s == null) continue;
                float room = s.MaxEnergy - s.Energy;
                if (room <= 0f) continue;
                float give = Mathf.Min(room, share);
                s.Add(give);
                amount -= give;
            }
        }

        _onUpdate?.Invoke(Energy);
    }
}
