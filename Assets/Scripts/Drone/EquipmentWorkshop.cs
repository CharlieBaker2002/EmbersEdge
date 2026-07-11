using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Produces claimable drone equipment as STOCK on the building (no physical item until a drone
/// dies or swaps roles). Two prefabs share this class: the Drill Forge (produces Drill) and the
/// Cargoloft (produces Bag). Production is an orb-cost slot in the building UI; drones dragged
/// onto the workshop take a unit of stock, or wait beside it until one is forged.
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
    }

    void ProduceOne()
    {
        stock++;
        ServeWaiters();
        UpdateStockText();
    }

    void UpdateStockText()
    {
        if (stockText == null) return;
        stockText.text = stock.ToString();
        stockText.color = GS.ColFromEra() * 1.25f;
    }

    public Vector2 WaitPoint => transform.position;

    /// <summary>Drag-assignment entry: kit the drone now, or queue it beside the workshop.</summary>
    public void TryClaim(Drone d)
    {
        if (d == null || d.equipment == produces) return;
        if (stock > 0)
        {
            stock--;
            UpdateStockText();
            waiting.Remove(d);
            d.TakeEquipment(produces);
        }
        else if (!waiting.Contains(d))
        {
            waiting.Add(d);
            d.WaitAt(this);
        }
    }

    public void LeaveQueue(Drone d) => waiting.Remove(d);

    void ServeWaiters()
    {
        for (int k = 0; k < waiting.Count && stock > 0; k++)
        {
            var d = waiting[k];
            if (d == null || d.state != Drone.State.WaitingAtStation)
            {
                waiting.RemoveAt(k);
                k--;
                continue;
            }
            stock--;
            waiting.RemoveAt(k);
            k--;
            d.TakeEquipment(produces);
        }
        UpdateStockText();
    }
}
