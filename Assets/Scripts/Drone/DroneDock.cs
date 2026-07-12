using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Home base for a squad of drones. Spawns its residents when first built and recharges them
/// every new day, paying 1 energy per drone from the adjacent grid (BuildingPower.DrawEnergy —
/// an over-time draw, so instabuffer caps never fail a whole day's recharge outright). A dead
/// resident is rebuilt at the same daily price. An unpowered dock recharges nobody: its drones
/// simply stay flat until the grid can pay again.
/// </summary>
public class DroneDock : Building
{
    [Header("Drone Dock")]
    public GameObject dronePrefab;
    public int residentCount = 4;
    [Tooltip("Daily energy per resident: recharge a living drone, or rebuild a dead one.")]
    public float energyPerDronePerDay = 1f;
    [Tooltip("Hover spots around the dock, one per resident (wraps if fewer).")]
    public Vector3[] slotOffsets =
    {
        new Vector3(-0.4f, -0.4f), new Vector3(0.4f, -0.4f),
        new Vector3(-0.4f, 0.4f), new Vector3(0.4f, 0.4f),
    };

    readonly List<Drone> residents = new List<Drone>();
    bool populated;
    Action newDay;
    readonly List<Coroutine> pendingDraws = new List<Coroutine>();
    int outstanding;
    Coroutine overlayCo;
    readonly HashSet<Drone> charging = new HashSet<Drone>();   // trickle draws in flight
    float trickleT;

    public Vector2 SlotPosition(int slot)
        => transform.position + slotOffsets[Mathf.Abs(slot) % slotOffsets.Length];

    protected override void BEnable()
    {
        DroneManager.Ensure();
        newDay ??= DailyRecharge;
        if (SpawnManager.instance != null)
        {
            SpawnManager.instance.OnNewDay -= newDay;   // idempotent across repair cycles
            SpawnManager.instance.OnNewDay += newDay;
        }
        if (!populated)
        {
            populated = true;
            for (int k = 0; k < residentCount; k++)
            {
                residents.Add(null);
                SpawnResident(k);
            }
        }
    }

    protected override void BDisable()
    {
        if (SpawnManager.instance != null && newDay != null)
            SpawnManager.instance.OnNewDay -= newDay;
        StopPendingDraws();
    }

    void SpawnResident(int slot)
    {
        if (dronePrefab == null) return;
        var g = Instantiate(dronePrefab,
            (Vector3)SlotPosition(slot) + GS.RandCircle(0.05f, 0.2f),
            Quaternion.identity, GS.FindParent(GS.Parent.allies));
        var d = g.GetComponent<Drone>();
        d.dock = this;
        d.dockSlot = slot;
        d.Recharge();
        residents[slot] = d;
        g.SetActive(true);
    }

    /// <summary>The dock is a charger ALL day, not just at daybreak: a resident that comes home
    /// spent starts a fresh grid draw as soon as it's sitting in its slot (same tariff as the
    /// daily top-up — DailyRecharge stays as the overnight sweep for drones that die out or
    /// come home after the grid ran dry).</summary>
    void Update()
    {
        if (!enabled || !builtYet) return;
        if ((trickleT -= Time.deltaTime) > 0f) return;
        trickleT = 1f;
        for (int k = 0; k < residents.Count; k++)
        {
            Drone d = residents[k];
            if (d == null || charging.Contains(d)) continue;
            if (d.energy >= 0.999f || d.transform.InDungeon()) continue;
            if (d.state != Drone.State.Docked) continue;
            if (((Vector2)d.transform.position - SlotPosition(k)).sqrMagnitude > 1.5f * 1.5f) continue;
            Drone dd = d;
            charging.Add(dd);
            BeginDraw(() => { charging.Remove(dd); if (dd != null) dd.Recharge(); });
        }
    }

    public void NotifyResidentDied(Drone d)
    {
        int idx = residents.IndexOf(d);
        if (idx >= 0) residents[idx] = null;
    }

    /// <summary>OnNewDay: pay 1 energy per resident — recharge the living, rebuild the dead.
    /// Draws that couldn't complete (unpowered grid) are abandoned at the next day tick, so a
    /// starved dock never banks IOUs.</summary>
    void DailyRecharge()
    {
        if (!enabled) return;
        StopPendingDraws();
        for (int k = 0; k < residents.Count; k++)
        {
            int slot = k;
            Drone d = residents[k];
            if (d == null)
                BeginDraw(() => SpawnResident(slot));
            else if (d.energy < 0.999f)
                BeginDraw(() => { if (d != null) d.Recharge(); });
        }
    }

    void BeginDraw(Action done)
    {
        outstanding++;
        pendingDraws.Add(StartCoroutine(Power.DrawEnergy(energyPerDronePerDay, _ =>
        {
            outstanding--;
            done();
        })));
        overlayCo ??= StartCoroutine(OverlayWhileDrawing());
    }

    IEnumerator OverlayWhileDrawing()
    {
        while (outstanding > 0)
        {
            ReportEnergyDraw(energyPerDronePerDay);
            yield return null;
        }
        ClearEnergyStatus();
        overlayCo = null;
    }

    void StopPendingDraws()
    {
        foreach (var c in pendingDraws)
            if (c != null) StopCoroutine(c);
        pendingDraws.Clear();
        outstanding = 0;
        if (overlayCo != null)
        {
            StopCoroutine(overlayCo);
            overlayCo = null;
            ClearEnergyStatusImmediate();
        }
    }
}
