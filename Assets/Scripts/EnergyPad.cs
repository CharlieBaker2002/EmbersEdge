using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 1-cell building that holds 4 batteries in fixed slot transforms. Acts as the storage
/// layer adjacent buildings draw from. Aggregates Energy/MaxEnergy across its slots,
/// and Use/Add split equally across qualifying batteries (non-empty for Use, non-full for Add).
/// </summary>
public class EnergyPad : Building, IEnergyAccumulator
{
    [Header("Battery slotting")]
    [SerializeField] private Battery batteryPrefab;
    [SerializeField] private Transform[] slotTransforms = new Transform[4];
    [Tooltip("Batteries spawned at game start. Max slots is fixed at 4; loose batteries can fill the remainder.")]
    [SerializeField] private int initialBatteryCount = 3;

    public readonly Battery[] slots = new Battery[4];
    private bool spawned;

    public event Action<float> OnUpdate;
    public event Action OnUse;

    public bool hub = false;

    public virtual float Energy
    {
        get
        {
            float s = 0f;
            for (int i = 0; i < slots.Length; i++) if (slots[i] != null) s += slots[i].energy;
            return s;
        }
    }

    public virtual float MaxEnergy
    {
        get
        {
            float s = 0f;
            for (int i = 0; i < slots.Length; i++) if (slots[i] != null) s += slots[i].maxEnergy;
            return s;
        }
    }

    /// <summary>Combined energy/sec this pad can supply through DrawEnergy. Empty batteries contribute 0.</summary>
    public virtual float DrawRate
    {
        get
        {
            float s = 0f;
            for (int i = 0; i < slots.Length; i++)
            {
                var b = slots[i];
                if (b != null && b.energy > 0f) s += b.drawRate;
            }
            return s;
        }
    }

    /// <summary>Aggregate per-frame cap across slot batteries (each tracks its own instabuffer).</summary>
    public virtual float MaxDrawThisFrame(float dt)
    {
        float s = 0f;
        for (int i = 0; i < slots.Length; i++)
        {
            var b = slots[i];
            if (b != null) s += b.MaxDrawThisFrame(dt);
        }
        return s;
    }

    /// <summary>Subclasses fire OnUpdate via this so the event field stays private to the declaring class.</summary>
    protected void RaiseOnUpdate(float newEnergy) => OnUpdate?.Invoke(newEnergy);
    protected void RaiseOnUse() => OnUse?.Invoke();

    public override void Start()
    {
        base.Start();
        if (builtYet) SpawnInitialBatteries();
    }

    protected override void BEnable()
    {
        if (!spawned) SpawnInitialBatteries();
        EnergyManager.i?.RegisterPad(this);
    }

    protected override void BDisable()
    {
        EnergyManager.i?.UnregisterPad(this);
    }

    void SpawnInitialBatteries()
    {
        if (spawned || batteryPrefab == null) return;
        spawned = true;
        int count = Mathf.Clamp(initialBatteryCount, 0, slots.Length);
        for (int i = 0; i < count && i < slotTransforms.Length; i++)
        {
            if (slotTransforms[i] == null) continue;
            var b = Instantiate(batteryPrefab, slotTransforms[i].position, Quaternion.identity);
            SlotInternal(b, i);
        }
    }

