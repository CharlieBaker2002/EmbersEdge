using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One connected group of Tubes (4-neighbour adjacency on the base grid) — THE unit the
/// stored chips live in. A stored chip is out of the loose-chip world (not in OreChip.all, no
/// physics): the cluster owns its position and glides it into an even, field-like spread
/// through the INTERIOR of the union of its cells — one continuous field over the whole shape,
/// never per-box counts. Each arrival is dropped into the biggest gap in the field (farthest
/// from every chip already there), then a centroidal Voronoi (Lloyd) relaxation over a FINE
/// sample lattice of the inset shape smooths everything to uniform spacing, with a
/// line-of-sight penalty so no chip claims ground behind an inside corner. The relaxation is
/// budgeted per frame and runs to convergence, so a re-jig plays out over a few frames while
/// every chip eases to its moving spot (critically damped, ~2 s settle) along cell-centre
/// routes — nothing ever cuts across the outside of an L or a U. Capacity = boxes ×
/// Tube.chipsPerStore.
///
/// The cluster also runs the SHIELD: at a clear cycle (ChipClearCycle) the BILL is upkeepPerOre
/// per chip (0.025 × X) paid over upkeepTick per chip (0.025 × X seconds) — a flat 1 energy/s
/// draw whose length scales with the shelf (100 chips = 2.5 energy over 2.5 s) — from the
/// DISTINCT energy sources its boxes touch, every frame. The bill is due INSIDE its window: a
/// grid that can't keep up with the 1 energy/s (too slow, or dry) leaves a shortfall, and at the
/// window's end every 0.025 unpaid is one chip lost (outermost first). The effects phase while
/// the supply is falling short and drop entirely while the grid is empty.
/// Visuals: an era-coloured comet running round the inset contour of the shape whenever a
/// source with energy is connected, plus the shield (member radiance + rippling fill) while
/// the debt is being paid. Built and torn down by Tube.Rebuild; ticked by its host box.
/// </summary>
public class TubeCluster
{
    public class Entry
    {
        public OreChip chip;
        public Vector2 pos, vel, target;
        public readonly List<Vector2> path = new List<Vector2>();   // cell-centre waypoints, then target
        public float scaleK;                                        // 0..1 into the stored scale
        public Vector2 routedFor = new Vector2(1e9f, 1e9f);          // target the path was built for
        public bool fresh;                                           // just arrived: gets the biggest gap before smoothing
    }

    public readonly List<Tube> members = new List<Tube>();
    public readonly HashSet<Vector2Int> cells = new HashSet<Vector2Int>();
    public readonly List<Entry> chips = new List<Entry>();
    /// <summary>The box that ticks and draws for the cluster (lowest cell — stable across rebuilds).</summary>
    public Tube host;
    /// <summary>IChipConsumer.InboundChipSpace, shared by every member (one shelf, one ledger).</summary>
    public int inbound;
    public Vector2 centroid;
    public float extent = 0.2f;
    /// <summary>World AABB of the union of cells (the gauge hangs off its right edge).</summary>
    public Vector2 boundsMin, boundsMax;

    // contour loops (world space, inset), one LineRenderer each (host draws loop 0)
    readonly List<Vector2[]> loops = new List<Vector2[]>();
    readonly List<float[]> loopCum = new List<float[]>();
    readonly List<LineRenderer> loopLines = new List<LineRenderer>();
    readonly List<Vector3[]> loopBufs = new List<Vector3[]>();
    readonly List<float> loopAlphaDrawn = new List<float>();

    // energy — every distinct source any box touches, pooled (SourcePool; built in Finish)
    public SourcePool power;

    // shield
    bool shieldActive, shieldSettled;
    float unpaid, shieldEnd, shieldMinUntil, lastWant, lastPaid;
    float shieldK, throttleK, powerK, comet;
    bool layoutDirty;
    float intakeT, donateT, deliverT;

    // layout: the sample lattice of the inset shape + cell visibility + buckets for the relaxation
    Vector2[] samples = System.Array.Empty<Vector2>();
    int[] sampleCell = System.Array.Empty<int>();
    float[] sampleGap = System.Array.Empty<float>();    // min distance² from each sample to any chip target (gap finding)
    readonly List<Vector2Int> cellList = new List<Vector2Int>();
    readonly Dictionary<Vector2Int, int> cellIndex = new Dictionary<Vector2Int, int>();
    bool[] cellVis = System.Array.Empty<bool>();
    int[][] ring1 = System.Array.Empty<int[]>(), ring2 = System.Array.Empty<int[]>();   // cell indices within Chebyshev 1 / 2 of a cell
    int relaxLeft;
    float[] sumX = System.Array.Empty<float>(), sumY = System.Array.Empty<float>();
    int[] cnt = System.Array.Empty<int>(), chipCellIdx = System.Array.Empty<int>();
    int[] bucketHead = System.Array.Empty<int>(), bucketNext = System.Array.Empty<int>();
    readonly List<OreChip> intakeQueue = new List<OreChip>();   // found by the last scan, taken as the grid pays
    float intakeBucket;                                          // energy paid ahead of the next chip
    const int MaxIters = 160;           // Lloyd iterations per re-jig (stops early once still)
    const int PairBudget = 400000;      // chip×sample pair tests per frame
    const float OverRelax = 1.3f;       // step past the centroid — fewer iterations, no wall bounce

