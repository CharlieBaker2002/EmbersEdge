using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The colony's charging bay — an EnergyPad that STOCKS itself with 4 batteries when built
/// (pads and hubs are empty housings now) and is the only thing that charges ordinary
/// batteries: ore chips fed into it are ground down into juice, and juice is pumped into
/// slotted batteries at most ONCE per battery per day.
///
/// Chips are ground by suction: any loose, unclaimed chip inside <see cref="suctionRadius"/>
/// eases into the grinder mouth and shrinks away (OreChip.AbsorbInto), crediting juice — so
/// both drone deliveries (dropped beside the station) and player-swept piles get eaten.
///
/// The overnight logistics the drones run (haul discharged batteries in, feed chips, return
/// batteries home) coordinate through the static Claim* methods here; per-station inbound
/// counters stop the fleet from over-fetching.
/// </summary>
public class BatteryStation : EnergyPad
{
    [Header("Battery Station")]
    [Tooltip("Energy credited per unit of chip space ground down (small chip = 1, big = 2, large = 3).")]
    public float juicePerChipSpace = 0.5f;
    [Tooltip("Energy/sec pumped into each charging battery while juice remains.")]
    public float chargeRate = 1.5f;
    [Tooltip("Loose chips inside this radius are dragged into the grinder whenever juice is wanted.")]
    public float suctionRadius = 1.5f;
    [Tooltip("Juice the grinder will bank ahead of demand. Suction/feeding stop at the cap.")]
    public float maxJuice = 16f;

    [SerializeField] private Transform eatSpot;

    /// <summary>Ground-down chip energy waiting to be pumped into batteries.</summary>
    [HideInInspector] public float juice;
    /// <summary>Chip space (units) in drone bags currently bound for this station.</summary>
    [HideInInspector] public int inboundChipSpace;
    // (inboundBatteries lives on EnergyPad now — pads track their own inbound distribution too)

    // Batteries that received ANY juice this visit: unslotting stamps their once-a-day charge,
    // so a partial charge (grinder starved) still counts as the day's charge once it leaves.
    readonly HashSet<Battery> visitCharged = new HashSet<Battery>();

    float suctionScanT;

    public static readonly List<BatteryStation> all = new List<BatteryStation>();

    /// <summary>Swap-window id. A working pad's battery makes at most ONE station trip per
    /// window; a fresh window opens at each day tick (wave complete) and again when the player
    /// returns from the dungeon — two swap opportunities per day, never continuous churn.
    /// Batteries record the window drone logistics placed them on a pad (Battery.padSwapWindow).</summary>
    static int swapWindow;
    static int swapWindowDay = int.MinValue;

    /// <summary>The current swap-window id (folds in the day tick lazily).</summary>
    public static int SwapWindow
    {
        get
        {
            if (SpawnManager.day != swapWindowDay) { swapWindowDay = SpawnManager.day; swapWindow++; }
            return swapWindow;
        }
    }

