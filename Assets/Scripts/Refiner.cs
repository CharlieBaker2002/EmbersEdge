using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The colony's ore refinery — an ORE SPLITTER (IChipConsumer, so the bag-drone fleet gathers and
/// delivers for it with no drone changes). It no longer makes ember: a chip sucked into the mouth
/// digests for <see cref="refineSeconds"/> and comes back out as MORE, SMALLER chips —
///   • medium chip → 2 small chips,
///   • large chip  → 2 medium chips + 1 small chip
/// (per <see cref="mediumYield"/> / <see cref="largeYield"/>). Every chip still counts as ONE ore
/// wherever chips are counted (construction, the Hoover), so refining multiplies ore. Small chips
/// are refused (nothing left to split) and every piece that comes out is stamped
/// <see cref="OreChip.refined"/> — the refinery refuses refined chip, so its own output isn't
/// sucked straight back in and a chip is split at most once; drones and other consumers treat
/// refined chip like any other.
/// </summary>
public class Refiner : Building, IChipConsumer
{
    [System.Serializable]
    public struct SplitYield
    {
        [Tooltip("Small chips out.")]  public int small;
        [Tooltip("Medium chips out.")] public int medium;
        [Tooltip("Large chips out.")]  public int large;
        public SplitYield(int s, int m, int l) { small = s; medium = m; large = l; }
        public int Count => Mathf.Max(0, small) + Mathf.Max(0, medium) + Mathf.Max(0, large);
    }

    [Header("Refiner")]
    [Tooltip("Loose, unclaimed chips inside this radius are dragged into the mouth when it's idle.")]
    public float suctionRadius = 1.75f;
    [Tooltip("Seconds to split one chip (the absorb shrink plays inside this window).")]
    public float refineSeconds = 1.2f;
    [Tooltip("Bag-space units of chip the refinery wants on hand — the fleet plans hauls against " +
             "this, net of whatever already sits in the suction ring.")]
    public float appetiteSpace = 8f;
    [Tooltip("What a MEDIUM chip splits into.")]
    public SplitYield mediumYield = new SplitYield(2, 0, 0);
    [Tooltip("What a LARGE chip splits into.")]
    public SplitYield largeYield = new SplitYield(1, 2, 0);
    [Tooltip("Seconds between pieces leaving the mouth after a split.")]
    public float outputInterval = 0.08f;

    [Tooltip("Where chips ease in and shrink away, and where the pieces come back out. Falls back to the building centre.")]
    [SerializeField] private Transform eatSpot;

    [HideInInspector] public int inboundChipSpace;

    public static readonly List<Refiner> all = new List<Refiner>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRegistry()   // no-domain-reload: statics survive play-stop
    {
        all.Clear();
    }

    // the chip on the plate (its size captured at swallow — the OreChip object is
    // presentation-only once AbsorbInto runs and destroys itself)
    float refineT;
    int digestSize;
    float suctionScanT;
    Coroutine outputCo;

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
    /// <summary>Splitting is worth doing (appeal 2 — above the grinders' 1, below construction's 3).</summary>
    public int ChipAppeal(int sizeClass, int element) => 2;
    public int InboundChipSpace { get => inboundChipSpace; set => inboundChipSpace = value; }
    /// <summary>Only chip with something to split: medium and large.</summary>
    public bool AcceptsChip(int sizeClass, int element) => sizeClass >= 1;
    /// <summary>A chip is split at most once — output never loops back in.</summary>
    public bool RefusesRefined => true;

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
        float stock = refineT > 0f ? OreChip.SpaceFor(digestSize) : 0f;
        Vector2 pos = Mouth().position;
        for (int k = 0; k < OreChip.all.Count; k++)
        {
            var chip = OreChip.all[k];
            if (chip == null || chip.Absorbing || chip.transform.InDungeon()) continue;
            if (chip.refined || !AcceptsChip(chip.sizeClass, chip.element)) continue;
            if (((Vector2)chip.transform.position - pos).sqrMagnitude > suctionRadius * suctionRadius) continue;
            stock += chip.SpaceCost;
        }
        ringStockCached = stock;
        return stock;
    }

    Transform Mouth() => eatSpot != null ? eatSpot : transform;

    // ------------------------------------------------------------------ split

    void Update()
    {
        if (!builtYet) return;
        if (refineT > 0f)
        {
            refineT -= Time.deltaTime;
            if (refineT <= 0f) FinishSplit();
            return;
        }
        TickSuction();
    }

    /// <summary>One chip at a time: when the plate is free, the nearest splittable, unclaimed,
    /// settled chip in the ring eases into the mouth (OreChip.AbsorbInto) and starts the split.</summary>
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
            if (chip.refined) continue;                            // already split once
            if (chip.Age < 0.35f) continue;                        // let fresh drops pop in first
            if (!ChipConsumers.MayGive(this, chip.sizeClass, chip.element, chip.refined)) continue;
            float d = ((Vector2)chip.transform.position - pos).sqrMagnitude;
            if (d > suctionRadius * suctionRadius || d >= bestSqr) continue;
            best = chip;
            bestSqr = d;
        }
        if (best == null) return;
        digestSize = best.sizeClass;
        best.AbsorbInto(Mouth());
        refineT = refineSeconds;
    }

    void FinishSplit()
    {
        var y = digestSize >= 2 ? largeYield : mediumYield;
        if (y.Count <= 0) return;
        if (outputCo != null) StopCoroutine(outputCo);
        outputCo = StartCoroutine(OutputCo(y));
    }

    /// <summary>The pieces pop out of the mouth one by one, stamped refined.</summary>
    System.Collections.IEnumerator OutputCo(SplitYield y)
    {
        var sizes = new List<int>(y.Count);
        for (int k = 0; k < y.large; k++) sizes.Add(2);
        for (int k = 0; k < y.medium; k++) sizes.Add(1);
        for (int k = 0; k < y.small; k++) sizes.Add(0);
        Vector3 mouth = Mouth().position;
        foreach (int size in sizes)
        {
            var chip = DroneManager.SpawnScrap(mouth + GS.RandCircle(0.1f, 0.3f), size, 0, mouth);
            if (chip != null) chip.refined = true;
            var fx = MineField.ChipFxPrefab();
            if (fx != null) Instantiate(fx, mouth, Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)), transform);
            yield return new WaitForSeconds(outputInterval);
        }
        outputCo = null;
    }
}
