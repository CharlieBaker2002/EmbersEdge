using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The Chip Factory (the "Chip Shop") — where the colony's batteries and the player's boosts are
/// BUILT from chip. The player queues an item from the building's UI (free to click — the price
/// is paid in JUICE, ground from ore chips the bag-drone fleet hauls in through the shared
/// IChipConsumer seam, exactly like the Chip Charger's grinder). While an order is outstanding
/// the factory advertises chip demand; if there's chip at base the fleet runs it over, otherwise
/// the order simply waits for the next dungeon haul.
///
/// Products:
///   • Battery — <see cref="batteryJuice"/> (20) juice, pops out loose at the door and the
///     distribution drones take it from there. This is where colony batteries COME FROM now
///     (the charger only recharges them).
///   • Boosts — one UI tile per MechanismSO in <see cref="level1Boosts"/> /
///     <see cref="level2Boosts"/> / <see cref="level3Boosts"/>; higher lists unlock with the
///     factory's two one-time level upgrades. A finished boost waits on the shelf until the
///     player has a free boost slot, then attaches via MechaSuit.AddParts.
///
/// Throughput is day-capped: at most <see cref="buildsPerDay"/> (2) orders may be QUEUED per
/// day (the tile flashes red past the cap); the counter resets on the day tick.
/// </summary>
public class ChipFactory : Building, IChipConsumer
{
    [Header("Chip Factory")]
    [Tooltip("Loose, unclaimed chips inside this radius are dragged into the grinder while an order wants juice.")]
    public float suctionRadius = 1.75f;
    [Tooltip("Approximate juice per unit of inbound bag space — only the drone demand heuristic. " +
             "Actual credit on grinding comes from OreChip.JuiceValue (per size).")]
    public float juicePerChipSpace = 0.25f;
    [Tooltip("Juice one battery costs to build.")]
    public float batteryJuice = 20f;
    [Tooltip("Juice a boost costs, by the LIST it comes from (level 1 / 2 / 3).")]
    public float[] boostJuiceByLevel = { 20f, 30f, 40f };
    [Tooltip("Orders the player may queue per day.")]
    public int buildsPerDay = 2;

    [Tooltip("The loose battery built by a battery order (drones distribute it).")]
    public Battery batteryPrefab;
    [Tooltip("Boost mechanisms craftable from level 1 (one UI tile each).")]
    public MechanismSO[] level1Boosts;
    [Tooltip("Unlocked by the Level 2 upgrade.")]
    public MechanismSO[] level2Boosts;
    [Tooltip("Unlocked by the Level 3 upgrade.")]
    public MechanismSO[] level3Boosts;

    [Tooltip("Where chips ease in and shrink away. Falls back to the building centre.")]
    [SerializeField] private Transform eatSpot;
    [Tooltip("Where finished batteries pop out. Falls back to the building centre.")]
    [SerializeField] private Transform outSpot;
    [Tooltip("Optional per-era body sprites (the ChipShop strip's three frames). Frame 0 ships on the renderer.")]
    [SerializeField] private Sprite[] eraSprites;

    /// <summary>Factory level (1..3) — gates which boost lists show. Raised by the two one-time upgrades.</summary>
    [HideInInspector] public int level = 1;
    /// <summary>Orders queued today (resets on the day tick).</summary>
    [HideInInspector] public int buildsToday;
    /// <summary>Ground-down chip energy banked toward the order queue.</summary>
    [HideInInspector] public float juice;
    [HideInInspector] public int inboundChipSpace;

    struct Order
    {
        public MechanismSO boost;   // null = battery
        public float juiceCost;
    }

    readonly List<Order> orders = new List<Order>();
    // finished boosts waiting for a free player boost slot
    readonly List<MechanismSO> shelf = new List<MechanismSO>();

    float suctionScanT;
    float grantT;
    System.Action newDay;

    public static readonly List<ChipFactory> all = new List<ChipFactory>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRegistry()   // no-domain-reload: statics survive play-stop
    {
        all.Clear();
    }

    public override void Start()
    {
        base.Start();
        GS.OnNewEra += UpdateColours;
        UpdateColours(GS.era);
        newDay = () => buildsToday = 0;
        if (SpawnManager.instance != null) SpawnManager.instance.OnNewDay += newDay;

        // Level upgrades, staged like the Chip Charger's intake bores: Level 3 only appears
        // once Level 2 is ground in.
        AddUpgradeSlot(new int[] { 0, 20, 0, 0 }, "Factory Level 2", icon, true,
            () => level = Mathf.Max(level, 2), 4, true);
        AddUpgradeSlot(new int[] { 0, 60, 0, 0 }, "Factory Level 3", icon, true,
            () => level = Mathf.Max(level, 3), 6, true,
            null, null, () => level >= 2);

        // Order tiles: free to click (the price is juice), gated by the daily cap.
        Sprite batteryIcon = batteryPrefab != null ? batteryPrefab.GetComponentInChildren<SpriteRenderer>(true)?.sprite : null;
        AddSlot(new int[4], $"Battery ({batteryJuice:0} Juice)", batteryIcon != null ? batteryIcon : icon, false,
            () => Enqueue(null, batteryJuice), false, null, CanQueue);
        AddBoostSlots(level1Boosts, 1);
        AddBoostSlots(level2Boosts, 2);
        AddBoostSlots(level3Boosts, 3);
    }

