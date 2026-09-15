using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// A chip-fed generator. Two prefabs run this one script with different numbers:
///   • Solo Generator (two cells) — 2 chips → 1 energy, no power needed, holds 1;
///   • Crush Generator (the old Pulse Generator, four cells) — 25 chips, then 3 energy DRAWN
///     from the grid in ONE burst (<see cref="crushEnergy"/>, user call 2026-09-14: all at once,
///     so the grid must be able to surge it — batteries, pylon pools), → 15 energy (12 net),
///     holds 30. It shows the "no energy" sign whenever its supply is empty and "insufficient"
///     while it stands full but the burst can't be met.
/// It stands OPEN (frame 0) waiting for <see cref="chipsPerCrush"/> chips: anything settling in
/// its ring is sucked in (a drone deposit, the Hoover's spray), and a Collector beside it, a
/// Tube beside it or a Belt ending at it hand chip straight in (<see cref="TakeDelivered"/>).
/// A Solo ranks those feeds (<see cref="MayTakeFrom"/>): Collector → Belt into it → Tube →
/// Belt tiles beside it, which it pulls chip off itself.
/// With its fill (and, when it needs any, the crush energy taken in one go) it slams shut —
/// frames 0→<see cref="crushFrame"/> at <see cref="crushFps"/> —
/// banks <see cref="energyPerCrush"/> into ITSELF on the crush frame, then eases back open
/// through the rest of the strip at <see cref="resetFps"/>. It holds at most
/// <see cref="maxEnergy"/> and takes chip only while a whole crush's worth still fits, so
/// nothing is ground for nothing. It is a grid SOURCE: buildings in the four cardinal cells
/// round its footprint draw from it (EnergyManager.RegisterSource — the generator pattern) at
/// <see cref="drawRate"/>. ONE bar: a crusher that draws gets the usual ThrottleBar for its
/// input burst (nothing for the bank; the Solo shows no bar). Prefabs: Tools/Crush Generator Kit.
/// </summary>
public class CrushGenerator : Building, IEnergyAccumulator, IChipConsumer
{
    [Header("Crush Generator")]
    [Tooltip("The crush cycle strip: 0 open … crushFrame shut … back round to open.")]
    public Sprite[] frames;
    [Tooltip("Chips one crush needs.")]
    public int chipsPerCrush = 2;
    [Tooltip("Energy banked per crush.")]
    public float energyPerCrush = 1f;
    [Tooltip("Energy held at most. Chip is taken only while a whole crush still fits.")]
    public float maxEnergy = 1f;
    [Tooltip("Energy/second adjacent buildings may draw from the bank.")]
    public float drawRate = 1f;
    [Tooltip("Grid energy one crush must DRAW from adjacent sources before it fires — ALL AT ONCE, in a single burst (0 = none — the Solo Generator). A grid that can't surge this much shows 'insufficient' and the plates wait.")]
    public float crushEnergy = 0f;
    [Tooltip("The frame the plates meet on — the energy lands here.")]
    public int crushFrame = 8;
    [Tooltip("Frames per second on the way shut (quick) when no crush energy is needed.")]
    public float crushFps = 24f;
    [Tooltip("Frames per second easing back open (slow).")]
    public float resetFps = 4f;
    [Tooltip("Loose chips this close to the centre are pulled in while it stands open.")]
    public float intakeRadius = 0.55f;
    [Tooltip("Seconds between intake scans.")]
    public float suctionInterval = 0.2f;

    readonly EnergyStore store = new EnergyStore(1f, 1f, 0f);
    int held;
    bool crushing;
    Coroutine crushCo;
    float scanT;
    int inbound;
    float ringScanT = float.NegativeInfinity;
    int ringStockCached;

    /// <summary>A crusher that draws for its crush wants that whole burst at once — the input ThrottleBar's span.</summary>
    public override float PeakEnergyDemand => Mathf.Max(0f, crushEnergy);
    /// <summary>"No energy" / "insufficient" signs for the crusher that needs grid energy (the Solo needs none).</summary>
    public override bool ShowsEnergyIcons => crushEnergy > 0f;

    // ------------------------------------------------------------------ IEnergyAccumulator (a source)

    public float Energy => store.Energy;
    public float MaxEnergy => store.MaxEnergy;
    public float DrawRate => store.DrawRate;
    public float MaxDrawThisFrame(float dt) => store.MaxDrawThisFrame(dt);
    public float PeekMaxDraw(float dt) => store.PeekMaxDraw(dt);
    public event Action<float> OnUpdate;
    public event Action OnUse;

