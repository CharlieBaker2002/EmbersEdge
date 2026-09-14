using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The Collector — a powered ore magnet. Once built it cycles through its 36-frame strip
/// (<see cref="frames"/>, script-driven — no Animator) and DRAWS loose chips inside
/// <see cref="radius"/> to its centre, where they settle as a pile: the chips stay real,
/// unclaimed OreChips — bag drones and the player's Hoover collect them from the pile exactly
/// as they would off the ground (drones fly over building bodies, chips don't collide with
/// them, and chips sort above buildings). A parked chip has its collider switched OFF so nothing
/// walking through the base can scatter the pile; it gets its body back the moment it leaves
/// (Hoover pull, drone swallow, dragged out) or the Collector shuts down. It keeps up to <see cref="capacity"/> ore gathered or
/// on the way; taking a chip on costs <see cref="energyPerOre"/>, charged from the grid at the
/// rate-paced <see cref="drawRate"/> (the ChargerTurret pattern — a weak grid pulls slower,
/// never for free; a chip nudged out of the pile is pulled back for nothing). Chips a drone has
/// claimed, chips under the Hoover, chips in another building's intake ring and chips in another
/// Collector's pile are left alone. Pulling wakes the cycle — faster frames and a hotter
/// material `thecolor` — which settles again when the pull ends. Hovering the built Collector
/// fades in an era-coloured ring at its reach: the authored RadiusRing child (Tools/Collector Kit).
/// </summary>
public class Collector : Building
{
    [Header("Collector")]
    [Tooltip("The cycle frames (the CollectorBase strip), assigned by Tools/Collector Kit.")]
    public Sprite[] frames;
    [Tooltip("Frames per second while idle.")]
    public float idleFps = 6f;
    [Tooltip("Frames per second while ore is being pulled in.")]
    public float activeFps = 20f;
    [Tooltip("Extra material radiance while pulling (thecolor × (1 + this)): 1 = twice as bright.")]
    [Range(0f, 4f)] public float pullRadiance = 1.2f;
    [Tooltip("Reach: loose chips inside this radius are pulled to the centre.")]
    public float radius = 2.5f;
    [Tooltip("Ore kept at most — gathered in the pile or on the way; pulling stops while that many are accounted for.")]
    public int capacity = 5;
    [Tooltip("Chips settle inside this distance of the centre and count as gathered.")]
    public float holdRadius = 0.22f;
    [Tooltip("Energy per ore taken on (a chip nudged out of the pile is pulled back free).")]
    public float energyPerOre = 0.05f;
    [Tooltip("Max energy/sec drawn from the grid while charging a pull (rate-paced — a weak grid pulls slower).")]
    public float drawRate = 0.5f;
    [Tooltip("Seconds between taking chips on.")]
    public float pullInterval = 0.25f;
    [Tooltip("Pull speed at the edge of the reach / at the centre.")]
    public float pullSpeedFar = 2.2f, pullSpeedNear = 0.8f;
    [Tooltip("How quickly a pulled chip's velocity bends toward the centre.")]
    public float pullAccel = 12f;

    [Header("Hover ring")]
    [Tooltip("Authored child LineRenderer showing the reach while hovered (Tools/Collector Kit draws it; the shared HoverRing component on the root fades it — Tools/Hover Ring Kit).")]
    public LineRenderer radiusRing;

    // every built Collector, so two magnets never fight over one pile
    static readonly List<Collector> live = new List<Collector>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => live.Clear();   // no-domain-reload: statics survive play-stop

    /// <summary>Every built Collector (a belt ending at one waits for room in its pile).</summary>
    public static IReadOnlyList<Collector> All => live;

    readonly List<OreChip> pulling = new List<OreChip>();   // chips on their way to the pile
    readonly List<OreChip> parked = new List<OreChip>();    // chips resting in the pile, colliders off
    int gatheredCached;
    float gatherScanT = float.NegativeInfinity;

    /// <summary>Chips settled in the pile right now (scanned on a short cadence).</summary>
    public int Gathered
    {
        get
        {
            if (Time.time - gatherScanT < 0.2f) return gatheredCached;
            gatherScanT = Time.time;
            gatheredCached = CountInPile();
            return gatheredCached;
        }
    }
    /// <summary>Gathered plus on the way — what the capacity counts.</summary>
    public int Reserved => Gathered + pulling.Count;

    Coroutine runCo;
    Material radMat;
    Color baseCol = Color.white;
    int radEra = -1;
    float frameT, activity, bucket, nextPull, pushT;
    float activeUntil = float.NegativeInfinity;
    static readonly int TheColorId = Shader.PropertyToID("thecolor");
    const float ActiveHold = 0.6f;    // the cycle stays lively this long after the last chip lands
    const float ActiveEase = 0.35f;   // seconds to swell / settle

