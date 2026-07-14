using UnityEngine;

/// <summary>
/// 1-slot battery receptacle that feeds ONLY surge credit (instabuffer) to the pylon grid —
/// tethered like a generator (pylon dragged onto it) or adjacent to a pylon. The slotted
/// battery contributes NO stored energy, is never drained of charge, and drones leave it
/// alone; its charge is entirely irrelevant here. What it passes on is its instabuffer:
/// pure throughput credit that lets bursts through the pylon exceed the generators' rate
/// (the energy itself still comes out of generator banks — see EnergyPylon.DebitSurge).
///
/// Every IEnergyAccumulator member reads as empty so ordinary adjacent consumers can never
/// see or draw from a node — pylons reach the credit through the dedicated SurgeCredit /
/// DebitSurge members instead. Registers as a pad (BEnable → RegisterPad) so hand-dropping
/// a battery snaps into the slot and pylons discover it via adjacency.
///
/// No surge bar on the node itself: the battery's own percent-sprite + coil animate the
/// instabuffer state (see Battery.UpdateVisual's capacitor mode).
/// </summary>
public class CapacitorNode : EnergyPad
{
    public override float Energy => 0f;
    public override float MaxEnergy => 0f;
    public override float DrawRate => 0f;
    public override float MaxDrawThisFrame(float dt) => 0f;
    public override float PeekMaxDraw(float dt) => 0f;
    public override bool Use(float cost) => cost <= 0f;
    public override void Add(float amount) { }

    /// <summary>No bar on the node — the slotted battery's own art animates the insta state.</summary>
    public override bool ShowsSurgeBar => false;

    /// <summary>Surge credit currently available — Σ slotted battery instabuffer, charge ignored.</summary>
    public float SurgeCredit
    {
        get
        {
            float s = 0f;
            for (int i = 0; i < slots.Length; i++) if (slots[i] != null) s += slots[i].InstaBuffer;
            return s;
        }
    }

    /// <summary>Ceiling of the credit pool — Σ slotted battery instaBufferMax.</summary>
    public float SurgeCreditMax
    {
        get
        {
            float s = 0f;
            for (int i = 0; i < slots.Length; i++) if (slots[i] != null) s += slots[i].InstaBufferMaxValue;
            return s;
        }
    }

    /// <summary>
    /// Pylon-only: spend surge (real energy banked from the grid) for a burst that exceeded
    /// the upstream rate budget. Drains battery instabuffers (never their inert charge).
    /// Returns what was actually debited.
    /// </summary>
    public float DebitSurge(float amount)
    {
        float taken = 0f;
        for (int i = 0; i < slots.Length && amount - taken > 1e-6f; i++)
        {
            var b = slots[i];
            if (b == null) continue;
            taken += b.DebitInsta(amount - taken);
        }
        if (taken > 0f) RaiseOnUpdate(0f);
        return taken;
    }

    /// <summary>Room left in the pool — how much grid energy the node could still bank.</summary>
    public float SurgeDeficit
    {
        get
        {
            float s = 0f;
            for (int i = 0; i < slots.Length; i++) if (slots[i] != null) s += slots[i].InstaDeficit;
            return s;
        }
    }

    /// <summary>Combined recharge rate of the slotted batteries (drawRate each) — caps how fast
    /// the pylon may push spare grid energy into this node per second.</summary>
    public float SurgeChargeRate
    {
        get
        {
            float s = 0f;
            for (int i = 0; i < slots.Length; i++) if (slots[i] != null) s += slots[i].drawRate;
            return s;
        }
    }

    /// <summary>Pylon-only: bank real spare grid energy into the battery pools. Returns what fit.</summary>
    public float ChargeSurge(float amount)
    {
        float taken = 0f;
        for (int i = 0; i < slots.Length && amount - taken > 1e-6f; i++)
        {
            var b = slots[i];
            if (b == null) continue;
            taken += b.ChargeInsta(amount - taken);
        }
        if (taken > 0f) RaiseOnUpdate(0f);
        return taken;
    }
}
