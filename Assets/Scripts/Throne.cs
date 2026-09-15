using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The Throne — the base's heart at the origin; its death loses the game. Since 2026-09-14
/// (user call) it is also:
///   • a 2×2 world-unit obstruction (8×8 cells) EXCEPT its four corner cells, which stay
///     buildable (<see cref="OccupiesFootprintCell"/> — the "2×2 circle"); the body itself is
///     the authored PhysicCircle at the centre;
///   • the way the STORE IN THE VERY CENTRE (the Small Ember Store at the origin, itself a grid
///     source since the same day) reaches the outside: the Throne is a grid source across its
///     whole block that forwards that store's energy, so every build tile touching the block
///     is powered by it. Pylons may cable onto the centre too — but only the small one
///     (EnergyPylon.ValidateTarget).
/// </summary>
public class Throne : Building, IOnDeath, IEnergyAccumulator
{
    [Tooltip("How far from the Throne's centre the centre store may sit and still be the one it forwards.")]
    public float centreStoreReach = 0.6f;

    EmberStoreBuilding centreStore;
    bool storeLooked;
    Action<float> fwdUpdate;
    Action fwdUse;

    public event Action<float> OnUpdate;
    public event Action OnUse;

    /// <summary>The Ember Store standing on the Throne (resolved once both have started).</summary>
    public EmberStoreBuilding CentreStore
    {
        get
        {
            if (centreStore != null || storeLooked) return centreStore;
            var list = EnergyManager.i != null ? EnergyManager.i.emberStores : null;
            if (list == null) return null;
            float best = centreStoreReach * centreStoreReach;
            for (int k = 0; k < list.Count; k++)
            {
                var s = list[k];
                if (s == null) continue;
                float d = ((Vector2)s.transform.position - (Vector2)transform.position).sqrMagnitude;
                if (d <= best) { best = d; centreStore = s; }
            }
            if (centreStore != null)
            {
                storeLooked = true;
                fwdUpdate = e => OnUpdate?.Invoke(e);
                fwdUse = () => OnUse?.Invoke();
                centreStore.OnUpdate += fwdUpdate;
                centreStore.OnUse += fwdUse;
            }
            return centreStore;
        }
    }

    // ------------------------------------------------------------------ IEnergyAccumulator (forwarding the centre store)

    public float Energy => CentreStore != null ? centreStore.Energy : 0f;
    public float MaxEnergy => CentreStore != null ? centreStore.MaxEnergy : 0f;
    public float DrawRate => CentreStore != null ? centreStore.DrawRate : 0f;
    public float MaxDrawThisFrame(float dt) => CentreStore != null ? centreStore.MaxDrawThisFrame(dt) : 0f;
    public float PeekMaxDraw(float dt) => CentreStore != null ? centreStore.PeekMaxDraw(dt) : 0f;
    public bool Use(float cost) => CentreStore != null && centreStore.Use(cost);
    public void Add(float amount) { if (CentreStore != null) centreStore.Add(amount); }

    // ------------------------------------------------------------------ footprint

    /// <summary>2×2 obstructed except the very corner points (user rule 2026-09-14).</summary>
    public override bool OccupiesFootprintCell(int x, int y, Vector2Int cells)
        => !((x == 0 || x == cells.x - 1) && (y == 0 || y == cells.y - 1));

    public override void Start()
    {
        if (RefreshManager.i.ARENAMODE)
        {
            return;
        }
        base.Start();
    }

    protected override void BEnable()
    {
        EnergyManager.i?.RegisterSource(this, anchorCell, gridSize);   // the centre store, felt all round the block
    }

    protected override void BDisable()
    {
        EnergyManager.i?.UnregisterSource(this, anchorCell, gridSize);
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        if (centreStore != null)
        {
            if (fwdUpdate != null) centreStore.OnUpdate -= fwdUpdate;
            if (fwdUse != null) centreStore.OnUse -= fwdUse;
        }
    }

    public override void OnDeath()
    {
        if (!TutorialManager.tutorial)
        {
            if (RefreshManager.i.LOSSPROTECTION)
            {
                Debug.LogWarning("loss protection");
            }
            else
            {
                PortalScript.i.Lose();
            }
        }
        else
        {
            BuildingTutorial.defeated = true;
        }
    }
}
