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
    // protected: PulseBattery self-heals a missing coil child (first-gen prefab shipped without one)
    [SerializeField] protected SpriteRenderer coil;
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

    // ---- charging rules ----
    // Batteries are NOT refilled by the dawn tick any more — charge comes from a BatteryStation
    // grinding ore chips (or, for pulse batteries, their own daily self-charge). Each battery
    // accepts at most ONE charge per day; the stamp is the SpawnManager.day the charge landed.
    [HideInInspector] public int lastChargeDay = -1;
    public bool ChargedToday => lastChargeDay == SpawnManager.day;
    public void StampChargedToday() => lastChargeDay = SpawnManager.day;
    /// <summary>Pulse batteries self-charge daily and never visit a station.</summary>
    public virtual bool IsPulse => false;

    // ---- overnight logistics bookkeeping ----
    /// <summary>Hauler claim so two drones never fly for the same battery (mirrors OreChip.claimedBy).</summary>
    [HideInInspector] public Drone claimedBy;
    // Where the battery LIVES: captured when a drone lifts it for a station visit, so the
    // return trip can put it back — same pad slot if it still exists, else the loose spot.
    [HideInInspector] public EnergyPad homePad;
    [HideInInspector] public int homeSlot = -1;
    [HideInInspector] public Vector2 homePos;
    [HideInInspector] public bool hasHome;
    /// <summary>True while riding the player's follower ring (pulse batteries clicked in the dungeon).</summary>
    [HideInInspector] public bool following;
    /// <summary>Swap-window id (BatteryStation.SwapWindow) when drone logistics last slotted this
    /// battery onto a working pad — a pad battery makes at most one station trip per window.</summary>
    [HideInInspector] public int padSwapWindow;

    public void RememberHome()
    {
        if (hasHome) return;   // first lift wins — a station slot must never become "home"
        homePad = pad;
        homeSlot = padSlot;
        homePos = transform.position;
        hasHome = true;
    }

    public void ForgetHome()
    {
        homePad = null;
        homeSlot = -1;
        hasHome = false;
    }

    /// <summary>Station swap: the charged battery leaving the station takes over the rack spot of
    /// the flat one that just arrived (which stays behind as station stock).</summary>
    public void TransferHomeFrom(Battery other)
    {
        homePad = other.homePad;
        homeSlot = other.homeSlot;
        homePos = other.homePos;
        hasHome = other.hasHome;
    }

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

    protected virtual void Start()
    {
        // Deliberately NO dawn refill here: ordinary batteries are only charged by a
        // BatteryStation grinding chips (once per day). PulseBattery subscribes its own
        // daily self-charge on top of this.
    }

    /// <summary>Full top-up. Only the pulse battery's daily self-charge uses this now.</summary>
    protected void RefillToMax()
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
        // Pulse batteries (and any variant without the flash strip) skip the flash outright.
        if (quickChargeSprs == null || quickChargeSprs.Length == 0 || mats == null || mats.Length == 0)
            yield break;
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
        UpdateVisual();
    }

    /// <summary>Charge-level presentation (percent sprite + spinning coil). PulseBattery replaces
    /// this wholesale with its crate animation.</summary>
    protected virtual void UpdateVisual()
    {
        // A battery without its visual rig (renderer/coil/sprite strips unassigned) has nothing
        // to draw — bail instead of indexing empty arrays every frame.
        if (sr == null || coil == null || mats == null || mats.Length == 0 ||
            energysprs == null || energysprs.Length == 0) return;

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

    public virtual void OnClick()
    {
        if (following) return;   // follower batteries ride the ring; they're not cursor-holdable
        if (held == this) Drop();
        else if (held == null) Pickup();
        // if held != null && held != this, ignore — the held one will eat the click instead
    }

    void Pickup()
    {
        held = this;
        if (pad != null)
        {
            pad.UnslotBattery(this, playerAction: true);   // lifting by hand lowers the pad's pin
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

    protected virtual void OnDestroy()
    {
        if (held == this) held = null;
        if (pad != null) pad.UnslotBattery(this);
        FocusRouter.i?.Deselect(this);
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