    public override float PeakEnergyDemand => drawRate;
    /// <summary>The "no energy" sign whenever its supply is empty (user call 2026-09-14).</summary>
    public override bool ShowsEnergyIcons => true;
    /// <summary>The hover ring shows the pull reach.</summary>
    public override float HoverRingRadius => radius;

    protected override void BEnable()
    {
        ApplyEraMaterial();
        if (!live.Contains(this)) live.Add(this);
        if (runCo == null) runCo = StartCoroutine(Run());
    }

    protected override void BDisable()
    {
        live.Remove(this);
        if (runCo != null) { StopCoroutine(runCo); runCo = null; }
        pulling.Clear();   // chips in flight just coast to a stop where they are
        ReleaseAll();      // the pile gets its bodies back
        ClearEnergyStatusImmediate();
    }

    public override void OnDestroy()
    {
        live.Remove(this);
        if (!GS.qutting) ReleaseAll();
        if (radMat != null) Destroy(radMat);
        base.OnDestroy();
    }

    // ------------------------------------------------------------------ the cycle

    /// <summary>The era's ore material (the one the ore glows) on a runtime copy so the pull
    /// radiance can drive `thecolor` without touching the shared asset; re-cut when the era turns.</summary>
    void ApplyEraMaterial()
    {
        if (sr == null) return;
        if (radMat == null || radEra != GS.era)
        {
            var src = GS.MatByEra(GS.era, bright: true);
            if (src == null) return;
            if (radMat != null) Destroy(radMat);
            radMat = new Material(src);
            radEra = GS.era;
            baseCol = radMat.HasProperty(TheColorId) ? radMat.GetColor(TheColorId) : Color.white;
        }
        sr.sharedMaterial = radMat;
    }

    IEnumerator Run()
    {
        nextPull = Time.time;
        while (true)
        {
            float dt = Time.deltaTime;
            if (radEra != GS.era) ApplyEraMaterial();
            // lively while chips are on the way (and a beat after the last lands), settling to idle
            bool lively = pulling.Count > 0 || Time.time < activeUntil;
            activity = Mathf.MoveTowards(activity, lively ? 1f : 0f, dt / ActiveEase);
            if (frames != null && frames.Length > 0 && sr != null)
            {
                frameT = Mathf.Repeat(frameT + dt * Mathf.Lerp(idleFps, activeFps, activity), frames.Length);
                sr.sprite = frames[Mathf.Clamp((int)frameT, 0, frames.Length - 1)];
            }
            if (radMat != null && radMat.HasProperty(TheColorId))
                radMat.SetColor(TheColorId, baseCol * (1f + pullRadiance * activity));
            TickTakeOn(dt);
            TickPushOut(dt);
            yield return null;
        }
    }

    // ------------------------------------------------------------------ pulling

    /// <summary>Take the next loose chip on, once the grid has paid for it.</summary>
    void TickTakeOn(float dt)
    {
        // idle: just report the supply — the "no energy" sign while it's empty, nothing otherwise
        if (MarkedForDemolition || Reserved >= capacity) { ReportEnergyDraw(0f); return; }
        var chip = Candidate(out bool stray);
        if (chip == null) { ReportEnergyDraw(0f); return; }
        if (!stray)
        {
            // charge the pull from the grid at the paced rate: capped by the fair-share offer
            // (MaxDrawThisFrame — this call is the frame's demand registration) and stored energy
            if (bucket < energyPerOre)
            {
                float step = Mathf.Min(drawRate * dt, Power.MaxDrawThisFrame(dt), Power.Energy, energyPerOre - bucket);
                if (step > 0f && Power.Use(step)) bucket += step;
                ReportEnergyDraw(drawRate);
            }
            if (bucket + 1e-5f < energyPerOre || Time.time < nextPull) return;
            bucket = Mathf.Max(0f, bucket - energyPerOre);
        }
        else if (Time.time < nextPull) return;
        pulling.Add(chip);
        nextPull = Time.time + pullInterval;
        gatherScanT = float.NegativeInfinity;   // the count is about to change — rescan next ask
    }