    void AddBoostSlots(MechanismSO[] list, int lvl)
    {
        if (list == null) return;
        float cost = boostJuiceByLevel[Mathf.Clamp(lvl - 1, 0, boostJuiceByLevel.Length - 1)];
        foreach (MechanismSO so in list)
        {
            if (so == null) continue;
            MechanismSO captured = so;
            AddSlot(new int[4], $"{so.name} ({cost:0} Juice)", so.s, false,
                () => Enqueue(captured, cost), false, null, CanQueue,
                lvl <= 1 ? (System.Func<bool>)null : () => level >= lvl);
        }
    }

    bool CanQueue() => buildsToday < buildsPerDay;

    void Enqueue(MechanismSO boost, float juiceCost)
    {
        buildsToday++;
        orders.Add(new Order { boost = boost, juiceCost = juiceCost });
        CM.Message($"Chip Factory: {(boost == null ? "Battery" : boost.name)} ordered ({buildsToday}/{buildsPerDay} today)", false);
    }

    void UpdateColours(int era)
    {
        if (sr != null)
        {
            sr.material = GS.MatByEra(era, false, false, true);
            if (eraSprites != null && eraSprites.Length > 0)
                sr.sprite = eraSprites[Mathf.Clamp(era, 0, eraSprites.Length - 1)];
        }
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        GS.OnNewEra -= UpdateColours;
        if (SpawnManager.instance != null && newDay != null) SpawnManager.instance.OnNewDay -= newDay;
    }

    protected override void BEnable()
    {
        if (!all.Contains(this)) all.Add(this);
        ChipConsumers.Register(this);
    }

    protected override void BDisable()
    {
        all.Remove(this);
        ChipConsumers.Unregister(this);
    }

    // ------------------------------------------------------------------ chip intake (IChipConsumer)

    public bool ChipIntakeActive => builtYet && enabled;
    public Vector2 ChipDropPoint => transform.position;
    public float ChipIntakeRadius => suctionRadius;
    /// <summary>Any chip is factory food (it all grinds to juice), but ore is fallback only —
    /// hungry refiners (appeal 2) keep dibs on it, same standing as the charger's grinder.</summary>
    public int ChipAppeal(int sizeClass, int element) => element >= 0 ? 0 : 1;
    /// <summary>Juice still owed on the order queue, in bag-space units for the fleet's planning.</summary>
    public float ChipDemandSpace => JuiceDemand / Mathf.Max(0.01f, juicePerChipSpace);
    public int InboundChipSpace { get => inboundChipSpace; set => inboundChipSpace = value; }
    /// <summary>No bore tiers here — the factory eats any chip while an order still wants juice.</summary>
    public bool AcceptsChip(int sizeClass, int element) => JuiceDemand > 0f;

    /// <summary>Juice the outstanding orders still need, net of the bank.</summary>
    public float JuiceDemand
    {
        get
        {
            float need = 0f;
            for (int i = 0; i < orders.Count; i++) need += orders[i].juiceCost;
            return Mathf.Max(0f, need - juice);
        }
    }

    // ------------------------------------------------------------------ grind + build

    void Update()
    {
        if (!builtYet) return;
        TickSuction();
        TickBuild();
        TickShelf();
    }

    void TickSuction()
    {
        if ((suctionScanT -= Time.deltaTime) > 0f) return;
        suctionScanT = 0.25f;
        if (JuiceDemand <= 0f) return;
        Vector2 pos = eatSpot != null ? (Vector2)eatSpot.position : (Vector2)transform.position;
        for (int k = OreChip.all.Count - 1; k >= 0; k--)
        {
            var chip = OreChip.all[k];
            if (chip == null || chip.Absorbing || chip.transform.InDungeon()) continue;
            if (chip.claimedBy != null) continue;                  // a drone is flying for it
            if (chip.Age < 0.35f) continue;                        // let fresh drops pop in first
            if (((Vector2)chip.transform.position - pos).sqrMagnitude > suctionRadius * suctionRadius) continue;
            juice += chip.JuiceValue;
            chip.AbsorbInto(eatSpot != null ? eatSpot : transform);
            if (JuiceDemand <= 0f) break;
        }
    }

    /// <summary>Head-of-queue order completes the moment the bank covers it.</summary>
    void TickBuild()
    {
        if (orders.Count == 0 || juice < orders[0].juiceCost) return;
        Order o = orders[0];
        orders.RemoveAt(0);
        juice -= o.juiceCost;
        Vector3 door = outSpot != null ? outSpot.position : transform.position;
        if (o.boost == null)
        {
            if (batteryPrefab != null) Instantiate(batteryPrefab, door, Quaternion.identity);
            CM.Message("Chip Factory: Battery built", false);
        }
        else
        {
            shelf.Add(o.boost);   // attaches when the player has a boost slot free
        }
    }

    /// <summary>Finished boosts sit on the shelf until the player has a free boost slot, then
    /// attach (MechaSuit.AddParts spawns the Part on the suit wherever the player is).</summary>
    void TickShelf()
    {
        if (shelf.Count == 0 || (grantT -= Time.deltaTime) > 0f) return;
        grantT = 1f;
        if (MechaSuit.m == null || CharacterScript.dead || MechaSuit.boostsLeft <= 0) return;
        MechanismSO so = shelf[0];
        shelf.RemoveAt(0);
        MechaSuit.m.AddParts(new[] { so });
        CM.Message($"Chip Factory: {so.name} ready", false);
    }
}
