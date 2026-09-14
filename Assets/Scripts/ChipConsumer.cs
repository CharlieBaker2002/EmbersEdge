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
    /// 1 medium / 2 large)? `element` is 0 for every chip now (one ore — the era's); -1 was
    /// the legacy plain-rock chip.</summary>
    bool AcceptsChip(int sizeClass, int element);

    /// <summary>True for a consumer that must not be given chip already stamped
    /// <see cref="OreChip.refined"/> (the Refiner: a chip is split at most once). Default false.</summary>
    bool RefusesRefined => false;

    /// <summary>Take a chip handed over DIRECTLY — a Belt letting go at its tail, a Collector or
    /// Tube beside this building. The chip is loose, at rest, on this building's doorstep and
    /// already passed <see cref="AcceptsChip"/>; return true once it is taken in (swallowed,
    /// shelved), false to leave it for the ordinary ring suction. Default false.</summary>
    bool TakeDelivered(OreChip chip) => false;
}

/// <summary>
/// Fleet-side chip logistics: the consumer registry, the hive-mind FAIR-SHARE ledger, and the
/// shared pickup rule. Allocation works in two layers:
///  - WHO eats next (user rule 2026-09-13): customers are fed IN THE ORDER THEY WERE BUILT —
///    the earliest-placed hungry building takes every chip it may until it's served, then the
///    next (<see cref="ServeFirst"/>; Building.placedIndex). One rule for every drone, so the
///    fleet completes sites one at a time instead of spreading chip thin. (The served-space
///    ledger is still credited for diagnostics but no longer ranks anyone.) SHELVING IS LAST:
///    a Tube never outranks a real customer, it's a separate job-board leg a drone takes
///    only when nothing else is on offer (<see cref="FindChipJob"/> shelving), and between
///    shelves the one nearest the base centre fills first.
///  - WHICH chip: contested chip is reserved for its keenest customer
///    (<see cref="IChipConsumer.ChipAppeal"/>), and within what a customer may take the rule
///    stays LARGEST accepted size class first (more value per bag space and per daily haul
///    quota), nearest chip within a class — all in <see cref="FindChipFor"/> so every consumer
///    inherits it.
///  - WHERE from (user rule 2026-09-13): LOOSE chip first, always — anything unstored and
///    settled at base; only when none fits does a customer draw on the Tube shelves, and
///    then the shelf nearest the BASE CENTRE is drained before any farther one.
/// </summary>
public static class ChipConsumers
{
    public static readonly List<IChipConsumer> all = new List<IChipConsumer>();
    /// <summary>The base is the disc round the world origin (PathZone.AtBase) — "closer to
    /// base" for a chip source means closer to this point.</summary>
    public static readonly Vector2 BaseCentre = Vector2.zero;
    static readonly List<IChipConsumer> scratch = new List<IChipConsumer>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRegistry()   // no-domain-reload: statics survive play-stop
    {
        all.Clear();
        scratch.Clear();
        served.Clear();
        System.Array.Fill(topCacheFrame, -1);   // a stale frame stamp would match an early frame of the next play
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

    /// <summary>Chip already sitting in the ring of a consumer that may EAT it — that
    /// consumer's own intake has it, the fleet keeps its hands off. MAY, not can: the ring
    /// owner passes the same dibs gate as everyone else (<see cref="MayGive"/>), so a chip a
    /// ring-owner can't eat (a large chip beside a tier-0 station), a chip a keener hungry
    /// customer is waiting on (ore beside a grinder while a refiner hungers), or any chip
    /// beside an intake whose appetite is spent, stays fair game for the fleet.</summary>
    public static bool AtAnIntake(OreChip chip) => AtAnIntake(chip, null);

    /// <summary>Same test, ignoring the boxes of one Tube shape (its own intake asking
    /// whether anyone ELSE owns a chip in its ring).</summary>
    public static bool AtAnIntake(OreChip chip, TubeCluster except)
    {
        Vector2 p = chip.transform.position;
        for (int k = 0; k < all.Count; k++)
        {
            var c = all[k];
            if (except != null && c is Tube store && store.cluster == except) continue;
            if (!Active(c) || !MayGive(c, chip.sizeClass, chip.element, chip.refined)) continue;
            float r = c.ChipIntakeRadius;
            if ((c.ChipDropPoint - p).sqrMagnitude <= r * r) return true;
        }
        return false;
    }

    /// <summary>May this customer be GIVEN a chip of this class right now? It must accept it and
    /// no keener hungry customer may have dibs (<see cref="TopAppealFor"/>). BOTH ends of a haul
    /// run this gate — the pickup search and the bag-dump — so what a drone fetches and what it
    /// spills always agree: ore rides past a grinder's stop while a refiner is waiting on it.
    /// <paramref name="refined"/>: a chip out of a Refiner split — a customer that refuses
    /// refined chip neither takes it nor holds dibs on it (a hungry refiner must not send
    /// its own output to the scrap pile).</summary>
    public static bool MayGive(IChipConsumer c, int sizeClass, int element, bool refined = false)
        => c.AcceptsChip(sizeClass, element)
           && !(refined && c.RefusesRefined)
           && c.ChipAppeal(sizeClass, element) >= TopAppealFor(sizeClass, element, refined);

    /// <summary>The keenest appetite any HUNGRY customer has for this chip — a consumer may
    /// only take a chip it wants at least this much (contested chip goes to whoever values it
    /// most; the grinder sees ore only once every hungry refiner is out of the bidding).</summary>
    // PERF (2026-09-13 profiler): this is asked per CHIP per consumer per drone per tick — with
    // 300 loose chips and 40 consumers it was the inner loop of a 10 ms Drone.Brain step. The
    // answer only moves when a claim lands, so it's cached per frame per (size, element, refined).
    const int TopKeys = 3 * 5 * 2;
    static readonly int[] topCache = new int[TopKeys];
    static readonly int[] topCacheFrame = new int[TopKeys];
    public static int TopAppealFor(int sizeClass, int element, bool refined = false)
    {
        int key = sizeClass >= 0 && sizeClass < 3 && element >= -1 && element < 4
            ? (sizeClass * 5 + element + 1) * 2 + (refined ? 1 : 0) : -1;
        int frame = Time.frameCount;
        if (key >= 0 && topCacheFrame[key] == frame) return topCache[key];
        int top = 0;
        for (int k = 0; k < all.Count; k++)
        {
            var c = all[k];
            if (!Active(c) || NetDemandSpace(c) <= 0f) continue;
            if (!c.AcceptsChip(sizeClass, element)) continue;
            if (refined && c.RefusesRefined) continue;
            int a = c.ChipAppeal(sizeClass, element);
            if (a > top) top = a;
        }
        if (key >= 0) { topCache[key] = top; topCacheFrame[key] = frame; }
        return top;
    }

    /// <summary>A Tube box that is NOT its shape's host: the host box bids for the whole
    /// shelf, so a 31-box shape is one customer on the board, not 31 identical scans.</summary>
    public static bool IsNonHostStoreBox(IChipConsumer c) => c is Tube s && s.cluster != null && s.cluster.host != s;

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
            if (chip.refined && c.RefusesRefined) continue;             // split once only
            if (!MayGive(c, chip.sizeClass, chip.element, chip.refined)) continue;   // can't eat it, or a keener customer has dibs
            int appeal = c.ChipAppeal(chip.sizeClass, chip.element);
            if (appeal < bestAppeal) continue;
            if (appeal == bestAppeal && chip.sizeClass < bestSize) continue;
            float d = ((Vector2)chip.transform.position - pos).sqrMagnitude;
            if (appeal == bestAppeal && chip.sizeClass == bestSize && d >= bestSqr) continue;
            if (AtAnIntake(chip)) continue;
            best = chip; bestAppeal = appeal; bestSize = chip.sizeClass; bestSqr = d;
        }
        if (best != null) return best;

