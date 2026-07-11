using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Drone gateway between dimensions, buildable at base AND in the dungeon (dungeonBuildable +
/// builtBlasts 0: dungeon placement completes on purchase — there are no orb pylons down there
/// to fly construction orbs in). Base pad #N links to dungeon pad #N (TelepadNetwork, by build
/// order; the number is shown on the pad). Drones are assigned by dragging them onto a BASE pad;
/// they ride the link when the player dives. The dungeon pad doubles as its drones' RALLY POINT.
/// </summary>
public class Telepad : Building
{
    [Header("Telepad")]
    [Tooltip("How many drones can be assigned to this pad.")]
    public int capacity = 4;

    [HideInInspector] public int index = -1;
    readonly List<Drone> assigned = new List<Drone>();
    TextMeshPro padNumber;
    bool dungeonSideCached;
    bool sideCached;

    public bool IsDungeonSide
    {
        get
        {
            if (!sideCached)
            {
                sideCached = true;
                dungeonSideCached = !PathZone.AtBase(transform.position);
            }
            return dungeonSideCached;
        }
    }

    public Telepad Linked => TelepadNetwork.LinkOf(this);

    /// <summary>Built, alive and enabled — a ghost awaiting drone repair is not operational.</summary>
    public bool IsOperational => builtYet && enabled && !IsGhostAwaitingRepair;

    public Vector2 RallyPoint => transform.position;

    protected override void BEnable()
    {
        DroneManager.Ensure();
        if (index < 0) index = TelepadNetwork.Register(this);
        if (padNumber == null && UIManager.i != null)
        {
            padNumber = Instantiate(UIManager.i.numText, transform.position + new Vector3(0f, 0.55f, 0f),
                Quaternion.identity, transform);
            padNumber.gameObject.SetActive(true);
        }
        if (padNumber != null)
        {
            padNumber.text = (index + 1).ToString();
            padNumber.color = GS.ColFromEra() * 1.25f;
        }
        // A dungeon pad coming online mid-dive pulls its assigned drones through right away —
        // the player shouldn't have to surface and re-dive to fetch them.
        if (IsDungeonSide) DroneManager.TryDeployNow();
    }

    public override void OnDestroy()
    {
        TelepadNetwork.Unregister(this);
        // free the mine-cell occupancy dungeon placement recorded
        if (IsDungeonSide && MineField.i != null)
            BM.DungeonOccupancy.Remove(MineField.i.WorldToCell(transform.position));
        foreach (var d in assigned)
            if (d != null && d.assignedPad == this) d.assignedPad = null;
        base.OnDestroy();
    }

    public bool HasRoom
    {
        get
        {
            PruneAssigned();
            return assigned.Count < capacity;
        }
    }

    public bool Assign(Drone d)
    {
        PruneAssigned();
        if (d == null || assigned.Contains(d)) return assigned.Contains(d);
        if (assigned.Count >= capacity) return false;
        assigned.Add(d);
        return true;
    }

    public void Unassign(Drone d) => assigned.Remove(d);

    void PruneAssigned()
    {
        for (int k = assigned.Count - 1; k >= 0; k--)
            if (assigned[k] == null || assigned[k].assignedPad != this) assigned.RemoveAt(k);
    }
}