    public bool Use(float cost)
    {
        if (!store.Use(cost)) return false;
        OnUpdate?.Invoke(store.Energy);
        OnUse?.Invoke();
        RefreshReadoutIfRoomChanged();   // the bank just made room → it wants chip again
        return true;
    }

    public void Add(float amount)
    {
        if (amount <= 0f) return;
        store.Add(amount);
        OnUpdate?.Invoke(store.Energy);
        RefreshReadoutIfRoomChanged();
    }

    /// <summary>A whole crush's worth still fits in the bank.</summary>
    bool Room => store.Energy <= maxEnergy - energyPerCrush + 0.01f;

    /// <summary>Chips swallowed toward the current crush.</summary>
    public int Held => held;
    public bool Crushing => crushing;

    // ------------------------------------------------------------------ lifecycle

    public override void Start()
    {
        base.Start();
        store.Configure(Mathf.Max(0f, maxEnergy), Mathf.Max(0f, drawRate), 0f);
        // ONE bar (user call 2026-09-14): the input ThrottleBar Building attaches for
        // PeakEnergyDemand = crushEnergy. No bank gauge — the Solo shows no bar at all.
    }

    protected override void BEnable()
    {
        EnergyManager.i?.RegisterSource(this, anchorCell, gridSize);
        ChipConsumers.Register(this);
        if (sr != null && frames != null && frames.Length > 0) sr.sprite = frames[0];
        RefreshReadout();
    }

    protected override void BDisable()
    {
        EnergyManager.i?.UnregisterSource(this, anchorCell, gridSize);
        ChipConsumers.Unregister(this);
        if (crushCo != null) { StopCoroutine(crushCo); crushCo = null; }
        crushing = false;
        held = 0;
        if (store.Energy > 0f) store.Use(store.Energy);   // a wreck keeps nothing
        OnUpdate?.Invoke(store.Energy);
        ClearEnergyStatusImmediate();
        ShowOreProgress(0, 0, false);
    }

    protected override void OnRotated(Vector2Int oldAnchor, Vector2Int oldSize)
    {
        if (!builtYet || (oldAnchor == anchorCell && oldSize == gridSize)) return;   // a square just spins
        EnergyManager.i?.UnregisterSource(this, oldAnchor, oldSize);
        EnergyManager.i?.RegisterSource(this, anchorCell, gridSize);
    }

    void Update()
    {
        store.Tick(Time.deltaTime);   // reconcile the store every frame, even when idle
        if (!builtYet) return;
        // not crushing: just report the supply — the "no energy" sign while it's empty (it can
        // never crush without it), nothing otherwise; the waiting loop reports while full
        if (!crushing && crushEnergy > 0f) ReportEnergyDraw(0f);
        if (crushing) return;
        scanT -= Time.deltaTime;
        if (scanT <= 0f)
        {
            scanT = Mathf.Max(0.05f, suctionInterval);
            TickSuction();
            TickBeltPull();
        }
    }

    // ------------------------------------------------------------------ Solo feed priority

    /// <summary>A Solo Generator: needs no grid energy for its crush.</summary>
    public bool IsSolo => crushEnergy <= 0f;

    /// <summary>Where a Solo's chip comes from, in PREFERENCE order (user rule 2026-09-14).</summary>
    public enum Feed { Collector = 0, IntoBelt = 1, Tube = 2, AdjacentBelt = 3 }

    /// <summary>Solo feed priority: a Collector beside it → a Belt ending INTO it → Tube shelf
    /// inside its ring → a Belt tile passing beside it. A source may hand a Solo chip only while
    /// no higher-ranked source has chip ready for it (so a lower one never fills the Solo ahead
    /// of a better one). Loose ground chip — drone deposits, the Hoover's spray — is outside the
    /// ranking. Any other crusher takes from anything.</summary>
    public bool MayTakeFrom(Feed src)
    {
        if (!IsSolo) return true;
        if (src > Feed.Collector && CollectorHasChip()) return false;
        if (src > Feed.IntoBelt && IntoBeltHasChip()) return false;
        if (src > Feed.Tube && TubeHasChip()) return false;
        return true;
    }

    bool CollectorHasChip()
    {
        var cols = Collector.All;
        for (int k = 0; k < cols.Count; k++)
        {
            var col = cols[k];
            if (col == null || !col.builtYet || col.MarkedForDemolition) continue;
            if (ChipConsumers.FootprintsAdjacent(col, this) && col.HasChipFor(this)) return true;
        }
        return false;
    }

