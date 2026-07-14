using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A building that EATS ore chips. The Battery Station (chips → juice) is the first;
/// chip-built walls, the ammunition turrets and the ember refiner are expected to implement
/// this and register in BEnable/BDisable — the bag-drone fleet (the only chip carriers) then
/// gathers for them, delivers to them and skips chips they can't use, with no drone changes.
///
/// The contract:
///  - <see cref="AcceptsChip"/> is the intake gate. Drones never fetch or dump a chip the
///    consumer won't eat (a tier-0 station is blind to medium/large chips; a refiner can gate
///    on element, a wall on plain rock).
///  - <see cref="ChipDemandSpace"/> is the GROSS want in bag-space units (small 1 / medium 2 /
///    large 4), net of any banked stock but ignoring chips in flight — logistics nets off
///    <see cref="InboundChipSpace"/> itself.
///  - Settled chips inside <see cref="ChipIntakeRadius"/> of an accepting consumer belong to
///    that consumer: drones leave them alone, so whatever lands in the ring (dumped hauls,
///    player sweeps) must be consumed by the building's own intake (the station's suction).
/// </summary>
public interface IChipConsumer
{
    /// <summary>Built and running — may be fed. Dead/ghost/unbuilt consumers drop off the board.</summary>
    bool ChipIntakeActive { get; }

    /// <summary>Where hauls land. Drones fly here and spill; the consumer's intake takes over.</summary>
    Vector2 ChipDropPoint { get; }

    /// <summary>Approach/ownership ring around the drop point (see class doc).</summary>
    float ChipIntakeRadius { get; }

    /// <summary>How much this consumer VALUES an accepted chip (0 = fallback food it will eat
    /// but barely wants; higher = a real input — ore to the refiner, ammo to a turret). The
    /// fleet RESERVES each chip for the highest-appeal customer still hungry for it, so a
    /// grinder never burns ore a refiner is waiting on; customers of equal appeal split the
    /// supply evenly (see ChipConsumers). Only consulted for chips that pass
    /// <see cref="AcceptsChip"/>.</summary>
    int ChipAppeal(int sizeClass, int element);

    /// <summary>Bag-space units of chip still wanted, gross of in-flight (see class doc).</summary>
    float ChipDemandSpace { get; }

    /// <summary>Bag-space units in drone bags currently bound here (logistics bookkeeping).</summary>
    int InboundChipSpace { get; set; }

    /// <summary>The intake gate: may this consumer eat a chip of this size class (0 small /
    /// 1 medium / 2 large) and element (-1 plain rock, else 0..3 white/green/blue/red)?</summary>
    bool AcceptsChip(int sizeClass, int element);
}

/// <summary>
/// Fleet-side chip logistics: the consumer registry, the hive-mind FAIR-SHARE ledger, and the
/// shared pickup rule. Allocation works in two layers:
///  - WHO eats next: the fleet feeds whichever hungry customer has been served the least chip
///    lately (bag-space units on a decaying ledger every drone reads and credits — one
///    allocator, however many drones), nearest breaking near-ties.
///  - WHICH chip: contested chip is reserved for its keenest customer
///    (<see cref="IChipConsumer.ChipAppeal"/>), and within what a customer may take the rule
///    stays LARGEST accepted size class first (more value per bag space and per daily haul
///    quota), nearest chip within a class — all in <see cref="FindChipFor"/> so every consumer
///    inherits it.
/// </summary>
public static class ChipConsumers
{
    public static readonly List<IChipConsumer> all = new List<IChipConsumer>();
    static readonly List<IChipConsumer> scratch = new List<IChipConsumer>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRegistry()   // no-domain-reload: statics survive play-stop
    {
        all.Clear();
        scratch.Clear();
        served.Clear();
    }

    public static void Register(IChipConsumer c) { if (!all.Contains(c)) all.Add(c); }
    public static void Unregister(IChipConsumer c) { all.Remove(c); served.Remove(c); }

