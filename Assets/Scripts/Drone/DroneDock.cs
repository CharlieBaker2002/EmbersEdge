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

    /// <summary>ONE charge cycle per drone per day. The dock still charges at any hour — but only
    /// drones that haven't drawn their day's charge yet (missed the overnight sweep, starved grid,
    /// or a full drone that saved its cycle). A drone that spent its recharge is done: it sleeps
    /// in its slot until the next day's sweep.</summary>
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
            if (d.ChargedToday) continue;   // today's cycle already spent — lights out till dawn
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
            else if (d.energy < 0.999f && !d.ChargedToday)   // a race-stamped drone doesn't bill twice
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

    // ------------------------------------------------------------------ roster UI

    public override void OnClick()
    {
        // Rebuild the roster fresh on every open — residents swap kit / die / rebuild between clicks.
        if (enabled && UIParent != null && !UIParent.activeInHierarchy) RefreshEquipmentTiles();
        base.OnClick();
    }

    /// <summary>Dock roster panel: one tile per resident slot showing that drone's kit. Clicking
    /// a kit tile SCRAPS the kit outright (Drone.DestroyEquipment — no drop, no refund); kitless
    /// and rebuilding slots just flash red. Tiles are snapshots — a drone that swapped kit since
    /// the panel opened refuses the click (optParam) instead of scrapping the wrong thing.</summary>
    void RefreshEquipmentTiles()
    {
        for (int k = 0; k < tiles.Count; k++)
            if (tiles[k] != null) Destroy(tiles[k].gameObject);
        tiles.Clear();
        int[] free = { 0, 0, 0, 0 };
        for (int k = 0; k < residents.Count; k++)
        {
            Drone d = residents[k];
            if (d == null)
            {
                AddSlot(free, "Rebuilding", KitIcon(DroneEquipment.None), false, () => { },
                    optionalParameter: () => false);
            }
            else
            {
                Drone dd = d;
                DroneEquipment kind = d.equipment;
                AddSlot(free,
                    kind == DroneEquipment.None ? "No Kit" : "Destroy " + kind,
                    KitIcon(kind), false,
                    () =>
                    {
                        if (dd != null) dd.DestroyEquipment();
                        RefreshEquipmentTiles();   // panel stays open — show the now-empty hands
                    },
                    optionalParameter: () => kind != DroneEquipment.None && dd != null && dd.equipment == kind);
            }
            tiles[^1].SetTextN(k + 1);
        }
        UpdateUI();
    }

    // kit icons for the roster tiles, loaded once and shared by every dock (null-check re-loads
    // after a domain-reload-off play-stop unloads them)
    static Sprite drillIcon, bagIcon, droneIcon;

    static Sprite KitIcon(DroneEquipment kind)
    {
        if (droneIcon == null)
        {
            var s = DroneManager.LoadStripNumeric("DroneDrill");
            drillIcon = s.Length > 0 ? s[0] : null;
            s = DroneManager.LoadStripNumeric("DroneBag");
            bagIcon = s.Length > 0 ? s[0] : null;
            s = DroneManager.LoadStripNumeric("Drone");
            droneIcon = s.Length > 0 ? s[0] : null;
        }
        // pilot kit has no sprite of its own and an empty slot shows the drone itself
        return kind == DroneEquipment.Drill ? drillIcon
            : kind == DroneEquipment.Bag ? bagIcon
            : droneIcon;
    }
}
