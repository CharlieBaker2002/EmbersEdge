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

    /// <summary>Batteries spawned when first built. Pads and hubs are EMPTY housings now —
    /// batteries come from the Battery Station (which overrides this to stock itself).</summary>
    protected virtual int InitialBatteryCount => 0;

    public readonly Battery[] slots = new Battery[4];
    private bool spawned;

    /// <summary>Usable slot count — the hub authors a single slot transform, pads four.</summary>
    public int SlotCapacity => Mathf.Min(slots.Length, slotTransforms.Length);

    public int SlottedCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < slots.Length; i++) if (slots[i] != null) n++;
            return n;
        }
    }

    /// <summary>Batteries en route on a drone, bound for this pad (distribution/station claims).</summary>
    [HideInInspector] public int inboundBatteries;

    /// <summary>Player promise: manually ADDING a battery pins the pad's count as a minimum the
    /// distribution must keep stocked, spares or not. Removing never changes the pin — re-adding
    /// re-stamps it at the new count.</summary>
    [HideInInspector] public int pinnedMin;

    public event Action<float> OnUpdate;
    public event Action OnUse;

    public bool hub = false;

    /// <summary>Pads/hubs show the aggregate instabuffer of their slotted batteries.</summary>
    public override bool ShowsSurgeBar => true;

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

    /// <summary>Side-effect-free MaxDrawThisFrame (no fair-share query registration) — gauge bars only.</summary>
    public virtual float PeekMaxDraw(float dt)
    {
        float s = 0f;
        for (int i = 0; i < slots.Length; i++)
        {
            var b = slots[i];
            if (b != null) s += b.PeekMaxDraw(dt);
        }
        return s;
    }

    /// <summary>Current aggregate instabuffer across slotted batteries — the surge bar's fill.</summary>
    public float SurgeNow
    {
        get
        {
            float s = 0f;
            for (int i = 0; i < slots.Length; i++) if (slots[i] != null) s += slots[i].InstaBuffer;
            return s;
        }
    }

    /// <summary>Max aggregate instabuffer across slotted batteries — the surge bar's stripe count.</summary>
    public float SurgeMax
    {
        get
        {
            float s = 0f;
            for (int i = 0; i < slots.Length; i++) if (slots[i] != null) s += slots[i].InstaBufferMaxValue;
            return s;
        }
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

    /// <summary>Rotation re-faced (and possibly moved) the pad. HubAccessibleFrom reads
    /// transform.up live, but consumers only re-resolve on OnPadsChanged — so re-stamp the source
    /// claim: pull it off the pre-rotation cells, register the new footprint. Both calls fire
    /// OnPadsChanged, which is what flips the hub's forward column to the new facing. Ghost/dead
    /// pads (never registered — BEnable hasn't run or BDisable already ran) stay unregistered.</summary>
    protected override void OnRotated(Vector2Int oldAnchor, Vector2Int oldSize)
    {
        if (EnergyManager.i == null) return;
        EnergyManager.i.UnregisterPadArea(this, oldAnchor, oldSize);
        if (builtYet && enabled) EnergyManager.i.RegisterPad(this);
        // the housing turned but the batteries riding in it stay upright
        foreach (var b in slots)
            if (b != null) b.transform.rotation = Quaternion.identity;
    }

    void SpawnInitialBatteries()
    {
        if (spawned || batteryPrefab == null) return;
        spawned = true;
        int count = Mathf.Clamp(InitialBatteryCount, 0, slots.Length);
        for (int i = 0; i < count && i < slotTransforms.Length; i++)
        {
            if (slotTransforms[i] == null) continue;
            var b = Instantiate(batteryPrefab, slotTransforms[i].position, Quaternion.identity);
            SlotInternal(b, i);
        }
    }

    /// <summary>Try to place a loose battery into the first empty slot. Returns false if all full.
    /// Bounded by slotTransforms — the hub authors a single slot, so its array is shorter than
    /// the fixed 4-wide slots[] (indexing past it threw once slot 0 was taken).
    /// <paramref name="playerAction"/> distinguishes the player's hand from drone logistics:
    /// a manual add PINS the pad's current count as a distribution minimum.</summary>
    public bool TrySlotBattery(Battery b, bool playerAction = true)
    {
        if (b == null) return false;
        for (int i = 0; i < slots.Length && i < slotTransforms.Length; i++)
        {
            if (slots[i] == null && slotTransforms[i] != null)
            {
                SlotInternal(b, i);
                if (this is not BatteryStation)
                {
                    if (playerAction) pinnedMin = Mathf.Min(SlottedCount, SlotCapacity);
                    // drone-placed: the battery's station trip for the current swap window is
                    // spent — it holds this pad until the next window (day tick / homecoming)
                    else b.padSwapWindow = BatteryStation.SwapWindow;
                }
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
        // slotted batteries always sit upright, whatever the pad's facing or however the
        // battery arrived (drone-carried ones inherit the drone's spin)
        b.transform.rotation = Quaternion.identity;
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

    /// <summary>Buildings ghost-tint reddish while awaiting repair, which makes the pad's battery
    /// housing (and the batteries sitting in it) read as dead-red. A pad/hub is a passive store, so
    /// give it a neutral "powered-down" dim instead: cancel the base's red tint tween on the body and
    /// keep the slotted batteries at their normal colour.</summary>
    public override void OnDeath()
    {
        base.OnDeath();
        if (sr != null)
        {
            LeanTween.cancel(sr.gameObject);   // kill the reddish ghost tween base just started
            sr.color = new Color(0.55f, 0.55f, 0.55f, 0.6f);
        }
        foreach (var b in slots)
        {
            if (b == null || b.sr == null) continue;
            LeanTween.cancel(b.sr.gameObject);
            b.sr.color = Color.white;
        }
    }

    /// <summary><paramref name="playerAction"/>: the player lifting a battery LOWERS the pad's
    /// pin to what's left — otherwise the drones shove the battery straight back where it was.
    /// Drone unslots (charge hauls, swaps) never touch the pin.</summary>
    public virtual void UnslotBattery(Battery b, bool playerAction = false)
    {
        if (b == null) return;
        if (b.padSlot >= 0 && b.padSlot < slots.Length && slots[b.padSlot] == b)
        {
            slots[b.padSlot] = null;
        }
        b.OnUpdate -= ForwardUpdate;
        b.pad = null;
        b.padSlot = -1;
        if (playerAction && this is not BatteryStation)
            pinnedMin = Mathf.Min(pinnedMin, SlottedCount);
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