    bool IntoBeltHasChip()
    {
        var lines = Belt.Lines;
        for (int l = 0; l < lines.Count; l++) if (lines[l].ReadyToFeed(this)) return true;
        return false;
    }

    bool TubeHasChip()
    {
        var clusters = Tube.Clusters;
        for (int c = 0; c < clusters.Count; c++) if (clusters[c].HasChipWithin(this, ChipDropPoint, intakeRadius)) return true;
        return false;
    }

    /// <summary>Last in the ranking: a Solo takes chip straight off a Belt tile beside it (a line
    /// passing by, or leading away) — the nearest chip riding a tile that touches its footprint,
    /// one per scan, swallowed at once. The tail of a line ending INTO it is left alone: that
    /// chip comes in through the exit, a better-ranked feed.</summary>
    void TickBeltPull()
    {
        if (!IsSolo || !ChipIntakeActive || held >= chipsPerCrush) return;
        if (!MayTakeFrom(Feed.AdjacentBelt)) return;
        Vector2 me = transform.position;
        var lines = Belt.Lines;
        BeltLine bestLine = null;
        BeltLine.Entry best = null;
        float bd = float.MaxValue;
        for (int l = 0; l < lines.Count; l++)
        {
            var line = lines[l];
            if (line.chips.Count == 0) continue;
            bool intoMe = line.FeedsInto(this);
            for (int i = 0; i < line.chips.Count; i++)
            {
                var e = line.chips[i];
                if (e.exiting || e.tile == null || e.chip == null || e.chip.Absorbing || e.chip.Fading) continue;
                if (intoMe && e.tile == line.tail) continue;
                if (!ChipConsumers.FootprintAdjacent(this, e.tile.Centre)) continue;
                float d = ((Vector2)e.chip.transform.position - me).sqrMagnitude;
                if (d >= bd) continue;
                bd = d;
                best = e;
                bestLine = line;
            }
        }
        if (best == null) return;
        var chip = bestLine.ReleaseTo(best, byPlayer: false);
        if (chip != null) TakeDelivered(chip);
    }

    // ------------------------------------------------------------------ chip intake (IChipConsumer)

    public bool ChipIntakeActive => builtYet && enabled && !MarkedForDemolition && !crushing && Room && !transform.InDungeon();
    public Vector2 ChipDropPoint => transform.position;
    public float ChipIntakeRadius => intakeRadius;
    /// <summary>A Solo (no grid energy needed — a base power source wanting 2 chips) is the
    /// keenest customer there is, construction-level, so a hungry one is fed FIRST (user call
    /// 2026-09-14); the Crush Generator, wanting 25 plus a burst, stays grinder-level so it
    /// can't hog the base's chip.</summary>
    public int ChipAppeal(int sizeClass, int element) => crushEnergy > 0f ? 1 : 3;
    /// <summary>Chips still wanted for this crush, net of what already lies in the ring, in medium-chip space.</summary>
    public float ChipDemandSpace => ChipIntakeActive ? Mathf.Max(0, chipsPerCrush - held - RingStock()) * 2f : 0f;
    public int InboundChipSpace { get => inbound; set => inbound = Mathf.Max(0, value); }
    /// <summary>Any chip is one chip to the crusher.</summary>
    public bool AcceptsChip(int sizeClass, int element) => true;

    /// <summary>A Belt, Tube or Collector handing a chip straight in — swallowed at once while open.</summary>
    public bool TakeDelivered(OreChip chip)
    {
        if (chip == null || chip.Absorbing || chip.Fading || chip.Stored || !ChipIntakeActive) return false;
        Swallow(chip);
        return true;
    }

    int RingStock()
    {
        if (Time.time - ringScanT < 0.25f) return ringStockCached;
        ringScanT = Time.time;
        int n = 0;
        Vector2 pos = transform.position;
        float r2 = intakeRadius * intakeRadius;
        for (int k = 0; k < OreChip.all.Count; k++)
        {
            var chip = OreChip.all[k];
            if (chip == null || chip.Absorbing || chip.transform.InDungeon()) continue;
            if (((Vector2)chip.transform.position - pos).sqrMagnitude > r2) continue;
            n++;
        }
        ringStockCached = n;
        return n;
    }