    // scratch
    static readonly Dictionary<Vector2Int, Vector2Int> bfsPrev = new Dictionary<Vector2Int, Vector2Int>();
    static readonly Queue<Vector2Int> bfsQueue = new Queue<Vector2Int>();
    static readonly List<Vector2Int> bfsTmp = new List<Vector2Int>();
    static readonly Vector2Int[] Dirs = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };

    public int Capacity => members.Count * (host != null ? Mathf.Max(0, host.chipsPerStore) : 0);
    public int Free => Mathf.Max(0, Capacity - chips.Count);
    /// <summary>Slots not already spoken for by loose chips lying in the ring (a drop the
    /// intake hasn't swept yet is as good as shelved) — what the shelf advertises as want.</summary>
    public int RoomChips => Mathf.Max(0, Free - RingStock());

    float ringStockT = float.NegativeInfinity;
    int ringStockCached;
    /// <summary>Unclaimed loose chips inside any box's intake ring (settled or just dropped),
    /// cached on a short cadence. Stamps BEFORE scanning so demand→ring→demand can't recurse.</summary>
    public int RingStock()
    {
        if (Time.time - ringStockT < 0.25f) return ringStockCached;
        ringStockT = Time.time;
        int n = 0;
        if (host != null && members.Count > 0)
        {
            float r2 = host.intakeRadius * host.intakeRadius;
            for (int k = 0; k < OreChip.all.Count; k++)
            {
                var chip = OreChip.all[k];
                if (chip == null || chip.Absorbing || chip.Fading || chip.claimedBy != null) continue;
                if (chip.transform.InDungeon()) continue;
                Vector2 p = chip.transform.position;
                for (int m = 0; m < members.Count; m++)
                {
                    if (members[m] == null) continue;
                    if (((Vector2)members[m].transform.position - p).sqrMagnitude <= r2) { n++; break; }
                }
            }
        }
        ringStockCached = n;
        return n;
    }
    public bool ShieldActive => shieldActive;
    /// <summary>0..1 — how far up the shield look is right now (members read it for their fill).</summary>
    public float ShieldK => shieldK;

    // ------------------------------------------------------------------ grid helpers

    /// <summary>The base grid's cell size (0.25 in World) — see BaseCell.</summary>
    public static float Cs => BaseCell.Cs;

    /// <summary>World-quantised cell of a point — the same lattice Building.CurrentWorldAnchor
    /// uses (cell x spans x·cs … (x+1)·cs), immune to GridManager re-anchoring.</summary>
    public static Vector2Int CellOf(Vector2 p) => BaseCell.Of(p);

    public static Vector2 CentreOf(Vector2Int c) => BaseCell.Centre(c);

    // ------------------------------------------------------------------ build / teardown

    /// <summary>Members are in — derive cells, host, centroid, contour; bind members.</summary>
    public void Finish()
    {
        host = null;
        cells.Clear();
        Vector2 sum = Vector2.zero;
        for (int k = 0; k < members.Count; k++)
        {
            var m = members[k];
            cells.Add(m.cell);
            sum += CentreOf(m.cell);
            if (host == null || m.cell.y < host.cell.y || (m.cell.y == host.cell.y && m.cell.x < host.cell.x)) host = m;
        }
        centroid = members.Count > 0 ? sum / members.Count : Vector2.zero;
        float far = 0f, cs = Cs;
        boundsMin = new Vector2(float.MaxValue, float.MaxValue);
        boundsMax = new Vector2(float.MinValue, float.MinValue);
        foreach (var c in cells)
        {
            far = Mathf.Max(far, (CentreOf(c) - centroid).magnitude);
            boundsMin = Vector2.Min(boundsMin, (Vector2)c * cs);
            boundsMax = Vector2.Max(boundsMax, (Vector2)(c + Vector2Int.one) * cs);
        }
        if (cells.Count == 0) boundsMin = boundsMax = centroid;
        extent = far + cs * 0.7f;
        for (int k = 0; k < members.Count; k++) members[k].cluster = this;
        power = new SourcePool(members);
        BuildContour();
        BuildSamples();
        layoutDirty = true;
    }

    /// <summary>Cluster being replaced (membership changed): hide what it drew, unbind members.
    /// Chips are migrated by Tube.Rebuild before this is called.</summary>
    public void Dispose()
    {
        for (int i = 0; i < loopLines.Count; i++)
            if (loopLines[i] != null) loopLines[i].gameObject.SetActive(false);
        loopLines.Clear();
        if (host != null) host.ClearDrawImmediate();   // the new shape's host reports from here on
        for (int k = 0; k < members.Count; k++)
        {
            var m = members[k];
            if (m == null) continue;
            if (m.cluster == this) m.cluster = null;
            m.HideShield();
        }
    }

    /// <summary>Adopt an entry from a dissolved cluster (a box died or came back): the chip keeps
    /// its spot and its motion — it is NOT a fresh arrival, so nothing gets re-dealt; the
    /// relaxation just eases the field into the new shape. Only a target that now lies outside
    /// the shape is pulled back in.</summary>
    public void Adopt(Entry e)
    {
        e.path.Clear();
        if (!cells.Contains(CellOf(e.target))) e.target = Constrain(e.pos);
        e.routedFor = new Vector2(1e9f, 1e9f);
        e.fresh = false;
        chips.Add(e);
        layoutDirty = true;
    }

    // ------------------------------------------------------------------ tick

    public void Tick(float dt)
    {
        if (host == null) return;
        TickIntake(dt);
        TickDonate(dt);
        TickDeliver(dt);
        TickChips(dt);
        TickShield(dt);
        // outside the shield window the host just reports the supply: the "no energy" sign
        // whenever no connected source has energy, nothing otherwise
        if (!(shieldActive && !shieldSettled)) host.ReportDraw(0f);
        if (layoutDirty) { layoutDirty = false; PlaceFresh(); relaxLeft = MaxIters; }
        if (relaxLeft > 0) RelaxStep();
        TickVisuals(dt);
    }

    // ------------------------------------------------------------------ intake

    /// <summary>Settled loose chips in any box's intake ring come in — EVERY one the shelf has
    /// room for (dumped hauls, the player's Hoover spray, chips nudged against the shelf) — but
    /// only while a connected source has energy, and each costs intakePerChip: the scan runs on
    /// its cadence, the payment every frame against the grid's own per-frame caps (a battery's
    /// surge swallows a whole dump at once; a bare generator streams it in at its rate).</summary>
    void TickIntake(float dt)
    {
        intakeT -= dt;
        if (intakeT <= 0f)
        {
            intakeT = host.intakeInterval;
            intakeQueue.Clear();
            if (Free > 0 && !host.MarkedForDemolition && Powered) Candidates(intakeQueue);
        }
        if (intakeQueue.Count == 0) return;
        if (Free <= 0) { intakeQueue.Clear(); return; }
        float price = Mathf.Max(0f, host.intakePerChip);
        int wantChips = Mathf.Min(Free, intakeQueue.Count);
        float need = wantChips * price - intakeBucket;
        if (need > 1e-6f) intakeBucket += Draw(need);
        while (intakeQueue.Count > 0 && Free > 0 && intakeBucket + 1e-5f >= price)
        {
            var chip = intakeQueue[0];
            intakeQueue.RemoveAt(0);
            if (chip == null || chip.Absorbing || chip.Fading || chip.Stored || chip.claimedBy != null || chip.PulledByPlayer) continue;
            intakeBucket = Mathf.Max(0f, intakeBucket - price);
            Take(chip);
        }
    }

    /// <summary>Loose chips in the ring the shelf may take, nearest first.</summary>
    void Candidates(List<OreChip> into)
    {
        into.Clear();
        float r = host.intakeRadius, r2 = r * r;
        float onTop = Cs * 0.75f, onTop2 = onTop * onTop;
        for (int k = 0; k < OreChip.all.Count; k++)
        {
            var chip = OreChip.all[k];
            if (chip == null || chip.Absorbing || chip.Fading || chip.rb == null) continue;
            if (chip.claimedBy != null || chip.PulledByPlayer || chip.PulledByCollector) continue;   // a drone, the player or a Collector has it
            if (chip.Age < OreChip.SettleSeconds) continue;                   // let it land first
            if (chip.transform.InDungeon()) continue;
            Vector2 p = chip.transform.position;
            float d = float.MaxValue;
            for (int m = 0; m < members.Count; m++)
            {
                float dm = ((Vector2)members[m].transform.position - p).sqrMagnitude;
                if (dm < d) d = dm;
            }
            if (d > r2) continue;
            // the fleet's dibs: a keener HUNGRY customer (construction, refiner…) is owed this chip —
            // unless it was put right on top of the box (a deliberate spray / dump)
            if (d > onTop2 && ChipConsumers.TopAppealFor(chip.sizeClass, chip.element, chip.refined) > 0) continue;
            if (ChipConsumers.AtAnIntake(chip, this)) continue;               // another building's ring owns it
            into.Add(chip);
        }
        if (into.Count > 1)
        {
            Vector2 c = centroid;
            into.Sort((a, b) => ((Vector2)a.transform.position - c).sqrMagnitude.CompareTo(((Vector2)b.transform.position - c).sqrMagnitude));
        }
    }

    /// <summary>A destroyed box touching this shape rebuilds from the shelf itself: while its
    /// ore-rebuild ghost still wants chip, the nearest chip steps off the shelf where it sits
    /// (inside the ghost's intake ring) and the ghost's own suction takes it from there — no
    /// loose chip or drone run needed, so a box that dies in a wave stands back up at once as
    /// long as the shape has anything in it.</summary>
    void TickDonate(float dt)
    {
        donateT -= dt;
        if (donateT > 0f) return;
        donateT = 0.25f;
        if (chips.Count == 0) return;
        var dead = Tube.DeadStores;
        for (int k = 0; k < dead.Count; k++)
        {
            var g = dead[k];
            if (g == null || !g.IsRebuildingFromOre) continue;
            var gc = g.cell;
            if (!cells.Contains(gc + Vector2Int.up) && !cells.Contains(gc + Vector2Int.right)
                && !cells.Contains(gc + Vector2Int.down) && !cells.Contains(gc + Vector2Int.left)) continue;
            var gi = g.GetComponent<GhostIntake>();
            if (gi == null || !gi.ChipIntakeActive || ChipConsumers.NetDemandSpace(gi) <= 0f) continue;
            Entry best = null;
            float bd = float.MaxValue;
            Vector2 gp = g.transform.position;
            for (int i = 0; i < chips.Count; i++)
            {
                var e = chips[i];
                if (e.chip == null || e.chip.Absorbing || e.chip.Fading || e.chip.claimedBy != null) continue;
                float d = (e.pos - gp).sqrMagnitude;
                if (d < bd) { bd = d; best = e; }
            }
            if (best == null) return;
            chips.Remove(best);
            best.chip.Unstore();
            layoutDirty = true;
            return;   // one chip per tick — the ghost's demand drops once it lands in the ring
        }
    }

    /// <summary>Shelved chip is a valid source for any building's own suction (user rule
    /// 2026-09-14): a hungry chip-eater whose suction ring already reaches a chip lying on the
    /// shelf may take it — the nearest such chip steps off the shelf WHERE IT LIES (nothing in
    /// the field is re-routed or re-prioritised) and is swallowed straight away
    /// (<see cref="IChipConsumer.TakeDelivered"/>) or left, at rest, for the building's next
    /// suction pass. Only inside the building's own radius, never beyond; one chip per building
    /// per tick; the fleet's dibs still apply. Another tube never takes from a tube; a dead
    /// box's own rebuild goes through TickDonate.</summary>
    void TickDeliver(float dt)
    {
        deliverT -= dt;
        if (deliverT > 0f) return;
        deliverT = 0.25f;
        if (chips.Count == 0 || host == null) return;
        var all = ChipConsumers.all;
        for (int k = 0; k < all.Count && chips.Count > 0; k++)
        {
            var c = all[k];
            if (c is Tube || !ChipConsumers.Active(c) || ChipConsumers.NetDemandSpace(c) <= 0f) continue;
            var b = ChipConsumers.BuildingOf(c);
            if (b is Tube) continue;
            Vector2 mouth = c.ChipDropPoint;
            float r2 = c.ChipIntakeRadius * c.ChipIntakeRadius;
            Entry best = null;
            float bd = float.MaxValue;
            for (int i = 0; i < chips.Count; i++)
            {
                var e = chips[i];
                var chip = e.chip;
                if (chip == null || chip.Absorbing || chip.Fading || chip.claimedBy != null) continue;
                float d = (e.pos - mouth).sqrMagnitude;
                if (d > r2 || d >= bd) continue;
                if (!ChipConsumers.MayGive(c, chip.sizeClass, chip.element, chip.refined)) continue;
                bd = d;
                best = e;
            }
            if (best == null) continue;
            chips.Remove(best);
            layoutDirty = true;
            var give = best.chip;
            give.Unstore();
            if (give.rb != null) { give.rb.linearVelocity = Vector2.zero; give.rb.angularVelocity = 0f; }
            c.TakeDelivered(give);   // false = its own suction takes it from where it lies
        }
        TickBeltOut();
    }

    /// <summary>Tube → belt (user call 2026-09-14): a Belt tile touching any box that carries
    /// AWAY from it gets the nearest shelved chip lying within <see cref="Tube.beltReach"/> (1
    /// unit) of it put aboard — the same "only within reach" rule every consumer's suction
    /// obeys, standing in for the suction a belt doesn't have; chips at the far end of a long
    /// shape stay where they are instead of racing through it. One chip per tile per tick, only
    /// while the tile has room; a chip a drone has claimed stays. Free, like everything a tube
    /// moves. (Belt → tube already works: a line ending on a box hands over through TakeDelivered.)</summary>
    void TickBeltOut()
    {
        if (chips.Count == 0) return;
        float reach2 = Mathf.Max(0f, host.beltReach) * Mathf.Max(0f, host.beltReach);
        var lines = Belt.Lines;
        for (int l = 0; l < lines.Count && chips.Count > 0; l++)
        {
            var tiles = lines[l].members;
            for (int t = 0; t < tiles.Count && chips.Count > 0; t++)
            {
                var tile = tiles[t];
                if (tile == null || !tile.HasRoom) continue;
                Tube touching = null;
                for (int m = 0; m < members.Count; m++)
                {
                    var box = members[m];
                    if (box == null) continue;
                    if (ChipConsumers.FootprintAdjacent(box, tile.Centre) && tile.PointsAwayFrom(box.transform.position)) { touching = box; break; }
                }
                if (touching == null) continue;
                Entry best = null;
                float bd = float.MaxValue;
                Vector2 tp = tile.Centre;
                for (int i = 0; i < chips.Count; i++)
                {
                    var e = chips[i];
                    var chip = e.chip;
                    if (chip == null || chip.Absorbing || chip.Fading || chip.claimedBy != null) continue;
                    float d = (e.pos - tp).sqrMagnitude;
                    if (d > reach2 || d >= bd) continue;   // only what lies within the tile's reach
                    bd = d;
                    best = e;
                }
                if (best == null) continue;
                var give = best.chip;
                chips.Remove(best);
                layoutDirty = true;
                give.Unstore();
                if (give.rb != null) { give.rb.linearVelocity = Vector2.zero; give.rb.angularVelocity = 0f; }
                if (!tile.TryLoad(give)) { Take(give); continue; }   // couldn't board after all — back on the shelf
                touching.Blip();
            }
        }
    }

    /// <summary>Shelve a loose chip: it leaves the world and enters the field from where it is.</summary>
    public void Take(OreChip chip)
    {
        chip.Store(this);
        var e = new Entry { chip = chip, pos = chip.transform.position, scaleK = 0f, fresh = true };
        e.target = Constrain(e.pos);
        chips.Add(e);
        layoutDirty = true;
        // nearest box blips as the chip goes in
        Tube near = null;
        float nd = float.MaxValue;
        for (int m = 0; m < members.Count; m++)
        {
            float d = ((Vector2)members[m].transform.position - e.pos).sqrMagnitude;
            if (d < nd) { nd = d; near = members[m]; }
        }
        if (near != null) near.Blip();
    }

    /// <summary>The player's Hoover takes a chip off the shelf where it lies (user call
    /// 2026-09-14 — the shelf used to be off-limits to it): out of the field, loose, at rest,
    /// stamped as the player's so the shape doesn't take it straight back; the syphon does the
    /// rest. The nearest box blips. Null if the entry is gone.</summary>
    public OreChip ReleaseTo(Entry e)
    {
        if (e == null || !chips.Remove(e)) return null;
        layoutDirty = true;
        var chip = e.chip;
        if (chip == null || chip.Absorbing || chip.Fading) return null;
        chip.Unstore();
        if (chip.rb != null) { chip.rb.linearVelocity = Vector2.zero; chip.rb.angularVelocity = 0f; }
        chip.playerPullStamp = Time.time;
        Tube near = null;
        float nd = float.MaxValue;
        for (int m = 0; m < members.Count; m++)
        {
            if (members[m] == null) continue;
            float d = ((Vector2)members[m].transform.position - e.pos).sqrMagnitude;
            if (d < nd) { nd = d; near = members[m]; }
        }
        if (near != null) near.Blip();
        return chip;
    }

    /// <summary>Let a chip go as an ordinary loose chip, tumbling out of the box it sat in.</summary>
    public void Drop(Entry e)
    {
        if (e.chip == null) return;
        if (e.chip.Absorbing || e.chip.Fading) return;
        e.chip.Unstore();
        e.chip.Tumble(CentreOf(CellOf(e.pos)) + new Vector2(0f, -0.02f));
    }

    // ------------------------------------------------------------------ motion

    void TickChips(float dt)
    {
        float smooth = Mathf.Max(0.05f, host.settleSeconds * 0.22f);
        float maxSpeed = Mathf.Max(0.1f, host.chipMaxSpeed);
        for (int k = chips.Count - 1; k >= 0; k--)
        {
            var e = chips[k];
            var chip = e.chip;
            if (chip == null || chip.Absorbing || chip.Fading || chip.stored != this)
            {
                chips.RemoveAt(k);
                layoutDirty = true;
                continue;
            }
            if (chip.claimedBy != null) continue;   // a drone is on its way for it — hold still
            while (e.path.Count > 0 && (e.path[0] - e.pos).sqrMagnitude < 0.08f * 0.08f) e.path.RemoveAt(0);
            Vector2 carrot = e.path.Count > 0 ? e.path[0] : e.target;
            e.pos = Vector2.SmoothDamp(e.pos, carrot, ref e.vel, smooth, maxSpeed, dt);
            var t = chip.transform;
            t.position = new Vector3(e.pos.x, e.pos.y, t.position.z);
            e.scaleK = Mathf.MoveTowards(e.scaleK, 1f, dt / 0.4f);
            chip.scaleMul = Mathf.Lerp(1f, host.storedChipScale, e.scaleK);
        }
    }

    /// <summary>The lattice the field is measured on: a fine grid per cell (finer for small
    /// shapes), keeping only points inside the inset shape (Constrain leaves them where they
    /// are). Also the cell-to-cell visibility table (the segment between two cell centres stays
    /// inside the shape) so samples behind an inside corner are penalised, and the 3×3 / 5×5
    /// neighbourhoods the bucketed nearest-chip search walks.</summary>
    void BuildSamples()
    {
        cellList.Clear();
        cellIndex.Clear();
        foreach (var c in cells) { cellIndex[c] = cellList.Count; cellList.Add(c); }
        int cc = cellList.Count;
        int per = cc <= 6 ? 24 : cc <= 20 ? 16 : cc <= 60 ? 12 : 8;
        var pts = new List<Vector2>(cc * per * per);
        var pcell = new List<int>(pts.Capacity);
        float cs = Cs;
        for (int ci = 0; ci < cc; ci++)
        {
            Vector2 o = (Vector2)cellList[ci] * cs;
            for (int y = 0; y < per; y++)
                for (int x = 0; x < per; x++)
                {
                    var p = o + new Vector2((x + 0.5f) / per * cs, (y + 0.5f) / per * cs);
                    if ((Constrain(p) - p).sqrMagnitude > 1e-8f) continue;   // in the wall inset
                    pts.Add(p);
                    pcell.Add(ci);
                }
        }
        samples = pts.ToArray();
        sampleCell = pcell.ToArray();
        sampleGap = new float[samples.Length];
        cellVis = new bool[cc * cc];
        for (int a = 0; a < cc; a++)
        {
            Vector2 pa = CentreOf(cellList[a]);
            for (int b = 0; b < cc; b++)
            {
                if (a == b) { cellVis[a * cc + b] = true; continue; }
                Vector2 pb = CentreOf(cellList[b]);
                bool vis = true;
                for (int k = 1; k < 8 && vis; k++)
                    if (!cells.Contains(CellOf(Vector2.Lerp(pa, pb, k / 8f)))) vis = false;
                cellVis[a * cc + b] = vis;
            }
        }
        ring1 = new int[cc][];
        ring2 = new int[cc][];
        var tmp = new List<int>(25);
        for (int ci = 0; ci < cc; ci++)
        {
            var c = cellList[ci];
            for (int r = 1; r <= 2; r++)
            {
                tmp.Clear();
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                        if (cellIndex.TryGetValue(c + new Vector2Int(dx, dy), out int ni)) tmp.Add(ni);
                if (r == 1) ring1[ci] = tmp.ToArray(); else ring2[ci] = tmp.ToArray();
            }
        }
        bucketHead = new int[cc];
    }

    int CellIdx(Vector2Int c) => cellIndex.TryGetValue(c, out int i) ? i : -1;

    void EnsureScratch(int n)
    {
        if (sumX.Length >= n) return;
        int sz = Mathf.Max(8, n * 2);
        sumX = new float[sz]; sumY = new float[sz]; cnt = new int[sz]; chipCellIdx = new int[sz]; bucketNext = new int[sz];
    }

    /// <summary>Occlusion-aware distance² from a chip target to a sample.</summary>
    float Dist2(Vector2 target, int targetCell, Vector2 sp, int sampleCellIdx, float penalty)
    {
        Vector2 d = target - sp;
        float d2 = d.x * d.x + d.y * d.y;
        if (targetCell >= 0 && !cellVis[targetCell * cellList.Count + sampleCellIdx]) d2 += penalty;
        return d2;
    }

    /// <summary>Every just-arrived chip is dropped into the biggest gap: the lattice point
    /// farthest from every chip already placed (settled ones keep their spots, so the field
    /// grows outward from what is there instead of re-dealing). Then Lloyd smooths it all.</summary>
    void PlaceFresh()
    {
        int n = chips.Count, m = samples.Length, cc = cellList.Count;
        if (n == 0 || m == 0 || cc == 0) return;
        bool any = false;
        for (int i = 0; i < n; i++) if (chips[i].fresh) { any = true; break; }
        if (!any) return;
        float penalty = Cs * Cs * 4f;
        // gap map from the settled chips
        for (int sidx = 0; sidx < m; sidx++) sampleGap[sidx] = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            if (chips[i].fresh) continue;
            Vector2 t = chips[i].target;
            int ci = CellIdx(CellOf(t));
            for (int sidx = 0; sidx < m; sidx++)
            {
                float d2 = Dist2(t, ci, samples[sidx], sampleCell[sidx], penalty);
                if (d2 < sampleGap[sidx]) sampleGap[sidx] = d2;
            }
        }
        for (int i = 0; i < n; i++)
        {
            var e = chips[i];
            if (!e.fresh) continue;
            e.fresh = false;
            int best = 0;
            float bg = -1f;
            for (int sidx = 0; sidx < m; sidx++) if (sampleGap[sidx] > bg) { bg = sampleGap[sidx]; best = sidx; }
            e.target = samples[best];
            int ci = sampleCell[best];
            for (int sidx = 0; sidx < m; sidx++)
            {
                float d2 = Dist2(e.target, ci, samples[sidx], sampleCell[sidx], penalty);
                if (d2 < sampleGap[sidx]) sampleGap[sidx] = d2;
            }
        }
    }

    /// <summary>A budgeted slice of Lloyd's algorithm: every lattice sample joins its nearest
    /// chip (occluded cells cost extra), every chip's target steps past the centroid of its
    /// samples (over-relaxed), clamped into the shape. Chips are bucketed by cell so a sample
    /// only tests its neighbourhood — a dense shelf the 3×3 around it, a sparse one the 5×5, a
    /// near-empty one everything. Runs until the field is still. Routes follow.</summary>
    void RelaxStep()
    {
        int n = chips.Count, m = samples.Length, cc = cellList.Count;
        if (n == 0 || m == 0 || cc == 0) { relaxLeft = 0; return; }
        EnsureScratch(n);
        float density = n / (float)cc;
        int[][] rings = n <= 48 ? null : density >= 2f ? ring1 : ring2;
        float perIter = rings == null ? n * (float)m : m * (rings == ring1 ? 9f : 25f) * Mathf.Max(1f, density);
        int iters = Mathf.Clamp(Mathf.FloorToInt(PairBudget / Mathf.Max(1f, perIter)), 1, relaxLeft);
        float penalty = Cs * Cs * 4f;
        for (int it = 0; it < iters; it++)
        {
            for (int ci = 0; ci < cc; ci++) bucketHead[ci] = -1;
            for (int i = 0; i < n; i++)
            {
                sumX[i] = 0f; sumY[i] = 0f; cnt[i] = 0;
                int ci = CellIdx(CellOf(chips[i].target));
                if (ci < 0) ci = CellIdx(NearestCell(chips[i].target));
                chipCellIdx[i] = ci;
                if (ci >= 0) { bucketNext[i] = bucketHead[ci]; bucketHead[ci] = i; }
            }
            for (int sidx = 0; sidx < m; sidx++)
            {
                Vector2 sp = samples[sidx];
                int sc = sampleCell[sidx];
                int best = -1;
                float bd = float.MaxValue;
                if (rings != null)
                {
                    var nb = rings[sc];
                    for (int k = 0; k < nb.Length; k++)
                    {
                        int ci = nb[k];
                        float pen = cellVis[ci * cc + sc] ? 0f : penalty;
                        for (int i = bucketHead[ci]; i >= 0; i = bucketNext[i])
                        {
                            Vector2 d = chips[i].target - sp;
                            float d2 = d.x * d.x + d.y * d.y + pen;
                            if (d2 < bd) { bd = d2; best = i; }
                        }
                    }
                }
                if (best < 0)   // sparse shelf (or nothing in reach): everyone
                {
                    for (int i = 0; i < n; i++)
                    {
                        float d2 = Dist2(chips[i].target, chipCellIdx[i], sp, sc, penalty);
                        if (d2 < bd) { bd = d2; best = i; }
                    }
                }
                sumX[best] += sp.x; sumY[best] += sp.y; cnt[best]++;
            }
            float maxMove = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector2 cur = chips[i].target;
                Vector2 t = cnt[i] > 0
                    ? cur + (new Vector2(sumX[i] / cnt[i], sumY[i] / cnt[i]) - cur) * OverRelax
                    : cur + new Vector2(Mathf.Cos(i * 2.399f), Mathf.Sin(i * 2.399f)) * 0.02f;   // starved (a twin on the same spot): step aside
                t = Constrain(t);
                maxMove = Mathf.Max(maxMove, (t - cur).magnitude);
                chips[i].target = t;
            }
            relaxLeft--;
            if (maxMove < 0.0015f) { relaxLeft = 0; break; }
        }
        for (int i = 0; i < n; i++)
        {
            var e = chips[i];
            if ((e.target - e.routedFor).sqrMagnitude < 0.005f * 0.005f) continue;
            Route(e);
            e.routedFor = e.target;
        }
    }

    /// <summary>Clamp a point into the shape: its cell (nearest cell if outside), inset on every
    /// EXTERIOR side (shared sides let it cross into the neighbour), and kept out of the corner
    /// notch beside a missing diagonal cell so nothing pokes past an inside corner.</summary>
    public Vector2 Constrain(Vector2 p)
    {
        float cs = Cs, inset = host != null ? host.wallInset : 0.05f, h = cs * 0.5f;
        var c = CellOf(p);
        if (!cells.Contains(c)) c = NearestCell(p);
        Vector2 ctr = CentreOf(c);
        bool L = cells.Contains(c + Vector2Int.left), R = cells.Contains(c + Vector2Int.right);
        bool D = cells.Contains(c + Vector2Int.down), U = cells.Contains(c + Vector2Int.up);
        float minX = ctr.x - h + (L ? 0f : inset), maxX = ctr.x + h - (R ? 0f : inset);
        float minY = ctr.y - h + (D ? 0f : inset), maxY = ctr.y + h - (U ? 0f : inset);
        p.x = Mathf.Clamp(p.x, minX, maxX);
        p.y = Mathf.Clamp(p.y, minY, maxY);
        // inside corners: both sides shared but the diagonal missing → the notch is exterior
        if (U && R && !cells.Contains(c + Vector2Int.one)) p = OutOfNotch(p, ctr.x + h, ctr.y + h, inset, -1f, -1f);
        if (U && L && !cells.Contains(c + new Vector2Int(-1, 1))) p = OutOfNotch(p, ctr.x - h, ctr.y + h, inset, 1f, -1f);
        if (D && R && !cells.Contains(c + new Vector2Int(1, -1))) p = OutOfNotch(p, ctr.x + h, ctr.y - h, inset, -1f, 1f);
        if (D && L && !cells.Contains(c + new Vector2Int(-1, -1))) p = OutOfNotch(p, ctr.x - h, ctr.y - h, inset, 1f, 1f);
        return p;
    }

    static Vector2 OutOfNotch(Vector2 p, float cx, float cy, float inset, float sx, float sy)
    {
        float dx = (p.x - cx) * sx, dy = (p.y - cy) * sy;   // distance INTO the shape from the corner, per axis
        if (dx >= inset || dy >= inset) return p;
        if (inset - dx < inset - dy) p.x = cx + sx * inset; else p.y = cy + sy * inset;
        return p;
    }

    Vector2Int NearestCell(Vector2 p)
    {
        Vector2Int best = default;
        float bestD = float.MaxValue;
        foreach (var c in cells)
        {
            float d = (CentreOf(c) - p).sqrMagnitude;
            if (d < bestD) { bestD = d; best = c; }
        }
        return best;
    }

    /// <summary>Waypoints from the chip's current cell to its target's cell through the cluster
    /// (BFS over member cells; a chip still outside enters via the nearest box first). Only
    /// intermediate cell centres are kept: two adjacent cells make a convex rectangle, so the
    /// legs between them never leave the shape.</summary>
    void Route(Entry e)
    {
        e.path.Clear();
        var from = CellOf(e.pos);
        var to = CellOf(e.target);
        if (!cells.Contains(from))
        {
            from = NearestCell(e.pos);
            e.path.Add(CentreOf(from));
        }
        if (from == to) return;
        bfsPrev.Clear();
        bfsQueue.Clear();
        bfsQueue.Enqueue(from);
        bfsPrev[from] = from;
        bool found = false;
        while (bfsQueue.Count > 0)
        {
            var c = bfsQueue.Dequeue();
            if (c == to) { found = true; break; }
            for (int d = 0; d < 4; d++)
            {
                var nb = c + Dirs[d];
                if (!cells.Contains(nb) || bfsPrev.ContainsKey(nb)) continue;
                bfsPrev[nb] = c;
                bfsQueue.Enqueue(nb);
            }
        }
        if (!found) return;
        bfsTmp.Clear();
        for (var c = bfsPrev[to]; c != from; c = bfsPrev[c]) bfsTmp.Add(c);
        for (int i = bfsTmp.Count - 1; i >= 0; i--) e.path.Add(CentreOf(bfsTmp[i]));
    }

    // ------------------------------------------------------------------ energy

    /// <summary>Energy banked across the connected sources (0 = nothing to protect with).</summary>
    public float Energy() => power != null ? power.Energy() : 0f;

    public bool Connected => power != null && power.Connected;

    /// <summary>A connected source with energy — the gate on taking chips in at all.</summary>
    public bool Powered => Connected && Energy() > 1e-3f;

    /// <summary>Energy/sec the shelf draws while shielding: upkeepPerOre / upkeepTick (1 at defaults) — flat, however many chips.</summary>
    public float WantRate => host != null ? host.upkeepPerOre / Mathf.Max(0.001f, host.upkeepTick) : 0f;

    /// <summary>The cycle's bill for the shelf as it stands: upkeepPerOre × chips.</summary>
    public float Bill => host != null ? host.upkeepPerOre * chips.Count : 0f;

    /// <summary>Seconds the bill takes at the flat rate: upkeepTick × chips.</summary>
    public float Window => host != null ? host.upkeepTick * chips.Count : 0f;

    /// <summary>Side-effect-free offer over <paramref name="dt"/> from every distinct source the
    /// shape touches (fair-share peek — never MaxDrawThisFrame, which registers a drawer).</summary>
    public float PeekOffer(float dt) => power != null ? power.PeekOffer(dt) : 0f;

    float Draw(float want) => power != null ? power.Draw(want) : 0f;

    // ------------------------------------------------------------------ shield

    /// <summary>A clear cycle is here: the bill is upkeepPerOre per chip on the shelf, demanded at
    /// the flat rate over upkeepTick per chip — due INSIDE that window (the look holds at least
    /// shieldSeconds so a small shelf's shield is still seen). Nothing to protect → nothing happens.</summary>
    public void BeginShield()
    {
        if (host == null || chips.Count == 0) return;
        shieldActive = true;
        shieldSettled = false;
        unpaid = 0f;
        shieldEnd = Time.time + Window;
        shieldMinUntil = Time.time + Mathf.Max(host.shieldSeconds, Window);
        lastWant = lastPaid = 0f;
        SpawnStrikeRing.Ring(centroid, 1f, Mathf.Clamp(extent, 0.2f, 3f));
    }

    void TickShield(float dt)
    {
        if (!shieldActive) return;
        if (!shieldSettled)
        {
            float now = Time.time;
            if (now < shieldEnd)
            {
                // the flat RATE (upkeepPerOre per upkeepTick — 1 energy/s) is demanded every frame of
                // the window; per-frame source caps apply honestly and there is NO catching up —
                // what the grid can't give this frame is shortfall
                float want = WantRate * Mathf.Min(dt, shieldEnd - now);
                float paid = want > 0f ? Draw(want) : 0f;
                unpaid += Mathf.Max(0f, want - paid);
                lastWant = want;
                lastPaid = paid;
                host.ReportDraw(WantRate);
            }
            else Settle();
        }
        if (shieldSettled && Time.time >= shieldMinUntil) shieldActive = false;
    }

    /// <summary>The window is over: every upkeepPerOre of shortfall is one chip eaten, outermost
    /// first (the shield collapses in from the edges); a fully covered bill keeps the shelf whole.</summary>
    void Settle()
    {
        shieldSettled = true;
        host.ClearDraw();
        int lost = Mathf.Clamp(Mathf.CeilToInt(unpaid / Mathf.Max(1e-4f, host.upkeepPerOre) - 1e-3f), 0, chips.Count);
        unpaid = 0f;
        if (lost <= 0) return;
        Vector2 c = centroid;
        chips.Sort((a, b) => (b.pos - c).sqrMagnitude.CompareTo((a.pos - c).sqrMagnitude));
        for (int k = 0; k < lost && chips.Count > 0; k++)
        {
            var e = chips[0];
            chips.RemoveAt(0);
            if (e.chip != null) e.chip.FadeOut(DroneManager.ChipFadeSeconds, k * 0.04f);
        }
        layoutDirty = true;
    }

    // ------------------------------------------------------------------ visuals

    void TickVisuals(float dt)
    {
        bool powered = Energy() > 1e-3f;
        // throttled = paying, but the grid's RATE can't keep up this frame (energy is there, input isn't)
        bool throttled = shieldActive && !shieldSettled && lastWant > 1e-6f && lastPaid < lastWant * 0.98f;
        throttleK = Mathf.MoveTowards(throttleK, throttled ? 1f : 0f, dt / 0.35f);
        powerK = Mathf.MoveTowards(powerK, powered ? 1f : 0f, dt / (powered ? 0.5f : 0.35f));
        shieldK = Mathf.MoveTowards(shieldK, shieldActive ? 1f : 0f, dt / (shieldActive ? 0.15f : 0.45f));
        // phase in and out while the supply catches up, steady once it does
        float phase = Mathf.Lerp(1f, 0.25f + 0.75f * (0.5f + 0.5f * Mathf.Sin(Time.time * 4.5f)), throttleK);
        float glow = powerK * phase;
        comet += dt * host.cometSpeed * (1f + 1.5f * shieldK);
        Color era = GS.ColFromEra();
        for (int i = 0; i < loops.Count; i++) DrawLoop(i, era, glow);
        float k = shieldK * glow;
        for (int m = 0; m < members.Count; m++)
            if (members[m] != null) members[m].TickVisual(dt, k, era, centroid);
    }

    void DrawLoop(int i, Color era, float glow)
    {
        if (i >= loopLines.Count) return;
        var lr = loopLines[i];
        if (lr == null) return;
        float a = host.lineAlpha * glow * (1f + 0.6f * shieldK);
        if (a <= 0.01f)
        {
            if (lr.gameObject.activeSelf) lr.gameObject.SetActive(false);
            return;
        }
        if (!lr.gameObject.activeSelf) lr.gameObject.SetActive(true);
        var loop = loops[i];
        var cum = loopCum[i];
        float len = cum[cum.Length - 1];
        var buf = loopBufs[i];
        int m = buf.Length;
        float off = Mathf.Repeat(comet, len);
        var tr = lr.transform;
        for (int j = 0; j < m; j++)
        {
            float s = Mathf.Repeat(off + j * len / m, len);
            Vector2 w = PointAt(loop, cum, s);
            var l = tr.InverseTransformPoint(new Vector3(w.x, w.y, 0f));
            l.z = 0f;
            buf[j] = l;
        }
        lr.positionCount = m;
        lr.SetPositions(buf);
        lr.widthMultiplier = host.lineWidth * (1f + 1.2f * shieldK) * (1f + 0.15f * Mathf.Sin(Time.time * 3.1f));
        a = Mathf.Min(1f, a);
        if (Mathf.Abs(loopAlphaDrawn[i] - a) > 0.02f || loopAlphaDrawn[i] < 0f)
        {
            loopAlphaDrawn[i] = a;
            lr.colorGradient = Comet(era, a);
        }
    }

    /// <summary>Two long bright heads a half-loop apart on a dimmer base — the heads are the era
    /// hue pushed to full saturation and value (never whitened: with the additive glow on top a
    /// pale core read as plain white), the base the same hue darkened; the loop is resampled
    /// with a sliding offset every frame, so the heads run round the shape.</summary>
    static Gradient Comet(Color era, float a)
    {
        var g = new Gradient();
        float dim = 0.4f * a;
        Color.RGBToHSV(era, out float h, out float sat, out float val);
        Color core = Color.HSVToRGB(h, Mathf.Clamp01(Mathf.Max(sat, 0.6f) * 1.25f), 1f);
        era = Color.HSVToRGB(h, Mathf.Clamp01(Mathf.Max(sat, 0.6f) * 1.25f), Mathf.Clamp01(val * 0.75f));
        const float w = 0.11f;
        g.SetKeys(
            new[]
            {
                new GradientColorKey(era, 0f), new GradientColorKey(era, 0.25f - w), new GradientColorKey(core, 0.25f), new GradientColorKey(era, 0.25f + w),
                new GradientColorKey(era, 0.75f - w), new GradientColorKey(core, 0.75f), new GradientColorKey(era, 0.75f + w), new GradientColorKey(era, 1f),
            },
            new[]
            {
                new GradientAlphaKey(dim, 0f), new GradientAlphaKey(dim, 0.25f - w), new GradientAlphaKey(a, 0.25f), new GradientAlphaKey(dim, 0.25f + w),
                new GradientAlphaKey(dim, 0.75f - w), new GradientAlphaKey(a, 0.75f), new GradientAlphaKey(dim, 0.75f + w), new GradientAlphaKey(dim, 1f),
            });
        return g;
    }

    static Vector2 PointAt(Vector2[] loop, float[] cum, float s)
    {
        int n = loop.Length;
        for (int i = 0; i < n; i++)
        {
            if (s > cum[i + 1]) continue;
            float segLen = cum[i + 1] - cum[i];
            float t = segLen > 1e-6f ? (s - cum[i]) / segLen : 0f;
            return Vector2.Lerp(loop[i], loop[(i + 1) % n], t);
        }
        return loop[0];
    }

    /// <summary>The outline of the union of cells as directed exterior edges (interior on the
    /// LEFT — CCW), chained into loops (a ring of boxes gives an inner loop too), collinear runs
    /// merged, then inset by contourInset along the corner bisectors. Each loop gets one member's
    /// authored Contour renderer (the host draws the first).</summary>
    void BuildContour()
    {
        loops.Clear();
        loopCum.Clear();
        loopBufs.Clear();
        loopAlphaDrawn.Clear();
        for (int i = 0; i < loopLines.Count; i++) if (loopLines[i] != null) loopLines[i].gameObject.SetActive(false);
        loopLines.Clear();
        if (cells.Count == 0 || host == null) return;

        var starts = new Dictionary<Vector2Int, List<Vector2Int>>();
        void Add(Vector2Int a, Vector2Int b)
        {
            if (!starts.TryGetValue(a, out var l)) starts[a] = l = new List<Vector2Int>(2);
            l.Add(b);
        }
        foreach (var c in cells)
        {
            var bl = c; var br = c + Vector2Int.right; var tr = c + Vector2Int.one; var tl = c + Vector2Int.up;
            if (!cells.Contains(c + Vector2Int.down)) Add(bl, br);
            if (!cells.Contains(c + Vector2Int.right)) Add(br, tr);
            if (!cells.Contains(c + Vector2Int.up)) Add(tr, tl);
            if (!cells.Contains(c + Vector2Int.left)) Add(tl, bl);
        }
        var poly = new List<Vector2Int>();
        int guardLoops = cells.Count + 2;
        while (starts.Count > 0 && guardLoops-- > 0)
        {
            poly.Clear();
            Vector2Int start = default;
            foreach (var k in starts.Keys) { start = k; break; }
            Vector2Int cur = start;
            int guard = 4 * cells.Count + 8;
            while (guard-- > 0)
            {
                if (!starts.TryGetValue(cur, out var outs) || outs.Count == 0) break;
                Vector2Int next = outs[0];
                if (outs.Count > 1 && poly.Count > 0)
                {
                    // pinch corner (two cells touching diagonally): turn RIGHT so the interior stays on our left
                    var din = cur - poly[poly.Count - 1];
                    var right = new Vector2Int(din.y, -din.x);
                    for (int o = 0; o < outs.Count; o++) if (outs[o] - cur == right) { next = outs[o]; break; }
                }
                outs.Remove(next);
                if (outs.Count == 0) starts.Remove(cur);
                poly.Add(cur);
                cur = next;
                if (cur == start) break;
            }
            if (poly.Count >= 4) AddLoop(poly);
        }

        // renderers: host first, then the other members, one loop each
        int li = 0;
        if (host.contour != null && li < loops.Count) { loopLines.Add(host.contour); li++; }
        for (int k = 0; k < members.Count && li < loops.Count; k++)
        {
            var m = members[k];
            if (m == null || m == host || m.contour == null) continue;
            loopLines.Add(m.contour);
            li++;
        }
    }

    void AddLoop(List<Vector2Int> corners)
    {
        // merge collinear runs
        var v = new List<Vector2Int>();
        int n = corners.Count;
        for (int i = 0; i < n; i++)
        {
            var prev = corners[(i + n - 1) % n];
            var cur = corners[i];
            var next = corners[(i + 1) % n];
            var d1 = cur - prev; var d2 = next - cur;
            if (d1.x * d2.y - d1.y * d2.x == 0) continue;   // straight through
            v.Add(cur);
        }
        if (v.Count < 4) return;
        float cs = Cs, inset = host.contourInset;
        n = v.Count;
        var loop = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            Vector2 p = (Vector2)v[(i + n - 1) % n] * cs, c = (Vector2)v[i] * cs, q = (Vector2)v[(i + 1) % n] * cs;
            Vector2 d1 = (c - p).normalized, d2 = (q - c).normalized;
            Vector2 n1 = new Vector2(-d1.y, d1.x), n2 = new Vector2(-d2.y, d2.x);   // left normals = inward
            loop[i] = c + (n1 + n2) * inset;
        }
        var cum = new float[n + 1];
        for (int i = 0; i < n; i++) cum[i + 1] = cum[i] + (loop[(i + 1) % n] - loop[i]).magnitude;
        int m = Mathf.Clamp(Mathf.RoundToInt(cum[n] / 0.05f), 12, 200);
        loops.Add(loop);
        loopCum.Add(cum);
        loopBufs.Add(new Vector3[m]);
        loopAlphaDrawn.Add(-1f);
    }
}
