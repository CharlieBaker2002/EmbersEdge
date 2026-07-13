using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The colony's chip refinery — the chip-eating counterpart of the Battery Station's grinder
/// (IChipConsumer, so the bag-drone fleet gathers and delivers for it with no drone changes).
/// Chips sucked into the mouth digest one at a time and come out as RESOURCES:
///   • plain rock (element -1, small/medium only — large is slag) → EMBER, credited to this
///     building's own EmberConnector and routed down the cable network to whoever wants it
///     (constructors → ember generators → stores), exactly like an expander collection;
///   • ORE chip (element 0..3, strictly medium since the mines cut it that way) → a burst of
///     that element's orbs (white 12 / green 6 / blue 2 / red 1 per chip — rarer runs richer).
/// A full connector pauses PLAIN intake (AcceptsChip gates on room, so drones stop hauling rock
/// too) — ore never blocks: orbs always have somewhere to fly.
/// </summary>
public class Refiner : Building, IChipConsumer
{
    [Header("Refiner")]
    [Tooltip("Loose, unclaimed chips inside this radius are dragged into the mouth when it's idle.")]
    public float suctionRadius = 1.75f;
    [Tooltip("Seconds to digest one chip (the absorb shrink plays inside this window).")]
    public float refineSeconds = 1.2f;
    [Tooltip("Bag-space units of chip the refiner wants on hand — the fleet plans hauls against " +
             "this, net of whatever already sits in the suction ring.")]
    public float appetiteSpace = 8f;

    /// <summary>Ember credited per PLAIN chip, by size class (small / medium).</summary>
    public int emberSmall = 1;
    public int emberMedium = 2;

    /// <summary>Orbs per ORE chip, by element (white / green / blue / red) — rarer runs richer.</summary>
    public static readonly int[] OrbsPerChip = { 12, 6, 2, 1 };

    [Tooltip("Where chips ease in and shrink away. Falls back to the building centre.")]
    [SerializeField] private Transform eatSpot;
    /// <summary>This building's node in the ember cable network (taip Generator — a pure source,
    /// cabled to the nearest store like an ember generator).</summary>
    public EmberConnector connect;

    [HideInInspector] public int inboundChipSpace;

    public static readonly List<Refiner> all = new List<Refiner>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRegistry()   // no-domain-reload: statics survive play-stop
    {
        all.Clear();
    }

    // the chip on the digest plate (its size/element captured at swallow — the OreChip object
    // is presentation-only once AbsorbInto runs and destroys itself)
    float refineT;
    int digestSize, digestElement;
    float suctionScanT;

    public override void Start()
    {
        base.Start();
        GS.OnNewEra += UpdateColours;
        UpdateColours(GS.era);
    }

    void UpdateColours(int era)
    {
        if (sr != null) sr.material = GS.MatByEra(era, false, false, true);
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        GS.OnNewEra -= UpdateColours;
    }

    protected override void BEnable()
    {
        if (!all.Contains(this)) all.Add(this);
        ChipConsumers.Register(this);
        EnergyManager.i.CreateCableConnections();   // join the ember graph (source node)
    }

    protected override void BDisable()
    {
        all.Remove(this);
        ChipConsumers.Unregister(this);
        if (EnergyManager.i != null) EnergyManager.i.CreateCableConnections();
    }

    // ------------------------------------------------------------------ chip intake (IChipConsumer)

    public bool ChipIntakeActive => builtYet && enabled;
    public Vector2 ChipDropPoint => transform.position;
    public float ChipIntakeRadius => suctionRadius;
    /// <summary>Refining outranks the battery grinder — resources beat juice for the same chip.</summary>
    public int ChipPriority => 1;
    public int InboundChipSpace { get => inboundChipSpace; set => inboundChipSpace = value; }