    /// <summary>Try to place a loose battery into the first empty slot. Returns false if all full.</summary>
    public bool TrySlotBattery(Battery b)
    {
        if (b == null) return false;
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == null && slotTransforms[i] != null)
            {
                SlotInternal(b, i);
                return true;
            }
        }
        return false;
    }

    void SlotInternal(Battery b, int idx)
    {
        slots[idx] = b;
        b.pad = this;
        b.padSlot = idx;
        // worldPositionStays=true so the battery's current world position becomes its
        // localPosition relative to the slot — SnapToSlot then lerps that to (0,0,0).
        b.transform.SetParent(slotTransforms[idx], true);
        StartCoroutine(SnapToSlot(b.transform));
        b.OnUpdate -= ForwardUpdate;
        b.OnUpdate += ForwardUpdate;
        OnUpdate?.Invoke(Energy);
    }

    System.Collections.IEnumerator SnapToSlot(Transform t)
    {
        const float duration = 0.15f;
        Vector3 from = t.localPosition;
        float elapsed = 0f;
        while (elapsed < duration && t != null)
        {
            elapsed += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            t.localPosition = Vector3.Lerp(from, Vector3.zero, k);
            yield return null;
        }
        if (t != null) t.localPosition = Vector3.zero;
    }

    /// <summary>
    /// Click forwarding: if the player's click lands on the pad but a slot battery is
    /// nearer the cursor (the pad's collider is large, the battery's is small), forward
    /// the click to that battery so pickup is reliable. Drops a held battery into a free slot.
    /// </summary>
    public override void OnClick()
    {
        if (Battery.held != null)
        {
            if (TrySlotBattery(Battery.held)) return;
        }
        Vector2 cursor = IM.controller ? (Vector2)IM.i.CWorldPoint() : IM.i.MousePosition();
        Battery near = null;
        float nearestSq = 0.5f * 0.5f;
        for (int i = 0; i < slots.Length; i++)
        {
            var b = slots[i];
            if (b == null) continue;
            float d = ((Vector2)b.transform.position - cursor).sqrMagnitude;
            if (d < nearestSq) { nearestSq = d; near = b; }
        }
        if (near != null) { near.OnClick(); return; }
        base.OnClick();
    }

    public void UnslotBattery(Battery b)
    {
        if (b == null) return;
        if (b.padSlot >= 0 && b.padSlot < slots.Length && slots[b.padSlot] == b)
        {
            slots[b.padSlot] = null;
        }
        b.OnUpdate -= ForwardUpdate;
        b.pad = null;
        b.padSlot = -1;
        OnUpdate?.Invoke(Energy);
    }

    void ForwardUpdate(float _) => OnUpdate?.Invoke(Energy);

    /// <summary>Drains cost equally across non-empty batteries. Returns false (drains nothing) if total < cost.</summary>
    public virtual bool Use(float cost)
    {
        if (cost <= 0f) return true;
        if (Energy < cost) return false;

        // Split equally; if a battery hasn't enough for its share, the remainder spills to the others.
        // Iterate until cost is fully drained or no candidates remain.
        int safety = 8;
        while (cost > 1e-5f && safety-- > 0)
        {
            int n = 0;
            for (int i = 0; i < slots.Length; i++) if (slots[i] != null && slots[i].energy > 0f) n++;
            if (n == 0) break;
            float share = cost / n;
            for (int i = 0; i < slots.Length; i++)
            {
                var b = slots[i];
                if (b == null || b.energy <= 0f) continue;
                float draw = Mathf.Min(b.energy, share);
                b.Use(draw);
                cost -= draw;
            }
        }

        OnUse?.Invoke();
        OnUpdate?.Invoke(Energy);
        return true;
    }

    /// <summary>Adds amount distributed equally across non-full batteries. Excess (all full) is dropped.</summary>
    public virtual void Add(float amount)
    {
        if (amount <= 0f) return;

        int safety = 8;
        while (amount > 1e-5f && safety-- > 0)
        {
            int n = 0;
            for (int i = 0; i < slots.Length; i++) if (slots[i] != null && slots[i].energy < slots[i].maxEnergy) n++;
            if (n == 0) break;
            float share = amount / n;
            for (int i = 0; i < slots.Length; i++)
            {
                var b = slots[i];
                if (b == null || b.energy >= b.maxEnergy) continue;
                float room = b.maxEnergy - b.energy;
                float give = Mathf.Min(room, share);
                b.Add(give);
                amount -= give;
            }
        }

        OnUpdate?.Invoke(Energy);
    }
}
