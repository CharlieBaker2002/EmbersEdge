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

    /// <summary>Job-board rank when several consumers want chips: higher is served first
    /// (ammunition before the grinder, say). Ties go to the nearest.</summary>
    int ChipPriority { get; }

    /// <summary>Bag-space units of chip still wanted, gross of in-flight (see class doc).</summary>
    float ChipDemandSpace { get; }

    /// <summary>Bag-space units in drone bags currently bound here (logistics bookkeeping).</summary>
    int InboundChipSpace { get; set; }

    /// <summary>The intake gate: may this consumer eat a chip of this size class (0 small /
    /// 1 medium / 2 large) and element (-1 plain rock, else 0..3 white/green/blue/red)?</summary>
    bool AcceptsChip(int sizeClass, int element);
}

/// <summary>
/// Fleet-side chip logistics: the consumer registry plus the shared pickup rule. The rule the
/// whole fleet follows — LARGEST accepted size class first (more value per bag space and per
/// daily haul quota), nearest chip within a class — lives in <see cref="FindChipFor"/> so every
/// consumer inherits it.
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
    }

    public static void Register(IChipConsumer c) { if (!all.Contains(c)) all.Add(c); }
    public static void Unregister(IChipConsumer c) => all.Remove(c);

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

    /// <summary>The fleet pickup rule for one consumer: among settled, unclaimed, base-side
    /// chips the consumer accepts and the bag can hold, take the LARGEST size class going,
    /// nearest first within a class. Claims nothing — the drone claims.</summary>
    public static OreChip FindChipFor(IChipConsumer c, Drone forDrone, int spaceLeft)
    {
        OreChip best = null;
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
            if (!c.AcceptsChip(chip.sizeClass, chip.element)) continue;
            if (chip.sizeClass < bestSize) continue;
            float d = ((Vector2)chip.transform.position - pos).sqrMagnitude;
            if (chip.sizeClass == bestSize && d >= bestSqr) continue;
            if (AtAnIntake(chip)) continue;
            best = chip; bestSize = chip.sizeClass; bestSqr = d;
        }
        return best;
    }

    /// <summary>Job-board entry: the consumer most worth serving — highest
    /// <see cref="IChipConsumer.ChipPriority"/>, nearest to the drone within a rank — that has
    /// net demand AND a fetchable chip. Claims nothing — the drone claims.</summary>
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
                if (Outranks(scratch[j], scratch[i], pos))
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

    static bool Outranks(IChipConsumer a, IChipConsumer b, Vector2 pos)
    {
        if (a.ChipPriority != b.ChipPriority) return a.ChipPriority > b.ChipPriority;
        return (a.ChipDropPoint - pos).sqrMagnitude < (b.ChipDropPoint - pos).sqrMagnitude;
    }
}
