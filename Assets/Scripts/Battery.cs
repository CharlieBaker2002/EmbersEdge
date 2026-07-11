using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Portable energy item. Sits in one of an EnergyPad's 4 slots, gets charged there,
/// can be picked up/dropped by left-click. While held, follows the cursor; left-click
/// again drops it — auto-snapping to the nearest free pad slot if the cursor is over a pad.
/// </summary>
public class Battery : MonoBehaviour, IClickable, IEnergyAccumulator, ISelectable
{
    public float energy;
    public float maxEnergy = 8f;
    [Tooltip("Energy/sec this battery can supply to a consumer drawing through BuildingPower.DrawEnergy.")]
    public float drawRate = 1f;
    [Tooltip("Instant-burst pool on top of drawRate. Drained by single-frame bursts, refills at drawRate when not in use.")]
    public float instaBufferMax = 2f;

    private float instaBuffer;       // current burst credit available
    private float drawnThisFrame;    // accumulated draws in the current frame

    public event Action<float> OnUpdate;
    public event Action OnUse;

    public float Energy => energy;
    public float MaxEnergy => maxEnergy;
    public float DrawRate => energy > 0f ? drawRate : 0f;

    public float MaxDrawThisFrame(float dt)
    {
        if (energy <= 0f) return 0f;
        float budgetRemaining = drawRate * dt + instaBuffer - drawnThisFrame;
        return Mathf.Min(energy, Mathf.Max(0f, budgetRemaining));
    }

    [SerializeField] public SpriteRenderer sr;
    [SerializeField] private bool visual = true;
    [SerializeField] private SpriteRenderer coil;
    [SerializeField] private Sprite[] coil0Sprs;
    [SerializeField] private Sprite[] coil1Sprs;
    [SerializeField] private Sprite[] coil2Sprs;
    [SerializeField] private Sprite[] quickChargeSprs;
    [SerializeField] private Sprite[] energysprs;
    [SerializeField] private Material[] mats;

    private Sprite[][] coilSprs;
    private float energyBuffer;
    private float buffer;
    private float t;

    [HideInInspector] public EnergyPad pad;          // pad we're slotted into, null if loose/held
    [HideInInspector] public int padSlot = -1;       // slot index within that pad

    public static Battery held;                       // global: only one battery can be held at a time
    private Collider2D pickCollider;
    private const float pickRadius = 0.18f;

    /// <summary>Live batteries (bag drones scan this instead of FindObjectsOfType).</summary>
    public static readonly System.Collections.Generic.List<Battery> all = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRegistry() => all.Clear();

    private void OnEnable() => all.Add(this);

    private void OnDisable() => all.Remove(this);

    private void Awake()
    {
        coilSprs = new[] { coil0Sprs, coil1Sprs, coil2Sprs };

        // Need a collider so the click raycast can pick us up.
        pickCollider = GetComponent<Collider2D>();
        if (pickCollider == null)
        {
            var c = gameObject.AddComponent<CircleCollider2D>();
            c.radius = pickRadius;
            c.isTrigger = true;
            pickCollider = c;
        }

        // FocusRouter raycasts on "Ally Buildings" layer.
        int layer = LayerMask.NameToLayer("Ally Buildings");
        if (layer >= 0) gameObject.layer = layer;

        instaBuffer = instaBufferMax;
        Add(8f);
    }

    private void Start()
    {
        // Player-droppable batteries refill to max each new day. Generator-internal storage
        // doesn't go through Battery, so this only touches the visible ones.
        if (SpawnManager.instance != null)
        {
            SpawnManager.instance.OnNewDay += RefillToMax;
        }
    }

    void RefillToMax()
    {
        if (energy < maxEnergy) Add(maxEnergy - energy);
    }

    /// <summary>COST IS +VE. Returns true and drains if there's enough; false otherwise.</summary>
    public bool Use(float cost)
    {
        if (cost <= 0f) return true;
        if (energy < cost) return false;
        energy -= cost;
        buffer -= cost;
        drawnThisFrame += cost;
        OnUpdate?.Invoke(energy);
        OnUse?.Invoke();
        return true;
    }

    public void Add(float amount)
    {
        if (amount <= 0f) return;
        if (energy >= maxEnergy) return;

        float before = energy;
        energy = Mathf.Min(maxEnergy, energy + amount);
        float delta = energy - before;
        buffer += delta;

        if (visual && delta >= 0.9f * maxEnergy)
        {
            StartCoroutine(QuickCharge());
        }

        OnUpdate?.Invoke(energy);
    }

