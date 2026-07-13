using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Produces claimable drone equipment as STOCK on the building (no physical item until a drone
/// dies or swaps roles). Two prefabs share this class: the Drill Forge (produces Drill) and the
/// Cargoloft (produces Bag). Production is an orb-cost slot in the building UI; drones dragged
/// onto the workshop fly to it and take a unit of stock on arrival, waiting beside it until
/// one is forged if the shelf is empty.
/// </summary>
public class EquipmentWorkshop : Building
{
    [Header("Workshop")]
    public DroneEquipment produces = DroneEquipment.Drill;
    public int stock;
    public int maxStock = 4;
    [Tooltip("Orb cost per unit of equipment (w/g/b/r).")]
    public int[] itemCost = { 6, 0, 0, 0 };
    public Sprite slotSprite;

    readonly List<Drone> waiting = new List<Drone>();
    TextMeshPro stockText;
    System.Action newDay;

    // Kits destroyed with their dead drones, per DroneEquipment kind. The producing workshop
    // forges free replacements at the start of the next day; losses queue up across days if
    // no workshop of that kind stands yet.
    static readonly int[] pendingLosses = new int[4];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => System.Array.Clear(pendingLosses, 0, pendingLosses.Length);

    public static void QueueReplacement(DroneEquipment kind)
    {
        if (kind == DroneEquipment.Drill || kind == DroneEquipment.Bag) pendingLosses[(int)kind]++;
    }

    public override void Start()
    {
        base.Start();
        if (slotSprite == null)
        {
            var frames = DroneManager.LoadStripNumeric(produces == DroneEquipment.Drill ? "DroneDrill" : "DroneBag");
            if (frames.Length > 0) slotSprite = frames[0];
        }
        AddSlot(itemCost, produces == DroneEquipment.Drill ? "Forge Drill" : "Weave Bag", slotSprite,
            false, ProduceOne, false, null, () => stock < maxStock);
    }

    protected override void BEnable()
    {
        DroneManager.Ensure();
        if (stockText == null && UIManager.i != null)
        {
            stockText = Instantiate(UIManager.i.numText, transform.position + new Vector3(0f, 0.6f, 0f),
                Quaternion.identity, transform);
            stockText.gameObject.SetActive(true);
        }
        UpdateStockText();
        newDay ??= ReplaceLostKits;
        if (SpawnManager.instance != null) SpawnManager.instance.OnNewDay += newDay;
    }

    protected override void BDisable()
    {
        base.BDisable();
        if (SpawnManager.instance != null) SpawnManager.instance.OnNewDay -= newDay;
    }

    /// <summary>Day-start: forge free replacements for every kit of this kind that died with
    /// its drone yesterday. Restock() handles a full shelf by dropping a physical item.</summary>
    void ReplaceLostKits()
    {
        while (pendingLosses[(int)produces] > 0)
        {
            pendingLosses[(int)produces]--;
            Restock();
        }
    }

    void ProduceOne()
    {
        stock++;
        UpdateStockText();
        // Waiting drones hovering at the workshop pick this up on their next brain tick
        // (they poll TryHandOver while at the WaitPoint).
    }

    void UpdateStockText()
    {
        if (stockText == null) return;
        stockText.text = stock.ToString();
        stockText.color = GS.ColFromEra() * 1.25f;
    }

    public Vector2 WaitPoint => transform.position;

    /// <summary>Stock not yet spoken for by a drone already flying in — the colony job board's
    /// dispatch gate, so exactly as many drones come as there are kits on the shelf.</summary>
    public bool HasUnclaimedStock => enabled && stock > waiting.Count;

    /// <summary>Drag-assignment entry: the drone flies to the workshop and collects its kit
    /// there (TryHandOver on arrival) — never an instant remote pickup.</summary>
    public void TryClaim(Drone d)
    {
        if (d == null || d.equipment == produces) return;
        if (!waiting.Contains(d)) waiting.Add(d);
        d.WaitAt(this);
    }

    /// <summary>Arrival hand-over: the drone is physically at the workshop, so give it a unit
    /// of stock if there is one (otherwise it keeps hovering in the queue).</summary>
    public void TryHandOver(Drone d)
    {
        if (d == null) return;
        if (d.equipment == produces || stock > 0)
        {
            if (d.equipment != produces)
            {
                stock--;
                UpdateStockText();
            }
            waiting.Remove(d);
            d.TakeEquipment(produces);
        }
    }

    public void LeaveQueue(Drone d) => waiting.Remove(d);

    /// <summary>A drone hands a unit of kit back (swap return). A full shelf drops it as a
    /// physical item beside the workshop instead of vanishing it.</summary>
    public void Restock()
    {
        if (stock < maxStock)
        {
            stock++;
            UpdateStockText();
        }
        else
        {
            DroneEquipmentItem.Spawn(produces, transform.position + GS.RandCircle(0.3f, 0.7f));
        }
    }

    /// <summary>The live workshop that owns this kind of kit, nearest to <paramref name="from"/> —
    /// where a swapping drone returns its old kit. Null when none stands.</summary>
    public static EquipmentWorkshop NearestProducing(DroneEquipment kind, Vector2 from)
    {
        EquipmentWorkshop best = null;
        float bestSqr = float.MaxValue;
        foreach (Building b in buildings)
        {
            if (b is not EquipmentWorkshop ws || !ws.enabled || ws.produces != kind) continue;
            float d = ((Vector2)ws.transform.position - from).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; best = ws; }
        }
        return best;
    }
}