    /// <summary>Teleport-home hook (DroneManager) — the homecoming opens the day's SECOND swap
    /// window: pads already served after the wave completed get one more service now.</summary>
    public static void StampHomecoming()
    {
        _ = SwapWindow;   // fold in any pending day tick first, or it would re-open this window later
        swapWindow++;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRegistry()   // no-domain-reload: statics survive play-stop
    {
        all.Clear();
        swapWindow = 0;
        swapWindowDay = int.MinValue;
    }

    /// <summary>Stations spawn a full rack — this is where the colony's batteries come from.</summary>
    protected override int InitialBatteryCount => 4;

    protected override void BEnable()
    {
        base.BEnable();
        if (!all.Contains(this)) all.Add(this);
    }

    protected override void BDisable()
    {
        base.BDisable();
        all.Remove(this);
    }

    // ------------------------------------------------------------------ grind + charge

    void Update()
    {
        if (!builtYet) return;
        TickSuction();
        TickCharge();
    }

    /// <summary>Juice still wanted to finish every chargeable slotted battery, net of the bank.</summary>
    public float JuiceDemand
    {
        get
        {
            float need = 0f;
            for (int i = 0; i < slots.Length; i++)
            {
                var b = slots[i];
                if (b == null || !Chargeable(b)) continue;
                need += b.maxEnergy - b.energy;
            }
            return Mathf.Max(0f, Mathf.Min(need, maxJuice) - juice);
        }
    }

    /// <summary>May THIS station pump into this battery right now? Pulse batteries self-charge;
    /// a battery already charged today waits for tomorrow.</summary>
    static bool Chargeable(Battery b)
        => !b.IsPulse && !b.ChargedToday && b.energy < b.maxEnergy - 1e-3f;

    void TickSuction()
    {
        if ((suctionScanT -= Time.deltaTime) > 0f) return;
        suctionScanT = 0.25f;
        if (JuiceDemand <= 0f) return;
        Vector2 pos = eatSpot.position;
        for (int k = OreChip.all.Count - 1; k >= 0; k--)
        {
            var chip = OreChip.all[k];
            if (chip == null || chip.Absorbing || chip.transform.InDungeon()) continue;
            if (chip.claimedBy != null) continue;                  // a drone is flying for it
            if (chip.Age < 0.35f) continue;                        // let fresh drops pop in first
            if (((Vector2)chip.transform.position - pos).sqrMagnitude > suctionRadius * suctionRadius) continue;
            juice = Mathf.Min(maxJuice, juice + chip.SpaceCost * juicePerChipSpace);
            chip.AbsorbInto(eatSpot);                            // the ease-in + shrink grind
            if (juice >= maxJuice) break;
        }
    }

    void TickCharge()
    {
        if (juice <= 0f) return;
        for (int i = 0; i < slots.Length; i++)
        {
            var b = slots[i];
            if (b == null || !Chargeable(b)) continue;
            float give = Mathf.Min(chargeRate * Time.deltaTime, juice, b.maxEnergy - b.energy);
            if (give <= 0f) continue;
            b.Add(give);
            juice -= give;
            visitCharged.Add(b);
            if (b.energy >= b.maxEnergy - 1e-3f) b.StampChargedToday();   // done — today's charge spent
            if (juice <= 0f) break;
        }
    }

    /// <summary>Leaving the station cashes the once-a-day rule: a battery that received ANY juice
    /// this visit is stamped, even if the grinder starved before it filled.</summary>
    public override void UnslotBattery(Battery b, bool playerAction = false)
    {
        if (b != null && visitCharged.Remove(b)) b.StampChargedToday();
        base.UnslotBattery(b, playerAction);
    }

    // ------------------------------------------------------------------ fleet job board

    /// <summary>"No more chip / all chip is being used": nothing left to grind for this station.</summary>
    public bool Starved
    {
        get
        {
            if (juice > 0f || inboundChipSpace > 0) return false;
            for (int k = 0; k < OreChip.all.Count; k++)
            {
                var chip = OreChip.all[k];
                if (chip == null || chip.Absorbing || chip.transform.InDungeon()) continue;
                if (chip.claimedBy != null) continue;
                if (chip.Age < OreChip.SettleSeconds) continue;
                return false;   // an unclaimed base-side chip exists somewhere
            }
            return true;
        }
    }

    static bool Full(Battery b) => b.energy >= b.maxEnergy - 1e-3f;

    /// <summary>A swap must hand back MEANINGFULLY more energy than it takes in, or it's churn.</summary>
    public const float SwapMargin = 0.5f;

    /// <summary>Is there anything to charge WITH, fleet-wide: banked juice, chips in flight, or
    /// any base-side chip on the ground (claimed or not — the charging economy is alive).</summary>
    public static bool ChargingPossible()
    {
        foreach (var st in all)
            if (st != null && st.builtYet && st.enabled && (st.juice > 0f || st.inboundChipSpace > 0))
                return true;
        for (int k = 0; k < OreChip.all.Count; k++)
        {
            var chip = OreChip.all[k];
            if (chip == null || chip.Absorbing || chip.transform.InDungeon()) continue;
            if (chip.Age < OreChip.SettleSeconds) continue;
            return true;
        }
        return false;
    }

    int FreeSlotCount()
    {
        int n = 0;
        for (int i = 0; i < slots.Length; i++) if (slots[i] == null) n++;
        return n;
    }

    /// <summary>A bag drone free of telepad duty, base-side, with today's haul quota (and either
    /// charge left or a recharge still owed) — the chip-run crew. While one exists AND there's
    /// chip to grind, pad service holds out for FULL substitutes: partial stock keeps charging
    /// instead of riding out early, and each swap fires the moment a battery fills.</summary>
    public static bool FreeBagAvailable()
    {
        for (int k = 0; k < AllyAI.allies.Count; k++)
        {
            if (AllyAI.allies[k] is not Drone d || d == null) continue;
            if (d.equipment != DroneEquipment.Bag || d.assignedPad != null) continue;
            if (!d.gameObject.activeInHierarchy || d.transform.InDungeon()) continue;
            if (!d.HasDailyHaulQuota) continue;
            if (!d.Charged && d.ChargedToday) continue;   // flat with the day's recharge spent
            return true;
        }
        return false;
    }

    /// <summary>Stock batteries a delivering drone could swap back out for <paramref name="incoming"/>.
    /// With the chip run coming (<paramref name="fullOnly"/>) only a FULL battery rides out;
    /// otherwise the substitute must beat the incoming by the margin and hold real energy
    /// (&gt; 1) — never a token trade.</summary>
    int SwappableFor(Battery incoming, Drone forDrone, bool fullOnly)
    {
        int n = 0;
        for (int i = 0; i < slots.Length; i++)
        {
            var b = slots[i];
            if (b == null || b.following || b.IsPulse) continue;
            if (b.hasHome) continue;   // a parked guest is owed back to ITS pad — never a substitute
            if (b.claimedBy != null && b.claimedBy != forDrone) continue;
            if (fullOnly) { if (Full(b)) n++; continue; }
            if (b.energy > 1f && b.energy >= incoming.energy + SwapMargin) n++;
        }
        return n;
    }

    /// <summary>Would sending <paramref name="b"/> here make its building better off? A ready
    /// substitute always justifies the trip (works with zero free slots — the swap frees one).
    /// The extra swap is only WORTH anything while charged batteries are around, so a WORKING
    /// pad's battery (<paramref name="requireSwapOut"/>) without one may still come in to
    /// CHARGE — but only when this station can genuinely feed it (a free bag to run chip, or
    /// juice/chip already at the grinder) and no substitute is in the making: homeless stock
    /// mid-charge means a swap-out IS coming, so the pad waits powered instead of going dark.
    /// Everything else (loose spares, nearly-dead cells) parks whenever charging is possible
    /// and there's room. Without chips and without qualifying stock a battery is best left
    /// where it is — hauling it would just ping-pong it against a starved grinder.</summary>
    public bool CanImprove(Battery b, bool chargingPossible, bool fullOnly, Drone forDrone, bool requireSwapOut)
    {
        int swappable = SwappableFor(b, forDrone, fullOnly);
        if (FreeSlotCount() + swappable - inboundBatteries <= 0) return false;
        if (swappable > 0) return true;
        if (requireSwapOut)
            return (fullOnly || (chargingPossible && SelfFeedPossible())) && !StockChargeInProgress();
        return chargingPossible;
    }

    /// <summary>True stock (homeless) mid-charge on the rack: a substitute is in the making,
    /// so working pads hold their post and wait for the swap rather than parking here.</summary>
    bool StockChargeInProgress()
    {
        for (int i = 0; i < slots.Length; i++)
        {
            var b = slots[i];
            if (b == null || b.hasHome || b.following) continue;
            if (Chargeable(b)) return true;
        }
        return false;
    }

    float selfFeedScanT = float.NegativeInfinity;
    bool selfFeedCached;

    /// <summary>Juice this station can get WITHOUT fleet help: banked juice, chip already
    /// flying in, or loose unclaimed chip sitting inside its own suction ring. (With a free
    /// bag about, FreeBagAvailable covers the hauling case instead.)</summary>
    bool SelfFeedPossible()
    {
        if (juice > 0f || inboundChipSpace > 0) return true;
        if (Time.time - selfFeedScanT < 0.25f) return selfFeedCached;
        selfFeedScanT = Time.time;
        selfFeedCached = false;
        Vector2 pos = eatSpot != null ? (Vector2)eatSpot.position : (Vector2)transform.position;
        for (int k = 0; k < OreChip.all.Count; k++)
        {
            var chip = OreChip.all[k];
            if (chip == null || chip.Absorbing || chip.transform.InDungeon()) continue;
            if (chip.claimedBy != null) continue;
            if (((Vector2)chip.transform.position - pos).sqrMagnitude > suctionRadius * suctionRadius) continue;
            selfFeedCached = true;
            break;
        }
        return selfFeedCached;
    }

    /// <summary>The stock battery a delivering drone takes back out: the fullest qualifying one.
    /// While the chip run is coming (free bag + chip about) only a FULL battery rides out —
    /// partial stock keeps charging. Otherwise the substitute must beat the arrival by the
    /// margin AND hold more than 1 energy, and a part-charged battery whose charging isn't over
    /// (not stamped, grinder not starved) NEVER rides out — taking it early wastes its day.</summary>
    public Battery PickSwapOut(Drone forDrone, Battery incoming)
    {
        bool fullOnly = FreeBagAvailable() && ChargingPossible();
        float floor = incoming != null ? incoming.energy + SwapMargin : 0f;
        bool starved = Starved;
        Battery best = null;
        for (int i = 0; i < slots.Length; i++)
        {
            var b = slots[i];
            if (b == null || b.following || b == incoming) continue;
            if (b.IsPulse) continue;   // player-racked pulse battery: drones never move it
            if (b.hasHome) continue;   // a parked guest rides home to ITS pad, never to another's
            if (b.claimedBy != null && b.claimedBy != forDrone) continue;
            if (fullOnly)
            {
                if (!Full(b)) continue;
            }
            else
            {
                if (b.energy <= 1f || b.energy < floor) continue;
                if (!Full(b) && !b.ChargedToday && !starved) continue;   // still charging — leave it
            }
            if (best == null || b.energy > best.energy) best = b;
        }
        return best;
    }

    /// <summary>A battery lacking energy (not yet charged today, not pulse) that should ride to a
    /// station, plus the station that can genuinely improve it (qualifying stock to swap, or
    /// chips to charge with). Claims NOTHING — the drone claims.</summary>
    public static Battery FindBatteryForCharge(Drone forDrone, out BatteryStation station)
    {
        station = null;
        if (all.Count == 0) return null;
        bool chargingPossible = ChargingPossible();
        bool fullOnly = chargingPossible && FreeBagAvailable();
        int window = SwapWindow;
        Battery best = null;
        BatteryStation bestSt = null;
        float bestSqr = float.MaxValue;
        Vector2 dronePos = forDrone.transform.position;
        for (int k = 0; k < Battery.all.Count; k++)
        {
            var b = Battery.all[k];
            if (b == null || b.transform.InDungeon() || b.following || b == Battery.held) continue;
            if (b.claimedBy != null && b.claimedBy != forDrone) continue;
            if (!Chargeable(b)) continue;
            if (b.pad is BatteryStation) continue;   // already at a station
            // a WORKING pad's battery makes ONE station trip per swap window (wave complete and
            // the dungeon homecoming each open one), and only when a substitute is ready to ride
            // back (requireSwapOut below). Once it's nearly dead (< 1) it goes straight in
            // regardless — hauling it costs the pad nothing.
            bool workingPad = b.pad != null && b.energy >= 1f;
            if (workingPad && b.padSwapWindow == window) continue;
            float d = ((Vector2)b.transform.position - dronePos).sqrMagnitude;
            if (d >= bestSqr) continue;
            // nearest station (to the battery) that can actually improve it
            BatteryStation stBest = null;
            float stSqr = float.MaxValue;
            foreach (var st in all)
            {
                if (st == null || !st.builtYet || !st.enabled) continue;
                if (!st.CanImprove(b, chargingPossible, fullOnly, forDrone, requireSwapOut: workingPad)) continue;
                float sd = ((Vector2)st.transform.position - (Vector2)b.transform.position).sqrMagnitude;
                if (sd < stSqr) { stSqr = sd; stBest = st; }
            }
            if (stBest == null) continue;
            bestSqr = d;
            best = b;
            bestSt = stBest;
        }
        station = bestSt;
        return best;
    }

    /// <summary>Batch pickup (bag drones mid station-run): the next flat battery worth adding to
    /// the load bound for THIS station — nearest chargeable, claimable, off-station battery,
    /// while the rack can still absorb it (inbound reservations included, so the batch is
    /// self-limiting) and charging is actually possible. WORKING pad batteries (energy left)
    /// never batch: batched arrivals park on the rack until charged, which would leave their
    /// pads dark — they travel single-carry so the substitute rides straight back. Claims
    /// nothing — the drone claims.</summary>
    public Battery FindBatteryForBatch(Drone forDrone)
    {
        bool chargingPossible = ChargingPossible();
        bool fullOnly = chargingPossible && FreeBagAvailable();
        Battery best = null;
        float bestSqr = float.MaxValue;
        Vector2 dronePos = forDrone.transform.position;
        for (int k = 0; k < Battery.all.Count; k++)
        {
            var b = Battery.all[k];
            if (b == null || b.transform.InDungeon() || b.following || b == Battery.held) continue;
            if (b.claimedBy != null && b.claimedBy != forDrone) continue;
            if (!Chargeable(b)) continue;
            if (b.pad is BatteryStation) continue;   // already at a station
            if (b.pad != null && b.energy >= 1f) continue;   // working pads are served single-carry
            if (!CanImprove(b, chargingPossible, fullOnly, forDrone, requireSwapOut: false)) continue;
            float d = ((Vector2)b.transform.position - dronePos).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; best = b; }
        }
        return best;
    }

