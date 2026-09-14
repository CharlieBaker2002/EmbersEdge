using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One run of linked Belt tiles — THE unit chips ride in. Built and torn down by Belt.Rebuild
/// (every tile enable/disable/spin), ticked by its host tile (the tail).
///
/// The line is a lockstep machine. Every chip sits on a tile (up to chipsPerTile to a tile, in a
/// tight cluster) or is sliding onto the next. A step begins only when (a) nothing is at the end OR the
/// building there is currently accepting chip (a chip consumer with gross want > 0 and its
/// intake live — a full crush generator, an unpowered Tube, a construction site whose count is
/// met all stop the line; a Collector with a full pile too), and (b) the pooled sources
/// touching any tile have paid energyPerStep for EVERY chip that will move (rate-paced: the
/// bucket fills against the grid's per-frame caps, the step fires when it holds the bill).
/// Then every mover slides one tile forward over stepSeconds, the tiles' chevrons scroll
/// together, and the tail's chips cross onto the exit cell — fed straight into the building
/// there (<see cref="IChipConsumer.TakeDelivered"/>; else set down inside its ring for its own
/// suction, zero scatter), set down for a Collector's pull, or just set down when nothing wants
/// them. Chips whose next tile is full (a merge from a side branch, a ring) wait their turn;
/// the rest still move.
///
/// Getting on: a chip whose centre lands ON a tile (direct contact — the Hoover's spray, a
/// nudge) is taken aboard while the tile has room, mid-step included; a Collector beside a
/// tile pointing away from it loads the tile itself. Nothing else — no ring, no drones.
/// </summary>
public class BeltLine
{
    public class Entry
    {
        public OreChip chip;
        /// <summary>The tile the chip sits on — or, mid-step, the one it is sliding onto.</summary>
        public Belt tile;
        public Vector2 from;
        public bool moving, exiting;
        /// <summary>0..1 easing onto the tile after getting on (or a re-link); 1 = seated.</summary>
        public float landT = 1f;
        /// <summary>Which spot of the tile's cluster it rides in.</summary>
        public int slot;
    }

    public readonly List<Belt> members = new List<Belt>();
    /// <summary>Tail first, then upstream (BFS over feeders) — a ring keeps member order.</summary>
    public readonly List<Belt> order = new List<Belt>();
    public readonly List<Belt> heads = new List<Belt>();
    /// <summary>The tile pointing at ground / a building (null for a closed ring).</summary>
    public Belt tail;
    /// <summary>The tile that ticks and gauges for the line.</summary>
    public Belt host;
    public readonly List<Entry> chips = new List<Entry>();
    public Vector2 boundsMin, boundsMax;
    public SourcePool power;

    // the building at the end: an IChipConsumer, a Collector, or null
    object end;
    float endT = float.NegativeInfinity;
    float stepT = -1f, bucket, frameT, contactT;
    int frameShown;
    readonly List<Entry> movers = new List<Entry>();
    readonly Dictionary<Belt, int> incoming = new Dictionary<Belt, int>();
    static readonly Queue<Belt> bfs = new Queue<Belt>();
    static readonly HashSet<Belt> seen = new HashSet<Belt>();
    const float LandSeconds = 0.15f;

    public bool Stepping => stepT >= 0f;
    /// <summary>Where the tail lets go: the centre of the cell beyond it.</summary>
    public Vector2 ExitPoint => tail != null ? tail.Centre + (Vector2)tail.Dir * BaseCell.Cs : Vector2.zero;
    public int Capacity => members.Count * (host != null ? Mathf.Max(0, host.chipsPerTile) : 0);
    public int Free => Mathf.Max(0, Capacity - chips.Count);

    /// <summary>What the line feeds: the chip consumer or Collector at its exit cell (never
    /// gated on appetite — a full one still counts, it just stops the line until it has room).</summary>
    public object End
    {
        get { RefreshEnd(); return end; }
    }

    // ------------------------------------------------------------------ build / teardown