    /// <summary>The intake gate: ore ALWAYS fits (the mines cut it strictly medium); plain rock
    /// must fit the small/medium bore AND have ember room to land in — a full connector stops
    /// the fleet hauling rock that would only pile up.</summary>
    public bool AcceptsChip(int sizeClass, int element)
        => element >= 0 || (sizeClass <= 1 && connect.ember < connect.maxEmber);

    /// <summary>Appetite net of the stock already settled in the suction ring (dumped hauls the
    /// mouth hasn't got to yet) — in-flight chips are netted off by logistics itself.</summary>
    public float ChipDemandSpace
    {
        get
        {
            if (!ChipIntakeActive) return 0f;
            return Mathf.Max(0f, appetiteSpace - RingStockSpace());
        }
    }

    float ringScanT = float.NegativeInfinity;
    float ringStockCached;

    float RingStockSpace()
    {
        if (Time.time - ringScanT < 0.25f) return ringStockCached;
        ringScanT = Time.time;
        float stock = refineT > 0f ? (digestSize == 2 ? 4 : digestSize + 1) : 0f;
        Vector2 pos = Mouth().position;
        for (int k = 0; k < OreChip.all.Count; k++)
        {
            var chip = OreChip.all[k];
            if (chip == null || chip.Absorbing || chip.transform.InDungeon()) continue;
            if (!AcceptsChip(chip.sizeClass, chip.element)) continue;
            if (((Vector2)chip.transform.position - pos).sqrMagnitude > suctionRadius * suctionRadius) continue;
            stock += chip.SpaceCost;
        }
        ringStockCached = stock;
        return stock;
    }

    Transform Mouth() => eatSpot != null ? eatSpot : transform;

    // ------------------------------------------------------------------ digest

    void Update()
    {
        if (!builtYet) return;
        if (refineT > 0f)
        {
            refineT -= Time.deltaTime;
            if (refineT <= 0f) FinishRefine();
            return;
        }
        TickSuction();
    }

    /// <summary>One chip at a time: when the plate is free, the nearest edible, unclaimed, settled
    /// chip in the ring eases into the mouth (OreChip.AbsorbInto) and starts the digest.</summary>
    void TickSuction()
    {
        if ((suctionScanT -= Time.deltaTime) > 0f) return;
        suctionScanT = 0.25f;
        Vector2 pos = Mouth().position;
        OreChip best = null;
        float bestSqr = float.MaxValue;
        for (int k = 0; k < OreChip.all.Count; k++)
        {
            var chip = OreChip.all[k];
            if (chip == null || chip.Absorbing || chip.transform.InDungeon()) continue;
            if (chip.claimedBy != null) continue;                  // a drone is flying for it
            if (chip.Age < 0.35f) continue;                        // let fresh drops pop in first
            if (!AcceptsChip(chip.sizeClass, chip.element)) continue;
            float d = ((Vector2)chip.transform.position - pos).sqrMagnitude;
            if (d > suctionRadius * suctionRadius || d >= bestSqr) continue;
            best = chip;
            bestSqr = d;
        }
        if (best == null) return;
        digestSize = best.sizeClass;
        digestElement = best.element;
        best.AbsorbInto(Mouth());
        refineT = refineSeconds;
    }

    void FinishRefine()
    {
        if (digestElement >= 0)
        {
            // ore → a burst of its element's orbs, popping out wild at the mouth
            int[] counts = new int[4];
            counts[digestElement] = OrbsPerChip[Mathf.Clamp(digestElement, 0, 3)];
            GS.CallSpawnOrbs(Mouth().position, counts);
        }
        else
        {
            // plain rock → ember on our connector, each unit routed to live demand at once
            // (constructors → ember generators → stores), riding the cables like expander ember
            int n = digestSize >= 1 ? emberMedium : emberSmall;
            for (int k = 0; k < n; k++)
            {
                if (connect.ember >= connect.maxEmber) break;   // network saturated — the rest is slag
                connect.ember++;
                connect.onRefresh?.Invoke();
                EnergyManager.i.RouteExtractedEmber(connect);
            }
        }
    }
}