        // Nothing loose fits: a REAL customer (appeal > 0 — construction, refiner, walls, the
        // grinders) may draw on the Tube shelves. Stores never restock from each other,
        // and loose chips always go first (they're the ones the next clear cycle eats).
        // SHELF ORDER (user rule 2026-09-13): the shape nearest the BASE CENTRE is drained
        // first — a farther shelf gives nothing while a nearer one still holds a chip this
        // customer may take; on one shelf the usual largest-then-nearest-to-the-drone pick.
        var shelves = Tube.Clusters;
        float bestSrc = float.MaxValue;   // the winning shelf's distance² from the base centre
        for (int i = 0; i < shelves.Count; i++)
        {
            var shelf = shelves[i];
            var list = shelf.chips;
            if (list.Count == 0) continue;
            float src = (shelf.centroid - BaseCentre).sqrMagnitude;
            for (int k = 0; k < list.Count; k++)
            {
                var chip = list[k].chip;
                if (chip == null || chip.Absorbing || chip.Fading) continue;
                if (chip.claimedBy != null && chip.claimedBy != forDrone) continue;
                if (Time.time < chip.unreachableUntil) continue;
                if (chip.SpaceCost > spaceLeft) continue;
                if (chip.refined && c.RefusesRefined) continue;
                if (!MayGive(c, chip.sizeClass, chip.element, chip.refined)) continue;
                int appeal = c.ChipAppeal(chip.sizeClass, chip.element);
                if (appeal <= 0) continue;
                if (appeal < bestAppeal) continue;
                if (appeal == bestAppeal)
                {
                    if (src > bestSrc) continue;                                   // a nearer shelf already offers
                    if (src == bestSrc && chip.sizeClass < bestSize) continue;
                }
                float d = ((Vector2)chip.transform.position - pos).sqrMagnitude;
                if (appeal == bestAppeal && src == bestSrc && chip.sizeClass == bestSize && d >= bestSqr) continue;
                best = chip; bestAppeal = appeal; bestSrc = src; bestSize = chip.sizeClass; bestSqr = d;
            }
        }
        return best;
    }

    /// <summary>Job-board entry: the hungry customer the FLEET owes the most chip — least
    /// served lately on the fair-share ledger, nearest to the drone within a near-tie — that
    /// has a fetchable chip. Claims nothing — the drone claims. <paramref name="constructionOnly"/>
    /// restricts the board to ghost buildings (GhostIntake) — what a bare, bagless drone may
    /// carry chip to. Tubes are OFF this board unless <paramref name="shelving"/> — then
    /// it's ONLY them (the least important leg, nearest-the-base-centre shelf first).</summary>
    public static OreChip FindChipJob(Drone forDrone, int spaceLeft, out IChipConsumer consumer, bool constructionOnly = false, bool shelving = false)
    {
        consumer = null;
        if (all.Count == 0 || spaceLeft <= 0) return null;
        scratch.Clear();
        for (int k = 0; k < all.Count; k++)
        {
            var c = all[k];
            if (!Active(c) || NetDemandSpace(c) <= 0f) continue;
            if (constructionOnly && !(c is GhostIntake)) continue;
            if ((c is Tube) != shelving) continue;
            if (IsNonHostStoreBox(c)) continue;   // one bid per shelf (PERF)
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

    /// <summary>BUILD ORDER (user rule 2026-09-13): the earlier-placed customer eats first;
    /// equal (or unknown) order goes to the nearest. Shared by the job board AND the bag-dump
    /// detour, so the whole fleet ranks customers one way.</summary>
    public static bool ServeFirst(IChipConsumer a, IChipConsumer b, Vector2 pos)
    {
        // Shelving is the LAST chip job (user rule 2026-09-13): any real customer outranks a
        // Tube, and between shelves the one nearest the base centre fills first.
        bool storeA = a is Tube, storeB = b is Tube;
        if (storeA != storeB) return storeB;
        if (storeA) return ShelfDistSqr(a) < ShelfDistSqr(b);
        int oa = BuildOrder(a), ob = BuildOrder(b);
        if (oa != ob) return oa < ob;
        return (a.ChipDropPoint - pos).sqrMagnitude < (b.ChipDropPoint - pos).sqrMagnitude;
    }

    /// <summary>The customer's building's placement order (a construction intake ranks by the
    /// building it's raising); anything unranked goes last.</summary>
    static int BuildOrder(IChipConsumer c)
        => c is Building b ? b.placedIndex
         : c is GhostIntake g && g.Owner != null ? g.Owner.placedIndex
         : int.MaxValue;

    /// <summary>A store's shape distance² from the base centre (a box without a cluster ranks last).</summary>
    static float ShelfDistSqr(IChipConsumer c)
        => c is Tube s && s.cluster != null ? (s.cluster.centroid - BaseCentre).sqrMagnitude : float.MaxValue;

    // ---------------------------------------------------------------- footprints (direct handovers)

    /// <summary>The building a consumer stands for (a construction intake ranks as the building it raises).</summary>
    public static Building BuildingOf(IChipConsumer c)
    {
        if (c is Building b) return b;
        if (c is GhostIntake g && g != null) return g.Owner;
        return null;
    }

    /// <summary>Two axis-aligned boxes (centre + half-extents) sharing an EDGE — touching side
    /// to side, not corner to corner, not overlapping.</summary>
    public static bool AabbAdjacent(Vector2 ca, Vector2 ha, Vector2 cb, Vector2 hb)
    {
        const float eps = 0.02f;
        float dx = Mathf.Abs(ca.x - cb.x), dy = Mathf.Abs(ca.y - cb.y);
        float sx = ha.x + hb.x, sy = ha.y + hb.y;
        bool touchX = Mathf.Abs(dx - sx) <= eps && dy < sy - eps;
        bool touchY = Mathf.Abs(dy - sy) <= eps && dx < sx - eps;
        return touchX || touchY;
    }

    /// <summary>A base cell (centre given) sharing an edge with a building's footprint.</summary>
    public static bool FootprintAdjacent(Building b, Vector2 cellCentre)
        => b != null && AabbAdjacent(b.transform.position, b.size * 0.5f, cellCentre, Vector2.one * (BaseCell.Cs * 0.5f));

    /// <summary>Two buildings' footprints sharing an edge.</summary>
    public static bool FootprintsAdjacent(Building a, Building b)
        => a != null && b != null && AabbAdjacent(a.transform.position, a.size * 0.5f, b.transform.position, b.size * 0.5f);

    /// <summary>A point inside a building's footprint.</summary>
    public static bool FootprintContains(Building b, Vector2 p)
    {
        if (b == null) return false;
        Vector2 c = b.transform.position, h = b.size * 0.5f;
        return Mathf.Abs(p.x - c.x) <= h.x + 1e-3f && Mathf.Abs(p.y - c.y) <= h.y + 1e-3f;
    }
}