    /// <summary>Members are in and linked — derive the tail, heads, order, host and bounds.</summary>
    public void Finish()
    {
        tail = null;
        heads.Clear();
        order.Clear();
        for (int k = 0; k < members.Count; k++)
        {
            var m = members[k];
            if (m.next == null) tail = m;
            if (m.prev.Count == 0) heads.Add(m);
        }
        bfs.Clear();
        seen.Clear();
        if (tail != null)
        {
            bfs.Enqueue(tail);
            seen.Add(tail);
            while (bfs.Count > 0)
            {
                var cur = bfs.Dequeue();
                order.Add(cur);
                for (int p = 0; p < cur.prev.Count; p++)
                    if (seen.Add(cur.prev[p])) bfs.Enqueue(cur.prev[p]);
            }
        }
        for (int k = 0; k < members.Count; k++) if (seen.Add(members[k])) order.Add(members[k]);
        host = tail != null ? tail : members.Count > 0 ? members[0] : null;
        float cs = BaseCell.Cs;
        boundsMin = new Vector2(float.MaxValue, float.MaxValue);
        boundsMax = new Vector2(float.MinValue, float.MinValue);
        for (int k = 0; k < members.Count; k++)
        {
            var c = members[k].cell;
            boundsMin = Vector2.Min(boundsMin, (Vector2)c * cs);
            boundsMax = Vector2.Max(boundsMax, (Vector2)(c + Vector2Int.one) * cs);
        }
        if (members.Count == 0) boundsMin = boundsMax = Vector2.zero;
        power = new SourcePool(members);
        for (int k = 0; k < members.Count; k++)
        {
            members[k].line = this;
            members[k].ShowFrame(frameShown);
        }
    }

    /// <summary>Line being replaced (a tile came or went): unbind. Chips are migrated by
    /// Belt.Rebuild before this is called.</summary>
    public void Dispose()
    {
        if (host != null) host.ClearDrawImmediate();   // the new line's host reports from here on
        for (int k = 0; k < members.Count; k++)
        {
            var m = members[k];
            if (m == null) continue;
            if (m.line == this) m.line = null;
            m.occupants.Clear();
        }
    }

    /// <summary>A chip back on the tile it rode, in this (new) line: seated, eased into place.
    /// The chip's holder is re-pointed at THIS line — Prune drops any entry whose chip belongs
    /// to another holder, so an adopted chip still stamped with the dissolved line would be
    /// orphaned on the spot: stored, off every registry, never moving again (the 2026-09-14
    /// "chip stuck on the belt while others pass it" bug).</summary>
    public void Adopt(Entry e)
    {
        e.moving = false;
        e.exiting = false;
        e.from = e.chip.transform.position;
        e.landT = 0f;
        e.slot = FreeSlot(e.tile);
        e.chip.stored = this;
        e.tile.occupants.Add(e);
        chips.Add(e);
    }

    /// <summary>A riding chip whose tile is gone: back to the world where it is, no scatter.</summary>
    public static void SetDown(Entry e)
    {
        if (e.chip == null || e.chip.Absorbing || e.chip.Fading) return;
        e.chip.Unstore();
        Still(e.chip);
    }

    static void Still(OreChip chip)
    {
        if (chip.rb == null) return;
        chip.rb.linearVelocity = Vector2.zero;
        chip.rb.angularVelocity = 0f;
    }

    static int FreeSlot(Belt tile)
    {
        for (int s = 0; s < 64; s++)
        {
            bool used = false;
            for (int k = 0; k < tile.occupants.Count; k++) if (tile.occupants[k].slot == s) { used = true; break; }
            if (!used) return s;
        }
        return tile.occupants.Count;
    }

    /// <summary>A loose chip gets on a tile (contact, or a Collector beside it) while the tile
    /// has room — mid-step too: the tile's count already includes the chips sliding onto it, the
    /// newcomer just seats itself (landT) and joins the NEXT step. (Loading only between steps
    /// starved a busy line: a back-to-back stepping belt has one idle tick per step.)</summary>
    public bool TryLoad(OreChip chip, Belt tile)
    {
        if (chip == null || tile == null || tile.line != this || host == null) return false;
        if (tile.occupants.Count >= Mathf.Max(0, host.chipsPerTile)) return false;
        if (chip.Absorbing || chip.Fading || chip.Stored || chip.claimedBy != null || chip.PulledByPlayer) return false;
        chip.Store(this);
        var e = new Entry { chip = chip, tile = tile, from = chip.transform.position, landT = 0f, slot = FreeSlot(tile) };
        tile.occupants.Add(e);
        chips.Add(e);
        return true;
    }

    // ------------------------------------------------------------------ tick

    public void Tick(float dt)
    {
        if (host == null) return;
        RefreshEnd();
        Prune();
        if (Stepping) TickStep(dt);
        TickContact(dt);                    // every tick — a step ending this tick flows straight into the next
        if (!Stepping) TryBeginStep(dt);
        TickLanding(dt);
    }