    IEnumerator QuickCharge()
    {
        visual = false;
        sr.material = mats[GS.Era1()];
        yield return StartCoroutine(GS.Animate(sr, quickChargeSprs, 1f));
        visual = true;
    }

    private void Update()
    {
        // Instabuffer tick — runs every frame regardless of visual state (battery is still
        // physically functioning during QuickCharge). End-of-frame reconcile: refill by what
        // we *could* have given at rate (drawRate*dt) minus what was actually drawn this
        // frame, clamped to [0, max]. If draws exceeded rate*dt, buffer drops; if below
        // (idle), buffer climbs back toward max.
        instaBuffer = Mathf.Clamp(instaBuffer + drawRate * Time.deltaTime - drawnThisFrame, 0f, instaBufferMax);
        drawnThisFrame = 0f;

        if (!visual) return;

        energyBuffer = Mathf.Lerp(energyBuffer, energy, Time.deltaTime * 3f);
        buffer = Mathf.Lerp(buffer, 0f, Time.deltaTime);

        if (buffer > 0.1f)
        {
            sr.material = mats[0];
            sr.sprite = GS.PercentParameter(energysprs, energy / maxEnergy);
            t += buffer * Time.deltaTime;
            if (t > 1f) t -= 1f;
            coil.sprite = GS.PercentParameter(coilSprs[GS.era], t);
        }
        else if (buffer < -0.1f)
        {
            sr.material = mats[GS.Era1()];
            sr.sprite = GS.PercentParameter(energysprs, energyBuffer / maxEnergy);
            t += buffer * Time.deltaTime;
            if (t < 0f) t += 1f;
            coil.sprite = GS.PercentParameter(coilSprs[GS.era], t);
        }
        else
        {
            coil.sprite = null;
            sr.material = mats[0];
        }
    }

    public void Charge(float y, float speed)
    {
        StartCoroutine(ICharge(y));
        IEnumerator ICharge(float target)
        {
            while (Mathf.Abs(energy - target) > speed * Time.deltaTime * 3f)
            {
                if (target > energy) Add(speed * Time.deltaTime);
                else Use(speed * Time.deltaTime);
                yield return null;
            }
        }
    }

    // -------- pickup / drop --------

    public void OnClick()
    {
        if (held == this) Drop();
        else if (held == null) Pickup();
        // if held != null && held != this, ignore — the held one will eat the click instead
    }

    void Pickup()
    {
        held = this;
        if (pad != null)
        {
            pad.UnslotBattery(this);
        }
        transform.SetParent(null, true);
        // FocusRouter.DispatchClick already calls Select(this) for ISelectables, but this
        // method is also invoked by the EnergyPad.OnClick forwarding path that bypasses
        // FocusRouter, so we belt-and-brace it here.
        FocusRouter.i?.Select(this);
    }

    void LateUpdate()
    {
        if (held == this)
        {
            Vector2 cursor = IM.controller ? (Vector2)IM.i.CWorldPoint() : IM.i.MousePosition();
            transform.position = new Vector3(cursor.x, cursor.y, transform.position.z);
        }
    }

    void Drop()
    {
        held = null;
        FocusRouter.i?.Deselect(this);

        // Look for a pad under the cursor. If found, snap into a free slot.
        Vector2 cursor = IM.controller ? (Vector2)IM.i.CWorldPoint() : IM.i.MousePosition();
        EnergyPad target = null;
        if (GridManager.i != null && EnergyManager.i != null)
        {
            target = EnergyManager.i.PadAt(GridManager.i.WorldToGrid(cursor));
        }

        if (target != null && target.TrySlotBattery(this))
        {
            return;
        }
        // Loose drop — just sits at cursor world pos. Already positioned by LateUpdate.
    }

    private void OnDestroy()
    {
        if (held == this) held = null;
        if (pad != null) pad.UnslotBattery(this);
        FocusRouter.i?.Deselect(this);
        if (SpawnManager.instance != null)
        {
            SpawnManager.instance.OnNewDay -= RefillToMax;
        }
    }

    // -------- ISelectable --------
    // Esc-while-held triggers FocusRouter.Clear, which calls OnDeselected here.
    // That's the moment we drop, mirroring a user-initiated Drop().
    public void OnSelected() { /* Pickup() already did the work */ }
    public void OnDeselected()
    {
        if (held == this)
        {
            // Selection was cleared externally (e.g. Esc). Behave like a normal drop —
            // try to snap into a pad under the cursor, otherwise leave loose.
            Drop();
        }
    }
}