    /// <summary>The nearest settled, unclaimed chip in the ring eases in. The fleet's dibs apply
    /// in the ring (a keener hungry customer's chip is left for the drones) — but NOT to a chip
    /// lying ON the machine itself: that was put there on purpose (the Hoover's spray, a drop),
    /// the Tube's on-top rule. Without it a crusher stood idle for as long as any construction
    /// ghost anywhere was hungry.</summary>
    void TickSuction()
    {
        if (!ChipIntakeActive) return;
        Vector2 pos = transform.position;
        float r2 = intakeRadius * intakeRadius;
        OreChip best = null;
        float bestSqr = float.MaxValue;
        for (int k = 0; k < OreChip.all.Count; k++)
        {
            var chip = OreChip.all[k];
            if (chip == null || chip.Absorbing || chip.transform.InDungeon()) continue;
            if (chip.claimedBy != null || chip.PulledByPlayer) continue;
            if (chip.Age < 0.35f) continue;   // let a fresh drop pop in first
            Vector2 p = chip.transform.position;
            float d = (p - pos).sqrMagnitude;
            if (d > r2 || d >= bestSqr) continue;
            if (!ChipConsumers.FootprintContains(this, p)
                && !ChipConsumers.MayGive(this, chip.sizeClass, chip.element, chip.refined)) continue;
            best = chip;
            bestSqr = d;
        }
        if (best != null) Swallow(best);
    }

    /// <summary>"held/chipsPerCrush" over the machine ONLY while it needs more chip (user call
    /// 2026-09-14): open, not crushing, and with room in the bank for another crush — a full
    /// Solo shows nothing. Re-checked whenever the bank moves (Use/Add flip <see cref="Room"/>).</summary>
    void RefreshReadout()
    {
        readoutShown = builtYet && !crushing && Room;
        ShowOreProgress(held, chipsPerCrush, readoutShown);
    }
    bool readoutShown;
    void RefreshReadoutIfRoomChanged() { if ((builtYet && !crushing && Room) != readoutShown) RefreshReadout(); }

    void Swallow(OreChip chip)
    {
        held++;
        ChipConsumers.CreditServed(this, chip.SpaceCost);
        chip.AbsorbInto(transform, 0f);
        ringStockCached = Mathf.Max(0, ringStockCached - 1);
        if (held >= chipsPerCrush && crushCo == null) crushCo = StartCoroutine(CrushCo());
        RefreshReadout();
    }

    // ------------------------------------------------------------------ the crush

    /// <summary>Shut (quick, or as fast as the grid pays the crush energy), bank the energy on
    /// the crush frame, ease back open slowly.</summary>
    IEnumerator CrushCo()
    {
        crushing = true;
        RefreshReadout();
        yield return new WaitForSeconds(0.3f);   // the last chip's swallow plays out first
        int last = frames != null ? frames.Length - 1 : -1;
        int shut = Mathf.Clamp(crushFrame, 0, Mathf.Max(0, last));
        float fast = 1f / Mathf.Max(1f, crushFps), slow = 1f / Mathf.Max(0.5f, resetFps);
        if (crushEnergy > 0f)
        {
            // ONE burst: the whole crushEnergy in a single frame's draw (user call 2026-09-14).
            // The plates stand open until the adjacent sources can surge it — a bare trickle
            // generator never can; batteries and pylon pools do. Signs while it waits: "no
            // energy" with an empty supply, "insufficient" with energy that can't burst.
            // MaxDrawThisFrame is the frame's demand registration (fair share); Use is the burst.
            while (true)
            {
                float dt = Time.deltaTime;
                if (StatusEnergy <= 1e-3f) ReportEnergyDraw(0f);
                else if (Power.MaxDrawThisFrame(dt) + 1e-4f >= crushEnergy && Power.Use(crushEnergy)) break;
                else ReportInsufficientEnergy();
                yield return null;
            }
            ClearEnergyStatus();
        }
        for (int f = 1; f < shut; f++)
        {
            Show(f);
            yield return new WaitForSeconds(fast);
        }
        Show(shut);
        Add(Mathf.Max(0f, energyPerCrush));
        SpawnStrikeRing.Ring(transform.position, 1f, 0.5f * Mathf.Max(size.x, size.y));
        for (int f = shut + 1; f <= last; f++)
        {
            Show(f);
            yield return new WaitForSeconds(slow);
        }
        Show(0);
        held = 0;
        crushing = false;
        crushCo = null;
        RefreshReadout();
    }

    void Show(int f)
    {
        if (sr == null || frames == null || frames.Length == 0) return;
        sr.sprite = frames[Mathf.Clamp(f, 0, frames.Length - 1)];
    }
}
