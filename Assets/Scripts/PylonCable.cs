using System;
using UnityEngine;

/// <summary>
/// Per-connection wrapper around an EnergyPylon. One PylonCable is created per outgoing
/// cable (pylon → tower or pylon → pylon); it's what the downstream's BuildingPower sees
/// as the source, not the pylon directly. The cable itself carries only a RATE cap —
/// burst capacity comes from the pylon's shared surge pool (its own instabuffer plus any
/// attached CapacitorNodes), so N cables off one pylon share one pool instead of each
/// minting their own. The same applies to pylon-to-pylon hops, since the downstream
/// pylon's cableUpstreams holds the cable (not the upstream pylon).
///
/// Energy/Use/Add/events all forward to the wrapped pylon. Per-frame draw accounting
/// lives here and is reset once per frame by <see cref="EnergyPylon.Update"/>.
/// </summary>
public class PylonCable : IEnergyAccumulator
{
    public EnergyPylon pylon;
    public Building target;

    public float perCableRate = 4f;

    private float drawnThisFrame;

    public PylonCable(EnergyPylon pylon, Building target, float perCableRate = 4f)
    {
        this.pylon = pylon;
        this.target = target;
        this.perCableRate = perCableRate;
    }

    public float Energy    => pylon != null ? pylon.Energy : 0f;
    public float MaxEnergy => pylon != null ? pylon.MaxEnergy : 0f;
    /// <summary>The pylon may report a smaller DrawRate (upstream limited); a single cable can't exceed perCableRate.</summary>
    public float DrawRate  => Mathf.Min(pylon != null ? pylon.DrawRate : 0f, perCableRate);

    public float MaxDrawThisFrame(float dt)
    {
        if (pylon == null) return 0f;
        // The upstream call FIRST, unconditionally: it registers this consumer in the surge/
        // rate fair-share counts. Early-outing on a saturated cable before registering would
        // undercount demanders and re-create first-in-update-order starvation one hop up.
        // The cable's own clamp: rate*dt plus the pylon's surge SHARE, net of what this cable
        // already carried this frame — NOT raw pylon.Energy, so N cables off one generator
        // can't multiply its rated output.
        float upstream = pylon.UpstreamMaxDrawThisFrame(dt);
        float budget = perCableRate * dt + pylon.SurgeAvailable - drawnThisFrame;
        return Mathf.Min(upstream, Mathf.Max(0f, budget));
    }

    /// <summary>Side-effect-free MaxDrawThisFrame (no fair-share query registration) — gauge bars only.</summary>
    public float PeekMaxDraw(float dt)
    {
        if (pylon == null) return 0f;
        float budget = perCableRate * dt + pylon.SurgeAvailable - drawnThisFrame;
        if (budget <= 0f) return 0f;
        return Mathf.Min(pylon.UpstreamPeekThisFrame(dt), budget);
    }

    public bool Use(float cost)
    {
        if (cost <= 0f) return true;
        if (pylon == null) return false;
        if (!pylon.Use(cost)) return false;
        drawnThisFrame += cost;
        return true;
    }

    /// <summary>Pylons don't store — Add is a no-op on the cable too.</summary>
    public void Add(float amount) { }

    public event Action<float> OnUpdate
    {
        add    { if (pylon != null) pylon.OnUpdate += value; }
        remove { if (pylon != null) pylon.OnUpdate -= value; }
    }
    public event Action OnUse
    {
        add    { if (pylon != null) pylon.OnUse += value; }
        remove { if (pylon != null) pylon.OnUse -= value; }
    }

    /// <summary>Per-frame reset of the draw accounting (called once per frame by the owning pylon).</summary>
    public void TickFrame()
    {
        drawnThisFrame = 0f;
    }
}
