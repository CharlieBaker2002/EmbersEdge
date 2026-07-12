using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A piece of drone kit lying on the ground — dropped when a drone dies or swaps roles.
/// Haulable by bag drones (payload cargo). Built entirely in code; no prefab needed.
/// </summary>
public class DroneEquipmentItem : MonoBehaviour
{
    public static readonly List<DroneEquipmentItem> all = new List<DroneEquipmentItem>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => all.Clear();

    public DroneEquipment kind;
    public SpriteRenderer sr;
    /// <summary>Spare drone flying in to take this kit (colony self-assignment) — one at a time.</summary>
    [HideInInspector] public Drone claimedBy;

    void OnEnable() => all.Add(this);

    void OnDisable() => all.Remove(this);

    public static DroneEquipmentItem Spawn(DroneEquipment kind, Vector3 pos)
    {
        if (kind != DroneEquipment.Drill && kind != DroneEquipment.Bag) return null;
        var go = new GameObject("DroneEquipment_" + kind);
        go.transform.SetParent(GS.FindParent(GS.Parent.loot));
        go.transform.position = pos;
        var item = go.AddComponent<DroneEquipmentItem>();
        item.kind = kind;
        item.sr = go.AddComponent<SpriteRenderer>();
        var frames = DroneManager.LoadStripNumeric(kind == DroneEquipment.Drill ? "DroneDrill" : "DroneBag");
        if (frames.Length > 0) item.sr.sprite = frames[0];
        return item;
    }
}