    // ---------------------------------------------------------------- fair-share ledger
    // Every chip a drone swallows for a customer is credited here in bag-space units, decaying
    // with a half-life so "even" means even inflow LATELY, not all-time totals (a station that
    // gorged on exclusive large chip an hour ago isn't starved forever). The ledger is fleet
    // state, not drone state — that's the hive mind.

    const float ServedHalfLife = 90f;
    static readonly Dictionary<IChipConsumer, (float space, float stamp)> served
        = new Dictionary<IChipConsumer, (float, float)>();

    /// <summary>Bag-space of chip the fleet has hauled to this customer lately (decayed).</summary>
    public static float ServedSpace(IChipConsumer c)
        => served.TryGetValue(c, out var s)
            ? s.space * Mathf.Pow(0.5f, (Time.time - s.stamp) / ServedHalfLife)
            : 0f;

    /// <summary>Called at the swallow (the moment supply is committed to this customer).</summary>
    public static void CreditServed(IChipConsumer c, float space)
        => served[c] = (ServedSpace(c) + space, Time.time);

    /// <summary>Alive (Unity fake-null aware — consumers are components) and taking chips.</summary>
    public static bool Active(IChipConsumer c)
        => c is Component m && m != null && c.ChipIntakeActive;

    /// <summary>Want still unserved once every chip already flying here lands.</summary>
    public static float NetDemandSpace(IChipConsumer c)
        => c.ChipDemandSpace - c.InboundChipSpace;

    /// <summary>Would any active consumer eat this chip? (The dungeon sweep uses this to grab
    /// edible debris first — inedible chips still come home last as stockpile.)</summary>
    public static bool AnyoneAccepts(int sizeClass, int element)
    {
        for (int k = 0; k < all.Count; k++)
        {
            var c = all[k];
            if (Active(c) && c.AcceptsChip(sizeClass, element)) return true;
        }
        return false;
    }

    /// <summary>Chip already sitting in the ring of a consumer that will eat it — that
    /// consumer's own intake has it, the fleet keeps its hands off. A chip a ring-owner
    /// can't eat (a large chip beside a tier-0 station) stays fair game.</summary>
    public static bool AtAnIntake(OreChip chip)
    {
        Vector2 p = chip.transform.position;
        for (int k = 0; k < all.Count; k++)
        {
            var c = all[k];
            if (!Active(c) || !c.AcceptsChip(chip.sizeClass, chip.element)) continue;
            float r = c.ChipIntakeRadius;
            if ((c.ChipDropPoint - p).sqrMagnitude <= r * r) return true;
        }
        return false;
    }

    /// <summary>May this customer be GIVEN a chip of this class right now? It must accept it and
    /// no keener hungry customer may have dibs (<see cref="TopAppealFor"/>). BOTH ends of a haul
    /// run this gate — the pickup search and the bag-dump — so what a drone fetches and what it
    /// spills always agree: ore rides past a grinder's stop while a refiner is waiting on it.</summary>
    public static bool MayGive(IChipConsumer c, int sizeClass, int element)
        => c.AcceptsChip(sizeClass, element)
           && c.ChipAppeal(sizeClass, element) >= TopAppealFor(sizeClass, element);

    /// <summary>The keenest appetite any HUNGRY customer has for this chip — a consumer may
    /// only take a chip it wants at least this much (contested chip goes to whoever values it
    /// most; the grinder sees ore only once every hungry refiner is out of the bidding).</summary>
    public static int TopAppealFor(int sizeClass, int element)
    {
        int top = 0;
        for (int k = 0; k < all.Count; k++)
        {
            var c = all[k];
            if (!Active(c) || NetDemandSpace(c) <= 0f) continue;
            if (!c.AcceptsChip(sizeClass, element)) continue;
            int a = c.ChipAppeal(sizeClass, element);
            if (a > top) top = a;
        }
        return top;
    }

