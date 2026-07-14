using System;

/// <summary>
/// Anything that holds and dispenses energy: a single Battery, an EnergyPad
/// aggregating its 4 batteries, or a building's BuildingPower view that aggregates
/// adjacent pads. Consumers (turrets, generators, factories) talk only to this
/// interface so the storage topology can change without touching them.
/// </summary>
public interface IEnergyAccumulator
{
    float Energy { get; }
    float MaxEnergy { get; }
    /// <summary>Energy/sec this accumulator can supply to downstream consumers. Relays divide by downstream contention.</summary>
    float DrawRate { get; }

    /// <summary>
    /// Maximum amount this source can deliver in a single frame of length <paramref name="dt"/>
    /// — DrawRate*dt for plain sources, plus any unused instabuffer for sources that have one
    /// (batteries, pylon surge pools). Already accounts for any draws done earlier this frame.
    /// </summary>
    float MaxDrawThisFrame(float dt);

    /// <summary>
    /// Same number as <see cref="MaxDrawThisFrame"/> but WITHOUT registering as a drawing
    /// consumer in the source's fair-share accounting. UI/diagnostic reads (gauge bars, the
    /// pylon surge-debit baseline) MUST use this — polling MaxDrawThisFrame every frame would
    /// halve the slice offered to real consumers.
    /// </summary>
    float PeekMaxDraw(float dt);

    /// <summary>Try to draw cost. Returns false (and changes nothing) if not enough.</summary>
    bool Use(float cost);

    /// <summary>Deposit energy. Excess is dropped on the floor.</summary>
    void Add(float amount);

    /// <summary>Fires after Energy changes. Argument is the new total.</summary>
    event Action<float> OnUpdate;

    /// <summary>Fires after a successful Use().</summary>
    event Action OnUse;
}