    /// <summary>Drop entries whose chip was taken away underneath us (a swallow, a fade). A live
    /// chip still stamped as some belt's (a stale line, never this one after Adopt) is put back
    /// on the ground rather than stranded: a stored chip is off every registry — no drone, no
    /// Hoover, no fade can reach it — so nothing else could ever free it.</summary>
    void Prune()
    {
        for (int k = chips.Count - 1; k >= 0; k--)
        {
            var e = chips[k];
            if (e.chip != null && !e.chip.Absorbing && !e.chip.Fading && e.chip.stored == this) continue;
            if (e.tile != null) e.tile.occupants.Remove(e);
            chips.RemoveAt(k);
            if (e.chip != null && !e.chip.Absorbing && !e.chip.Fading && e.chip.stored is BeltLine) SetDown(e);
        }
    }

    // ------------------------------------------------------------------ getting on (contact)

    /// <summary>A loose chip whose centre is on one of our tiles gets on — direct contact, no
    /// ring (user rule 2026-09-14): the Hoover's spray landing on the belt, a chip shoved onto
    /// it. Never a chip a drone has claimed or the player is still pulling. Runs on its own
    /// cadence whether or not a step is playing.</summary>
    void TickContact(float dt)
    {
        contactT -= dt;
        if (contactT > 0f) return;
        contactT = 0.1f;
        if (Free <= 0) return;
        for (int k = OreChip.all.Count - 1; k >= 0; k--)
        {
            var chip = OreChip.all[k];
            if (chip == null || chip.Absorbing || chip.Fading || chip.Stored) continue;
            if (chip.claimedBy != null || chip.PulledByPlayer || chip.PulledByCollector) continue;   // in someone's hands — crossing, not landing
            if (chip.transform.InDungeon()) continue;
            var tile = Belt.At(BaseCell.Of(chip.transform.position));
            if (tile == null || tile.line != this) continue;
            TryLoad(chip, tile);
        }
    }

    void TickLanding(float dt)
    {
        for (int k = 0; k < chips.Count; k++)
        {
            var e = chips[k];
            if (e.landT >= 1f || e.moving || e.chip == null || e.tile == null) continue;
            e.landT = Mathf.Min(1f, e.landT + dt / LandSeconds);
            Place(e.chip, Vector2.Lerp(e.from, e.tile.Centre + Belt.SlotOffset(e.slot), Mathf.SmoothStep(0f, 1f, e.landT)));
        }
    }

    static void Place(OreChip chip, Vector2 p)
    {
        var t = chip.transform;
        t.position = new Vector3(p.x, p.y, t.position.z);
    }

    // ------------------------------------------------------------------ stepping

    /// <summary>Is whatever sits at the end taking chip right now? Gross want, not net of
    /// inbound — what rides here is what will fill it.</summary>
    bool EndAcceptingNow()
    {
        if (end == null) return true;   // nothing there: the belt runs and sets chips down
        if (end is IChipConsumer c) return ChipConsumers.Active(c) && c.ChipDemandSpace > 0f;
        if (end is Collector col) return col != null && col.builtYet && !col.MarkedForDemolition && col.Reserved < col.capacity;
        return true;
    }

    /// <summary>Which chips move this step: tail first, so a group may take the tile its
    /// downstream neighbours are leaving; a merge (two feeders, one tile) lets through what fits.</summary>
    void PlanMovers()
    {
        movers.Clear();
        incoming.Clear();
        int cap = Mathf.Max(0, host.chipsPerTile);
        for (int i = 0; i < order.Count; i++)
        {
            var tile = order[i];
            var occ = tile.occupants;
            if (occ.Count == 0) continue;
            var nx = tile.next;
            if (nx == null)
            {
                for (int k = 0; k < occ.Count; k++) if (occ[k].landT >= 1f) movers.Add(occ[k]);   // the tail lets go
                continue;
            }
            int leaving = 0;
            for (int k = 0; k < nx.occupants.Count; k++) if (movers.Contains(nx.occupants[k])) leaving++;
            incoming.TryGetValue(nx, out int inc);
            int room = cap - (nx.occupants.Count - leaving) - inc;
            for (int k = 0; k < occ.Count && room > 0; k++)
            {
                if (occ[k].landT < 1f) continue;
                movers.Add(occ[k]);
                room--;
                inc++;
            }
            incoming[nx] = inc;
        }
    }

