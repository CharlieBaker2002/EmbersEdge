using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sits on a vehicle factory (ClawBot Factory, Fighter Ship Factory). Tracks the hulls the
/// factory spawns and routes drones dragged onto the factory into pilot seats: the oldest
/// under-crewed hull takes them; with no seat free the drone WAITS at the factory until the
/// next hull rolls out.
/// </summary>
[RequireComponent(typeof(UnitBuilding))]
public class FactoryPilotStation : MonoBehaviour
{
    readonly List<PilotedVehicle> hulls = new List<PilotedVehicle>();
    readonly List<Drone> waiting = new List<Drone>();
    UnitBuilding factory;

    void Awake()
    {
        factory = GetComponent<UnitBuilding>();
        factory.onUnitSpawned += OnHull;
    }

    void OnDestroy()
    {
        if (factory != null) factory.onUnitSpawned -= OnHull;
    }

    void OnHull(GameObject unit)
    {
        var hull = unit != null ? unit.GetComponent<PilotedVehicle>() : null;
        if (hull == null) return;
        hulls.Add(hull);
        ServeWaiters();
    }

    /// <summary>Drag-assignment entry: seat the drone now, or queue it at the factory.</summary>
    public void AssignPilot(Drone d)
    {
        if (d == null) return;
        var hull = FirstSeat();
        if (hull != null)
        {
            waiting.Remove(d);
            hull.AssignPilot(d);
            d.state = Drone.State.BoardingVehicle;
        }
        else if (!waiting.Contains(d))
        {
            waiting.Add(d);
            d.WaitAt(this);
        }
    }

    public void LeaveQueue(Drone d) => waiting.Remove(d);

    public Vector2 WaitPoint => transform.position;

    PilotedVehicle FirstSeat()
    {
        for (int k = 0; k < hulls.Count; k++)
        {
            if (hulls[k] == null) { hulls.RemoveAt(k); k--; continue; }
            if (hulls[k].NeedsPilots) return hulls[k];
        }
        return null;
    }

    void ServeWaiters()
    {
        for (int k = 0; k < waiting.Count; k++)
        {
            if (waiting[k] == null) { waiting.RemoveAt(k); k--; continue; }
            var hull = FirstSeat();
            if (hull == null) return;
            var d = waiting[k];
            waiting.RemoveAt(k);
            k--;
            hull.AssignPilot(d);
            d.state = Drone.State.BoardingVehicle;
        }
    }
}