    /// <summary>The nearest loose chip in reach that nobody else has and that isn't already in
    /// the pile or on its way: not claimed by a drone, not under the player's Hoover, not in
    /// another building's intake ring, not in another Collector's pile. <paramref name="stray"/>
    /// flags a chip that was nudged out of OUR pile — pulled back for free.</summary>
    OreChip Candidate(out bool stray)
    {
        stray = false;
        Vector2 pos = transform.position;
        float r2 = radius * radius, hold2 = holdRadius * holdRadius;
        float strayR2 = holdRadius * 3f * holdRadius * 3f;
        OreChip best = null;
        float bestSqr = float.MaxValue;
        for (int k = 0; k < OreChip.all.Count; k++)
        {
            var chip = OreChip.all[k];
            if (chip == null || chip.Absorbing || chip.Fading || chip.Stored || chip.rb == null || chip.claimedBy != null) continue;   // gone, held, or a drone is flying for it
            if (chip.PulledByPlayer) continue;                                                        // the Hoover has it
            if (chip.Age < OreChip.SettleSeconds) continue;                                           // let fresh drops land
            if (chip.transform.InDungeon()) continue;
            float d = ((Vector2)chip.transform.position - pos).sqrMagnitude;
            if (d > r2 || d <= hold2 || d >= bestSqr) continue;                                       // out of reach / already in the pile
            if (pulling.Contains(chip)) continue;
            if (ChipConsumers.AtAnIntake(chip)) continue;                                             // a building's own intake owns it
            if (InAnotherPile(chip)) continue;
            best = chip;
            bestSqr = d;
        }
        stray = best != null && bestSqr <= strayR2;
        return best;
    }

    bool InAnotherPile(OreChip chip)
    {
        Vector2 p = chip.transform.position;
        for (int k = 0; k < live.Count; k++)
        {
            var c = live[k];
            if (c == null || c == this) continue;
            float h = c.holdRadius * 1.5f;
            if (((Vector2)c.transform.position - p).sqrMagnitude <= h * h) return true;
        }
        return false;
    }

    int CountInPile()
    {
        int n = 0;
        Vector2 pos = transform.position;
        float h = holdRadius * 1.5f, h2 = h * h;
        for (int k = 0; k < OreChip.all.Count; k++)
        {
            var chip = OreChip.all[k];
            if (chip == null || chip.Absorbing) continue;
            if (((Vector2)chip.transform.position - pos).sqrMagnitude <= h2) n++;
        }
        return n;
    }

    /// <summary>Steer every chip on its way: bend its velocity toward the centre, slowing as it
    /// nears, and park it once inside the hold. Anything somebody else took mid-pull (a drone
    /// claim, the Hoover, a swallow) or that got dragged out of reach is simply let go.</summary>
    void FixedUpdate()
    {
        if (!builtYet) return;
        TickParked();
        if (pulling.Count == 0) return;
        Vector2 pos = transform.position;
        float dtF = Time.fixedDeltaTime;
        for (int k = pulling.Count - 1; k >= 0; k--)
        {
            var chip = pulling[k];
            // gone, or somebody else's now: swallowed, fading, shelved / on a belt (Stored — its
            // body is off, it would never reach the pile and would hold a reservation forever),
            // claimed by a drone, under the Hoover
            if (chip == null || chip.Absorbing || chip.Fading || chip.Stored || chip.rb == null || chip.claimedBy != null || chip.PulledByPlayer)
            {
                pulling.RemoveAt(k);
                continue;
            }
            Vector2 to = pos - (Vector2)chip.transform.position;
            float d = to.magnitude;
            if (d > radius * 1.25f) { pulling.RemoveAt(k); continue; }   // dragged away — not ours any more
            chip.collectorPullStamp = Time.time;   // ours while it travels: tubes and belts on the way don't snatch it
            if (d <= holdRadius)
            {
                Park(chip);   // landed
                pulling.RemoveAt(k);
                activeUntil = Time.time + ActiveHold;
                gatherScanT = float.NegativeInfinity;
                continue;
            }
            Vector2 dir = to / d;
            float speed = Mathf.Lerp(pullSpeedNear, pullSpeedFar, Mathf.Clamp01(d / radius));
            chip.rb.linearVelocity = Vector2.MoveTowards(chip.rb.linearVelocity, dir * speed, pullAccel * dtF);
        }
    }

    // ------------------------------------------------------------------ pushing out (user wiring 2026-09-14)

    /// <summary>The pile FEEDS what stands beside the Collector: a Belt tile touching its
    /// footprint that carries AWAY from it gets a chip put on it (the only way onto a belt
    /// besides direct contact), and any chip consumer touching the footprint that takes a direct
    /// handover — a Tube with room, a crush generator standing open — is handed one
    /// (<see cref="IChipConsumer.TakeDelivered"/>). One chip per neighbour per tick, nearest
    /// parked chip first, in PRIORITY order (user rule 2026-09-14): Solo Generators first, then
    /// outward belts, then everything else. One-way — nothing beside it ever gives back (a belt
    /// pointing INTO the Collector just sets its chips down at the exit, and the pull parks them).</summary>
    void TickPushOut(float dt)
    {
        pushT -= dt;
        if (pushT > 0f) return;
        pushT = 0.25f;
        if (parked.Count == 0 || MarkedForDemolition) return;
        PushToConsumers(soloOnly: true);
        Vector2 me = transform.position;
        var lines = Belt.Lines;
        for (int l = 0; l < lines.Count && parked.Count > 0; l++)
        {
            var tiles = lines[l].members;
            for (int t = 0; t < tiles.Count && parked.Count > 0; t++)
            {
                var tile = tiles[t];
                if (tile == null || !tile.HasRoom) continue;
                if (!ChipConsumers.FootprintAdjacent(this, tile.Centre) || !tile.PointsAwayFrom(me)) continue;
                var chip = NearestParked(tile.Centre, null);
                if (chip != null && tile.TryLoad(chip)) Gave(chip);
            }
        }
        PushToConsumers(soloOnly: false);
    }