    void TryBeginStep(float dt)
    {
        // idle: the host just reports the supply — the "no energy" sign whenever nothing
        // touching the line has energy, no sign otherwise
        if (chips.Count == 0) { host.ReportDraw(0f); return; }
        if (!EndAcceptingNow()) { host.ReportDraw(0f); return; }
        PlanMovers();
        if (movers.Count == 0) { host.ReportDraw(0f); return; }
        float cost = movers.Count * Mathf.Max(0f, host.energyPerStep);
        if (bucket + 1e-5f < cost)
        {
            bucket += power.Draw(cost - bucket);
            host.ReportDraw(cost / Mathf.Max(0.05f, host.stepSeconds));
            if (bucket + 1e-5f < cost) return;   // the grid pays as fast as it can; the line waits
        }
        bucket = Mathf.Max(0f, bucket - cost);
        host.ClearDraw();
        // commit: lift every mover off its tile first, then seat each on the next
        for (int k = 0; k < movers.Count; k++)
        {
            var e = movers[k];
            e.tile.occupants.Remove(e);
            e.from = e.chip != null ? (Vector2)e.chip.transform.position : e.tile.Centre + Belt.SlotOffset(e.slot);
            e.moving = true;
        }
        for (int k = 0; k < movers.Count; k++)
        {
            var e = movers[k];
            if (e.tile.next == null) e.exiting = true;
            else
            {
                e.tile = e.tile.next;
                e.slot = FreeSlot(e.tile);
                e.tile.occupants.Add(e);
            }
        }
        stepT = 0f;
    }

    void TickStep(float dt)
    {
        stepT += dt / Mathf.Max(0.05f, host.stepSeconds);
        frameT += dt * Mathf.Max(0f, host.fps);
        float k = Mathf.Clamp01(stepT);   // linear: a belt runs at one speed
        for (int i = 0; i < chips.Count; i++)
        {
            var e = chips[i];
            if (!e.moving || e.chip == null) continue;
            Vector2 to = (e.exiting ? ExitPoint : e.tile.Centre) + Belt.SlotOffset(e.slot);
            Place(e.chip, Vector2.Lerp(e.from, to, k));
        }
        int f = Mathf.FloorToInt(frameT);
        if (f != frameShown)
        {
            frameShown = f;
            for (int i = 0; i < members.Count; i++) members[i].ShowFrame(f);
        }
        if (stepT >= 1f) FinishStep();
    }

    void FinishStep()
    {
        stepT = -1f;
        bool exited = false;
        for (int k = chips.Count - 1; k >= 0; k--)
        {
            var e = chips[k];
            if (!e.moving) continue;
            e.moving = false;
            if (e.exiting)
            {
                chips.RemoveAt(k);
                if (!exited) { RefreshEnd(force: true); exited = true; }
                Deliver(e);
            }
            else if (e.chip != null) Place(e.chip, e.tile.Centre + Belt.SlotOffset(e.slot));
        }
    }

    /// <summary>A tail chip has crossed onto the exit cell: fed straight into the building
    /// there if it takes this chip (else left exactly where it is — a consumer's own suction,
    /// a Collector's pull), or simply set down when nothing wants it. Never a scatter.</summary>
    void Deliver(Entry e)
    {
        var chip = e.chip;
        if (chip == null || chip.Absorbing || chip.Fading) return;
        chip.Unstore();
        Place(chip, ExitPoint + Belt.SlotOffset(e.slot));
        Still(chip);
        if (!(end is IChipConsumer t) || !ChipConsumers.Active(t)) return;
        if (!t.AcceptsChip(chip.sizeClass, chip.element) || (chip.refined && t.RefusesRefined)) return;
        t.TakeDelivered(chip);
    }

    // ------------------------------------------------------------------ the end building

    /// <summary>Whatever the exit cell lands in: a chip consumer or Collector whose footprint
    /// holds the exit point first, then a Collector the cell touches, then the nearest consumer
    /// whose ring covers it.</summary>
    void RefreshEnd(bool force = false)
    {
        if (!force && Time.time - endT < 0.25f) return;
        endT = Time.time;
        end = null;
        if (tail == null) return;
        Vector2 x = ExitPoint;
        var all = ChipConsumers.all;
        for (int k = 0; k < all.Count; k++)
        {
            var c = all[k];
            if (!(c is Component comp) || comp == null) continue;
            var b = ChipConsumers.BuildingOf(c);
            if (b != null && ChipConsumers.FootprintContains(b, x)) { end = c; return; }
        }
        var cols = Collector.All;
        for (int k = 0; k < cols.Count; k++)
        {
            var col = cols[k];
            if (col == null || !col.builtYet) continue;
            if (ChipConsumers.FootprintContains(col, x) || ChipConsumers.FootprintAdjacent(col, x)) { end = col; return; }
        }
        float bd = float.MaxValue;
        for (int k = 0; k < all.Count; k++)
        {
            var c = all[k];
            if (!(c is Component comp) || comp == null) continue;
            float r = c.ChipIntakeRadius;
            float d = (c.ChipDropPoint - x).sqrMagnitude;
            if (d > r * r || d >= bd) continue;
            end = c;
            bd = d;
        }
    }
}
