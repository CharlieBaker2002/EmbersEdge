using System;
using UnityEngine;

/// <summary>
/// Per-connection wrapper around an EnergyPylon. One PylonCable is created per outgoing
/// cable (pylon → tower or pylon → pylon); it's what the downstream's BuildingPower sees
/// as the source, not the pylon directly. Each cable carries its own instabuffer (4 by
/// default) so two cables off the same pylon burst independently — and the same applies
/// to pylon-to-pylon hops, since the downstream pylon's cableUpstreams holds the cable
/// (not the upstream pylon) so the cable's per-frame cap mediates the flow.
///
/// Energy/Use/Add/events all forward to the wrapped pylon. The instabuffer state lives
/// here and is ticked once per frame by <see cref="EnergyPylon.Update"/>.
/// </summary>
public class PylonCable : IEnergyAccumulator
{
    public EnergyPylon pylon;
    public Building target;

    public float perCableRate = 4f;
    public float instaBufferMax = 4f;

    private float instaBuffer;
    private float drawnThisFrame;

    public PylonCable(EnergyPylon pylon, Building target, float perCableRate = 4f, float instaBufferMax = 4f)
    {
        this.pylon = pylon;
        this.target = target;
        this.perCableRate = perCableRate;
        this.instaBufferMax = instaBufferMax;
        instaBuffer = instaBufferMax;
    }

    public float Energy    => pylon != null ? pylon.Energy : 0f;
    public float MaxEnergy => pylon != null ? pylon.MaxEnergy : 0f;
    /// <summary>The pylon may report a smaller DrawRate (upstream limited); a single cable can't exceed perCableRate.</summary>
    public float DrawRate  => Mathf.Min(pylon != null ? pylon.DrawRate : 0f, perCableRate);

    public float MaxDrawThisFrame(float dt)
    {
        if (pylon == null) return 0f;
        float budget = perCableRate * dt + instaBuffer - drawnThisFrame;
        if (budget <= 0f) return 0f;
        return Mathf.Min(pylon.Energy, budget);
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

    /// <summary>Per-frame reconcile: same shape as Battery's tick.</summary>
    public void TickInstaBuffer(float dt)
    {
        instaBuffer = Mathf.Clamp(instaBuffer + perCableRate * dt - drawnThisFrame, 0f, instaBufferMax);
        drawnThisFrame = 0f;
    }
}