    /// <summary>A settled, unclaimed base-side chip worth hauling to a station that wants juice.
    /// Chips already inside a station's suction ring are left alone — the grinder has them.</summary>
    public static OreChip FindChipForStation(Drone forDrone, out BatteryStation station)
    {
        station = null;
        BatteryStation want = null;
        foreach (var st in all)
        {
            if (st == null || !st.builtYet || !st.enabled) continue;
            if (st.JuiceDemand - st.inboundChipSpace * st.juicePerChipSpace > 0f) { want = st; break; }
        }
        if (want == null) return null;
        OreChip best = null;
        float bestSqr = float.MaxValue;
        Vector2 dronePos = forDrone.transform.position;
        for (int k = 0; k < OreChip.all.Count; k++)
        {
            var chip = OreChip.all[k];
            if (chip == null || chip.Absorbing || chip.transform.InDungeon()) continue;
            if (chip.claimedBy != null && chip.claimedBy != forDrone) continue;
            if (chip.Age < OreChip.SettleSeconds) continue;
            if (InsideAnySuction(chip.transform.position)) continue;
            float d = ((Vector2)chip.transform.position - dronePos).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; best = chip; }
        }
        station = want;
        return best;
    }

    static bool InsideAnySuction(Vector2 p)
    {
        foreach (var st in all)
        {
            if (st == null || !st.builtYet) continue;
            if (((Vector2)st.transform.position - p).sqrMagnitude <= st.suctionRadius * st.suctionRadius)
                return true;
        }
        return false;
    }

    /// <summary>A slotted battery whose station visit is over — full, already stamped for today,
    /// or the grinder is starved — and that has a home to go back to.</summary>
    public static Battery FindBatteryToReturn(Drone forDrone, out BatteryStation station)
    {
        station = null;
        foreach (var st in all)
        {
            if (st == null || !st.builtYet || !st.enabled) continue;
            bool starved = st.Starved;
            for (int i = 0; i < st.slots.Length; i++)
            {
                var b = st.slots[i];
                if (b == null || !b.hasHome || b.following || b.IsPulse) continue;   // pulse: player-placed, stays put
                if (b.claimedBy != null && b.claimedBy != forDrone) continue;
                bool done = b.energy >= b.maxEnergy - 1e-3f || b.ChargedToday || starved;
                if (!done) continue;
                station = st;
                return b;
            }
        }
        return null;
    }
}