    /// <summary>The fleet pickup rule for one consumer: among settled, unclaimed, base-side
    /// chips the consumer accepts, the bag can hold and no keener customer has dibs on, take
    /// what this customer values MOST (appeal), the LARGEST size class within that, nearest
    /// first within a class. Claims nothing — the drone claims.</summary>
    public static OreChip FindChipFor(IChipConsumer c, Drone forDrone, int spaceLeft)
    {
        OreChip best = null;
        int bestAppeal = int.MinValue;
        int bestSize = -1;
        float bestSqr = float.MaxValue;
        Vector2 pos = forDrone.transform.position;
        for (int k = 0; k < OreChip.all.Count; k++)
        {
            var chip = OreChip.all[k];
            if (chip == null || chip.Absorbing || chip.transform.InDungeon()) continue;
            if (chip.claimedBy != null && chip.claimedBy != forDrone) continue;
            if (chip.Age < OreChip.SettleSeconds) continue;
            if (Time.time < chip.unreachableUntil) continue;   // a drone recently failed to reach it
            if (chip.SpaceCost > spaceLeft) continue;
            if (!MayGive(c, chip.sizeClass, chip.element)) continue;   // can't eat it, or a keener customer has dibs
            int appeal = c.ChipAppeal(chip.sizeClass, chip.element);
            if (appeal < bestAppeal) continue;
            if (appeal == bestAppeal && chip.sizeClass < bestSize) continue;
            float d = ((Vector2)chip.transform.position - pos).sqrMagnitude;
            if (appeal == bestAppeal && chip.sizeClass == bestSize && d >= bestSqr) continue;
            if (AtAnIntake(chip)) continue;
            best = chip; bestAppeal = appeal; bestSize = chip.sizeClass; bestSqr = d;
        }
        return best;
    }

    /// <summary>Job-board entry: the hungry customer the FLEET owes the most chip — least
    /// served lately on the fair-share ledger, nearest to the drone within a near-tie — that
    /// has a fetchable chip. Claims nothing — the drone claims.</summary>
    public static OreChip FindChipJob(Drone forDrone, int spaceLeft, out IChipConsumer consumer)
    {
        consumer = null;
        if (all.Count == 0 || spaceLeft <= 0) return null;
        scratch.Clear();
        for (int k = 0; k < all.Count; k++)
        {
            var c = all[k];
            if (!Active(c) || NetDemandSpace(c) <= 0f) continue;
            scratch.Add(c);
        }
        if (scratch.Count == 0) return null;
        Vector2 pos = forDrone.transform.position;
        // selection-sort in place (consumers number a handful — no allocation, no comparer)
        for (int i = 0; i < scratch.Count - 1; i++)
            for (int j = i + 1; j < scratch.Count; j++)
                if (ServeFirst(scratch[j], scratch[i], pos))
                    (scratch[i], scratch[j]) = (scratch[j], scratch[i]);
        for (int k = 0; k < scratch.Count; k++)
        {
            var chip = FindChipFor(scratch[k], forDrone, spaceLeft);
            if (chip == null) continue;
            consumer = scratch[k];
            return chip;
        }
        return null;
    }

    /// <summary>Fair share first: the customer served the least chip lately eats next. Near-ties
    /// (within a small chip of each other) go to the nearest, so equal-hunger customers don't
    /// flip-flop drones across the map. Shared by the job board AND the bag-dump detour, so the
    /// whole fleet ranks customers one way.</summary>
    public static bool ServeFirst(IChipConsumer a, IChipConsumer b, Vector2 pos)
    {
        float sa = ServedSpace(a), sb = ServedSpace(b);
        if (Mathf.Abs(sa - sb) > 0.5f) return sa < sb;
        return (a.ChipDropPoint - pos).sqrMagnitude < (b.ChipDropPoint - pos).sqrMagnitude;
    }
}