    /// <summary>Hand one parked chip to each adjacent consumer that takes a direct handover —
    /// the Solo Generators (a crusher needing no crush energy) in the priority pass, the rest after.</summary>
    void PushToConsumers(bool soloOnly)
    {
        var all = ChipConsumers.all;
        for (int k = 0; k < all.Count && parked.Count > 0; k++)
        {
            var c = all[k];
            bool solo = c is CrushGenerator g && g.crushEnergy <= 0f;
            if (solo != soloOnly) continue;
            if (!ChipConsumers.Active(c)) continue;
            var b = ChipConsumers.BuildingOf(c);
            if (b == null || b == this || !ChipConsumers.FootprintsAdjacent(this, b)) continue;
            var chip = NearestParked(b.transform.position, c);
            if (chip != null && c.TakeDelivered(chip)) Gave(chip);
        }
    }

    /// <summary>The parked chip nearest a point that nobody else has (and that a given customer accepts).</summary>
    OreChip NearestParked(Vector2 p, IChipConsumer forWhom)
    {
        OreChip best = null;
        float bd = float.MaxValue;
        for (int k = 0; k < parked.Count; k++)
        {
            var chip = parked[k];
            if (chip == null || chip.Absorbing || chip.Fading || chip.Stored) continue;
            if (chip.claimedBy != null || chip.PulledByPlayer) continue;
            if (forWhom != null && (!forWhom.AcceptsChip(chip.sizeClass, chip.element) || (chip.refined && forWhom.RefusesRefined))) continue;
            float d = ((Vector2)chip.transform.position - p).sqrMagnitude;
            if (d < bd) { bd = d; best = chip; }
        }
        return best;
    }

    void Gave(OreChip chip)
    {
        parked.Remove(chip);
        gatherScanT = float.NegativeInfinity;   // the count just changed — rescan next ask
        activeUntil = Time.time + ActiveHold;
    }

    // ------------------------------------------------------------------ the pile

    /// <summary>Stop the chip dead and switch its collider off: a parked chip can't be knocked
    /// by anything walking through (drones swallow by distance, the Hoover pulls by velocity —
    /// neither needs the body).</summary>
    void Park(OreChip chip)
    {
        chip.rb.linearVelocity = Vector2.zero;
        chip.rb.angularVelocity = 0f;
        var body = chip.GetComponent<Collider2D>();
        if (body != null) body.enabled = false;
        if (!parked.Contains(chip)) parked.Add(chip);
    }

    /// <summary>A parked chip that has left the pile — being swallowed, under the Hoover, or
    /// dragged off — gets its body back.</summary>
    void TickParked()
    {
        if (parked.Count == 0) return;
        Vector2 pos = transform.position;
        float keep = holdRadius * 1.5f, keep2 = keep * keep;
        for (int k = parked.Count - 1; k >= 0; k--)
        {
            var chip = parked[k];
            if (chip == null) { parked.RemoveAt(k); continue; }
            if (chip.Stored) { parked.RemoveAt(k); continue; }   // onto a belt / into a tube: its holder owns the body now
            bool gone = chip.Absorbing || chip.PulledByPlayer
                        || ((Vector2)chip.transform.position - pos).sqrMagnitude > keep2;
            if (!gone) continue;
            Release(chip);
            parked.RemoveAt(k);
        }
    }

    static void Release(OreChip chip)
    {
        if (chip == null) return;
        var body = chip.GetComponent<Collider2D>();
        if (body != null) body.enabled = true;
    }

    void ReleaseAll()
    {
        for (int k = 0; k < parked.Count; k++) Release(parked[k]);
        parked.Clear();
    }

    // ------------------------------------------------------------------ hover ring (drawn here, faded by the shared HoverRing)

    /// <summary>The reach circle in the ring's LOCAL space (local + no caps/corners — see the
    /// LineRenderer AABB gotcha). Shared with the kits and HoverRing so every ring matches.</summary>
    public static void DrawRing(LineRenderer lr, float r, int points = 64)
    {
        lr.useWorldSpace = false;
        lr.loop = true;
        lr.numCapVertices = 0;
        lr.numCornerVertices = 0;
        lr.positionCount = points;
        for (int i = 0; i < points; i++)
        {
            float a = i / (float)points * Mathf.PI * 2f;
            lr.SetPosition(i, new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f));
        }
    }
}
