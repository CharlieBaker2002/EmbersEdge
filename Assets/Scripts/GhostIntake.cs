using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The construction phase of an ORE-BUILT building. Placing a building no longer completes it:
/// <see cref="Building.BuildFirst"/> bolts one of these onto the ghost, which
///   • registers as an <see cref="IChipConsumer"/> (appeal 3 — construction outranks the
///     refiner's ore hunger), so the bag-drone fleet fetches chip for it exactly as it does for
///     a refiner or a chip wall, with no drone changes;
///   • runs its own suction ring: any settled, unclaimed chip inside the ring — dumped by a
///     drone or SPRAYED by the player's Hoover — eases into the building's centre and counts as
///     one ore, whatever its size;
///   • prints the art in as ore arrives: every SpriteRenderer of the building wears the
///     BlueprintFill2D material (BM.blueprintStyle picks the look) and its <c>_Fill</c> climbs so
///     each chip prints another batch of pixels. EVERY CHIP PRINTS AN EQUAL SHARE: the shader's
///     per-texel print order is not uniformly distributed (dark pixels cluster early, a raster
///     follows the silhouette), so on begin the ghost reads its sprite back once, replays the
///     style's order formula over every opaque texel, and maps "n of N chips" to the order
///     QUANTILE that prints exactly n/N of the pixels — no more tiny last step;
///   • spices the print with era-glow sparks along the expanding boundary while ore is landing
///     (<c>_Active</c> — the effect settles between deliveries);
///   • on the last chip restores the original materials and calls <see cref="Building.CompleteBuild"/>.
/// REBUILDS run the same phase: a destroyed base-side building gets one via <see cref="BeginRebuild"/>
/// (Building.OnDeath) wanting <see cref="Building.RebuildOreCost"/> chips — BM.rebuildOreFraction
/// of its build cost — and stands back up through <see cref="Building.CompleteRebuild"/>; medic
/// drones never tap an ore rebuild. A rebuild is DAY-GATED (user rule 2026-09-13): the wreck
/// takes no chip — from the fleet or the Hoover — until the day after it fell
/// (<see cref="Building.RebuildAllowedNow"/>; the Tube is exempt) — and NO wreck shows
/// its "n/N" counter until that day, the Tube included (it may already be eating).
/// Cancelling (Delete over the ghost) refunds every swallowed chip as loose scrap. Runs as a
/// coroutine so it survives the ghost's disabled-script state; lives on the building's own
/// GameObject and takes itself off on completion.
/// </summary>
public class GhostIntake : MonoBehaviour, IChipConsumer
{
    static readonly int FillId = Shader.PropertyToID("_Fill");
    static readonly int ProgressId = Shader.PropertyToID("_Progress");
    static readonly int BlueprintId = Shader.PropertyToID("_Blueprint");
    static readonly int OutlineId = Shader.PropertyToID("_Outline");
    static readonly int RectId = Shader.PropertyToID("_Rect");
    static readonly int StyleId = Shader.PropertyToID("_Style");
    static readonly int BrightnessId = Shader.PropertyToID("_Brightness");
    static readonly int ActiveId = Shader.PropertyToID("_Active");
    static readonly int JitterId = Shader.PropertyToID("_Jitter");
    static readonly int CentreId = Shader.PropertyToID("_Centre");
    static readonly int ReachId = Shader.PropertyToID("_Reach");
    static readonly int TexelWorldId = Shader.PropertyToID("_TexelWorld");
    // ONE ring per building: the combined centre of every sprite and the distance to the farthest corner
    Vector2 ringCentre;
    float ringReach = 1f;
    const float ActiveHold = 1.2f, ActiveFade = 0.8f, ActiveRise = 0.45f;   // the print "lives" this long after each chip, then settles; it wakes over ActiveRise
    float activeUntil = float.NegativeInfinity;
    float activeLevel;
    /// <summary>1 while ore is landing (and for a beat after), fading to 0 so the schematic settles.
    /// Eases IN over ActiveRise so the contour and sparks swell rather than snap.</summary>
    float Activity => activeLevel;
    void UpdateActivity()
    {
        float target = Mathf.Clamp01((activeUntil - Time.time) / ActiveFade);
        activeLevel = target > activeLevel ? Mathf.MoveTowards(activeLevel, target, Time.deltaTime / ActiveRise) : target;
    }
    const float ScanInterval = 0.15f;   // one swallow per scan at most → ~7 chips/s prints as a stream
    const float FillSpeed = 1.6f;       // progress units per second (a 4-chip building prints a chip's share in ~0.15 s)
    const float SparkRate = 14f;        // sparks/s along the boundary at full activity
    const int   SparkBurst = 10;        // sparks per chip landing

    static Material fillMat;

    Building b;
    struct Swallowed { public int size, element; public bool refined; }
    readonly List<Swallowed> swallowed = new List<Swallowed>();
    int inbound;
    SpriteRenderer[] srs;
    SpriteRenderer mainSR;
    Material[] saved;
    MaterialPropertyBlock mpb;
    float fillShown;             // PROGRESS 0..1 (share of pixels), eased toward delivered/required
    float nextScan;
    float ringScanT = float.NegativeInfinity;
    int ringStockCached;
    bool done;
    bool rebuild;                // a destroyed building printing back in (see BeginRebuild)
    int rebuildRequired;
    bool counterHidden;          // the "n/N" readout is off the wreck till the day after it fell (see CounterHidden)

    // order-quantile table per style (see class doc): sorted per-texel print orders
    readonly Dictionary<int, float[]> orderTables = new Dictionary<int, float[]>();
    ParticleSystem sparks;
    Material sparkMat;
    float sparkAcc;

    /// <summary>The building this intake is building (its placement order ranks the site).</summary>
    public Building Owner => b;
    public int Required => b == null ? 0 : rebuild ? rebuildRequired : Mathf.Max(0, b.oreRequired);
    public int Delivered => swallowed.Count;
    public int Remaining => Mathf.Max(0, Required - Delivered);

    /// <summary>Attach the construction phase to a freshly placed ghost (called from
    /// Building.BuildFirst, after SwitchMonos(false, init)).</summary>
    public static GhostIntake Begin(Building building)
    {
        var g = building.gameObject.AddComponent<GhostIntake>();
        g.Init(building);
        return g;
    }

    /// <summary>Attach the REBUILD phase to a just-destroyed building (Building.OnDeath, after
    /// SwitchMonos(false)): it wants the building's RebuildOreCost and completes via CompleteRebuild.</summary>
    public static GhostIntake BeginRebuild(Building building)
    {
        var g = building.gameObject.AddComponent<GhostIntake>();
        g.rebuild = true;
        g.rebuildRequired = Mathf.Max(1, building.RebuildOreCost);
        g.Init(building);
        return g;
    }

    void Init(Building building)
    {
        b = building;
        counterHidden = CounterHidden;
        RefreshCounter();
        ChipConsumers.Register(this);
        StartCoroutine(Run());   // visuals begin on its first tick — see Run
    }

    // ------------------------------------------------------------------ IChipConsumer

    /// <summary>A rebuild the day-gate still holds: wants nothing, accepts nothing, holds no
    /// dibs — chips sprayed at it just lie in the ring (and fade at the next clear cycle).</summary>
    bool Locked => rebuild && b != null && !b.RebuildAllowedNow;
    /// <summary>No "n/N" over a wreck until the day after it fell — even the Tube, which
    /// may already be taking its chip (user rule 2026-09-13).</summary>
    bool CounterHidden => rebuild && b != null && !b.DayAfterDeath;
    void RefreshCounter() => b.ShowOreProgress(Delivered, Required, !CounterHidden);
    public bool ChipIntakeActive => !done && b != null && (rebuild || !b.builtYet) && Remaining > 0 && !b.MarkedForDemolition && !Locked;
    public Vector2 ChipDropPoint => transform.position;
    /// <summary>Footprint half-extent plus the authored reach — a sprayed load only has to land
    /// NEAR the ghost.</summary>
    public float ChipIntakeRadius => b != null ? b.oreIntakeRadius + 0.5f * Mathf.Max(b.size.x, b.size.y) : 1.75f;
    /// <summary>Construction is the keenest customer: a ghost's chip is reserved before the
    /// refiner (2) or a chip wall (2) can bid for it.</summary>
    public int ChipAppeal(int sizeClass, int element) => 3;
    /// <summary>Planned in medium-chip space units (one ore = one chip of any size; the fleet
    /// plans hauls in bag space, so a medium chip's 2 is the honest rate), net of the ring stock.</summary>
    public float ChipDemandSpace => ChipIntakeActive ? Mathf.Max(0, Remaining - RingStock()) * 2f : 0f;
    public int InboundChipSpace { get => inbound; set => inbound = value; }
    public bool AcceptsChip(int sizeClass, int element) => Remaining > 0 && !Locked;

    /// <summary>Chips already settled inside the ring — as good as swallowed, so the fleet
    /// doesn't over-fetch. Cached on the scan cadence.</summary>
    int RingStock()
    {
        if (Time.time - ringScanT < 0.25f) return ringStockCached;
        ringScanT = Time.time;
        int n = 0;
        Vector2 pos = transform.position;
        float r = ChipIntakeRadius;
        for (int k = 0; k < OreChip.all.Count; k++)
        {
            var chip = OreChip.all[k];
            if (chip == null || chip.Absorbing || chip.transform.InDungeon()) continue;
            if (((Vector2)chip.transform.position - pos).sqrMagnitude > r * r) continue;
            n++;
        }
        ringStockCached = n;
        return n;
    }

    // ------------------------------------------------------------------ construction loop

    IEnumerator Run()
    {
        // Begin is called from inside Building.Start (BuildFirst); subclasses assign their own
        // material AFTER base.Start (Refiner/ChipFactory era mats, …), which would both clobber
        // the blueprint and be lost on restore. So the swap waits one frame and captures what
        // the building actually settled on.
        yield return null;
        if (b == null) yield break;
        BeginVisuals();
        while (!done)
        {
            if (b == null) yield break;
            UpdateActivity();
            TickCounter();
            float target = Required > 0 ? Delivered / (float)Required : 1f;
            if (fillShown < target)
            {
                fillShown = Mathf.MoveTowards(fillShown, target, Time.deltaTime * FillSpeed);
                ApplyFill(fillShown);
            }
            else if (Delivered >= Required)
            {
                Complete();
                yield break;
            }
            else ApplyFill(fillShown);   // idle ghost: keeps the live style/brightness/activity current
            TickSparks();
            if (Time.time >= nextScan)
            {
                nextScan = Time.time + ScanInterval;
                TickSuction();
            }
            yield return null;
        }
    }

    /// <summary>Show the "n/N" readout the moment the day rolls past the death day.</summary>
    void TickCounter()
    {
        bool hidden = CounterHidden;
        if (hidden == counterHidden) return;
        counterHidden = hidden;
        RefreshCounter();
    }

    /// <summary>The nearest settled, unclaimed chip in the ring eases into the building and
    /// counts as one ore. Same dibs gate every intake runs (nothing outbids construction).</summary>
    void TickSuction()
    {
        if (!ChipIntakeActive) return;
        Vector2 pos = transform.position;
        float r = ChipIntakeRadius;
        OreChip best = null;
        float bestSqr = float.MaxValue;
        for (int k = 0; k < OreChip.all.Count; k++)
        {
            var chip = OreChip.all[k];
            if (chip == null || chip.Absorbing || chip.transform.InDungeon()) continue;
            if (chip.claimedBy != null) continue;                   // a drone is flying for it
            if (chip.Age < 0.35f) continue;                         // let fresh drops pop in first
            if (!ChipConsumers.MayGive(this, chip.sizeClass, chip.element, chip.refined)) continue;
            float d = ((Vector2)chip.transform.position - pos).sqrMagnitude;
            if (d > r * r || d >= bestSqr) continue;
            best = chip;
            bestSqr = d;
        }
        if (best == null) return;
        swallowed.Add(new Swallowed { size = best.sizeClass, element = best.element, refined = best.refined });
        activeUntil = Time.time + ActiveHold + ActiveFade;   // wake the print effect
        ChipConsumers.CreditServed(this, best.SpaceCost);
        best.AbsorbInto(transform, 0f);
        ringStockCached = Mathf.Max(0, ringStockCached - 1);
        RefreshCounter();
        BurstSparks(SparkBurst);
    }

    void Complete()
    {
        done = true;
        RestoreVisuals();
        b.ShowOreProgress(Delivered, Required, false);
        ChipConsumers.Unregister(this);
        if (Application.isPlaying)
        {
            SpawnStrikeRing.Ring(transform.position, 1f, 0.5f * Mathf.Max(b.size.x, b.size.y));
            BurstSparks(SparkBurst * 2);
        }
        ReleaseSparks();
        if (rebuild) b.CompleteRebuild();
        else b.CompleteBuild();
        Destroy(this);
    }

    // ------------------------------------------------------------------ blueprint print

    void BeginVisuals()
    {
        if (fillMat == null) fillMat = Resources.Load<Material>("BlueprintFill");
        if (fillMat == null)
        {
            Debug.LogWarning("[GhostIntake] Resources/BlueprintFill.mat missing — construction runs without the print effect.");
            return;
        }
        var found = b.hasExtraParent && b.transform.parent != null
            ? b.transform.parent.GetComponentsInChildren<SpriteRenderer>(true)
            : b.GetComponentsInChildren<SpriteRenderer>(true);
        // the building's OWN art only: overlays hung under it (health bar, energy gauges — a Chip
        // Store's gauge sits off the far edge of its shape) would drag the ring centre and get
        // painted as blueprint
        var art = new List<SpriteRenderer>(found.Length);
        for (int i = 0; i < found.Length; i++)
            if (found[i] != null && !OverlayVisual.Owns(found[i])) art.Add(found[i]);
        srs = art.ToArray();
        saved = new Material[srs.Length];
        mpb ??= new MaterialPropertyBlock();
        // schematic colours off the ERA'S ORE (the purple the ore glows, not the pale ember tint):
        // a dark translucent body, a saturated contour/glow
        Color ore = OreHue();
        Color body = Color.Lerp(ore, Color.black, 0.6f); body.a = 0.32f;
        Color line = ore; line.a = 0.95f;
        // the building's ONE circle: centre of the combined sprite bounds, reaching the farthest corner
        bool any = false;
        Bounds all = new Bounds();
        for (int i = 0; i < srs.Length; i++)
        {
            var s = srs[i];
            if (s == null || s.sprite == null) continue;
            if (!any) { all = s.bounds; any = true; } else all.Encapsulate(s.bounds);
        }
        ringCentre = any ? (Vector2)all.center : (Vector2)transform.position;
        ringReach = any ? Mathf.Max(0.05f, new Vector2(all.extents.x, all.extents.y).magnitude) : 1f;
        float bestArea = -1f;
        for (int i = 0; i < srs.Length; i++)
        {
            var s = srs[i];
            if (s == null) continue;
            saved[i] = s.sharedMaterial;
            s.sharedMaterial = fillMat;
            s.color = Color.white;   // the shader tints the unprinted part; printed pixels stay true
            s.GetPropertyBlock(mpb);
            mpb.SetFloat(FillId, -0.001f);
            mpb.SetFloat(ProgressId, 0f);
            mpb.SetColor(BlueprintId, body);
            mpb.SetColor(OutlineId, line);
            mpb.SetVector(RectId, UvRect(s.sprite));
            mpb.SetVector(CentreId, new Vector4(ringCentre.x, ringCentre.y, 0f, 0f));
            mpb.SetFloat(ReachId, ringReach);
            mpb.SetFloat(TexelWorldId, s.sprite != null && s.sprite.textureRect.width > 0f ? s.bounds.size.x / s.sprite.textureRect.width : 0.03f);
            mpb.SetFloat(StyleId, (int)BM.Style);
            mpb.SetFloat(BrightnessId, BM.BlueprintBrightness);
            mpb.SetFloat(ActiveId, 0f);
            s.SetPropertyBlock(mpb);
            float area = s.bounds.size.x * s.bounds.size.y;
            if (s.sprite != null && area > bestArea) { bestArea = area; mainSR = s; }
        }
        if (Application.isPlaying) BuildSparks();
    }

    /// <summary>The era's ore colour as a saturated, non-HDR tint: the hue of the ore material's
    /// `thecolor` (Purple 1's deep purple in era 0) at full value — so contours and sparks read as
    /// the ore, not as a washed-out pink. Falls back to the era colour.</summary>
    static Color OreHue()
    {
        var m = DroneManager.OreSourceMaterial();
        if (m != null && m.HasProperty("thecolor"))
        {
            Color.RGBToHSV(m.GetColor("thecolor"), out float hue, out float sat, out _);
            return Color.HSVToRGB(hue, Mathf.Max(0.75f, sat), 1f);
        }
        return GS.ColFromEra();
    }

    /// <summary>The sprite's rect in texture UV space (x, y, w, h) — the shader's print raster and
    /// outline work in sprite-local space, so atlas neighbours never bleed in.</summary>
    static Vector4 UvRect(Sprite sp)
    {
        if (sp == null || sp.texture == null) return new Vector4(0f, 0f, 1f, 1f);
        Rect r = sp.textureRect;
        float w = sp.texture.width, h = sp.texture.height;
        return new Vector4(r.x / w, r.y / h, r.width / w, r.height / h);
    }

    void ApplyFill(float progress)
    {
        if (srs == null || mpb == null) return;
        int styleI = (int)BM.Style;
        float style = styleI, bright = BM.BlueprintBrightness;   // live: the inspector enum re-styles ghosts as they print
        float active = Activity;
        float fill = QuantileFill(styleI, progress);
        for (int i = 0; i < srs.Length; i++)
        {
            var s = srs[i];
            if (s == null) continue;
            s.GetPropertyBlock(mpb);
            mpb.SetFloat(FillId, fill);
            mpb.SetFloat(ProgressId, progress);
            mpb.SetFloat(StyleId, style);
            mpb.SetFloat(BrightnessId, bright);
            mpb.SetFloat(ActiveId, active);
            s.SetPropertyBlock(mpb);
        }
    }

    void RestoreVisuals()
    {
        if (srs == null) return;
        for (int i = 0; i < srs.Length; i++)
        {
            var s = srs[i];
            if (s == null) continue;
            if (saved != null && i < saved.Length && saved[i] != null) s.sharedMaterial = saved[i];
            s.SetPropertyBlock(null);
        }
        srs = null;
        saved = null;
    }

    // ------------------------------------------------------------------ equal share per chip (order quantiles)

    /// <summary>The shader's order threshold that prints exactly <paramref name="progress"/> of
    /// the opaque texels under the current style — the style's order formula replayed over the
    /// sprite once, sorted, and read at the quantile. Identity when the sprite can't be read.</summary>
    float QuantileFill(int style, float progress)
    {
        if (progress <= 0f) return -0.001f;
        if (progress >= 1f) return 1.001f;
        if (!orderTables.TryGetValue(style, out var table))
        {
            table = BuildOrderTable(style);
            orderTables[style] = table;
        }
        if (table == null || table.Length == 0) return progress;
        int idx = Mathf.Clamp(Mathf.RoundToInt(progress * (table.Length - 1)), 0, table.Length - 1);
        // the threshold sits just past the idx-th order so that texel prints too
        return table[idx] + 1e-4f;
    }

    /// <summary>Every opaque texel's print order under <paramref name="style"/>, sorted ascending.
    /// Mirrors the formulas in BlueprintFill2D.shader (the per-texel hash there is replaced by a
    /// uniform random here — statistically the same distribution, which is all a quantile needs).</summary>
    float[] BuildOrderTable(int style)
    {
        if (srs == null) return null;
        float jitter = fillMat != null && fillMat.HasProperty(JitterId) ? fillMat.GetFloat(JitterId) : 0.07f;
        var rng = new System.Random(GetInstanceID());
        var blockHash = new Dictionary<long, float>();
        var orders = new List<float>(4096);
        foreach (var sr in srs)
        {
            if (sr == null || sr.sprite == null || sr.sprite.texture == null) continue;
            Color32[] px;
            int w, h;
            try { px = ReadSpritePixels(sr.sprite, out w, out h); }
            catch (System.Exception e)
            {
                Debug.LogWarning("[GhostIntake] sprite readback failed (" + e.Message + ") — print shares are approximate.");
                return null;
            }
            if (px == null) continue;
            Rect rect = sr.sprite.textureRect;
            Bounds lb = sr.sprite.bounds;                                                 // sprite-local extents (world via the renderer's transform)
            AppendOrders(style, sr, px, w, h, rect, lb, jitter, rng, blockHash, orders);
        }
        if (orders.Count == 0) return null;
        orders.Sort();
        return orders.ToArray();
    }

    void AppendOrders(int style, SpriteRenderer sr, Color32[] px, int w, int h, Rect rect, Bounds lb, float jitter,
                      System.Random rng, Dictionary<long, float> blockHash, List<float> orders)
    {
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
            {
                var c = px[j * w + i];
                if (c.a <= 0) continue;
                float lx = (i + 0.5f) / w, ly = (j + 0.5f) / h;                       // sprite-local uv
                float lum = (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;
                float hv = (float)rng.NextDouble();
                // block (4×4 in TEXTURE texels, as the shader does it)
                int bx = Mathf.FloorToInt((rect.x + i) / 4f), by = Mathf.FloorToInt((rect.y + j) / 4f);
                long key = ((long)bx << 32) ^ (uint)by;
                if (!blockHash.TryGetValue(key, out float hb)) { hb = (float)rng.NextDouble(); blockHash[key] = hb; }
                float blx = Mathf.Clamp01(((bx + 0.5f) * 4f - rect.x) / w), bly = Mathf.Clamp01(((by + 0.5f) * 4f - rect.y) / h);
                float o;
                switch (style)
                {
                    case 1: o = Mathf.Clamp01(new Vector2((lx - 0.5f) * 2f, (ly - 0.5f) * 2f).magnitude) * (1f - jitter) + hv * jitter; break;
                    case 2: o = (lx + ly) * 0.5f * (1f - jitter) + hv * jitter; break;
                    case 3: o = Mathf.Lerp(hv, lum, 0.5f); break;
                    case 4: o = new Vector2(lx - 0.5f, ly * 1.15f).magnitude / 1.15f * (1f - jitter) + hv * jitter; break;
                    case 5: o = ly * 0.55f + hb * 0.45f; break;
                    case 6: o = new Vector2(blx - 0.5f, bly * 1.15f).magnitude / 1.15f * 0.85f + hb * 0.15f; break;
                    case 7: o = new Vector2(lx - 0.5f, ly * 1.15f).magnitude / 1.15f * (1f - jitter * 0.5f) + hv * jitter * 0.5f; break;
                    case 8: o = bly * 0.55f + hb * 0.45f; break;
                    case 9:
                    {
                        // the building's ONE circle: this texel's world position vs the shared centre/reach
                        Vector3 local = new Vector3(lb.min.x + lx * lb.size.x, lb.min.y + ly * lb.size.y, 0f);
                        Vector2 wp = sr.transform.TransformPoint(local);
                        float r = Mathf.Clamp01((wp - ringCentre).magnitude / ringReach);
                        o = r * 0.5f + Mathf.Pow(lum, 0.7f) * 0.35f + hv * 0.15f;
                        break;
                    }
                    default: o = ly * (1f - jitter) + hv * jitter; break;
                }
                orders.Add(Mathf.Clamp01(o));
            }
    }

    /// <summary>One-off CPU copy of the sprite's texels (textures aren't readable): blit the texture
    /// to a temporary render target and read the sprite's rect back. Rows come back bottom-up.</summary>
    static Color32[] ReadSpritePixels(Sprite sp, out int w, out int h)
    {
        Rect r = sp.textureRect;
        w = Mathf.Max(1, Mathf.RoundToInt(r.width));
        h = Mathf.Max(1, Mathf.RoundToInt(r.height));
        var tex = sp.texture;
        var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
        var prev = RenderTexture.active;
        Texture2D tmp = null;
        try
        {
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            tmp = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tmp.ReadPixels(new Rect(r.x, r.y, w, h), 0, 0);
            tmp.Apply();
            return tmp.GetPixels32();
        }
        finally
        {
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            if (tmp != null) Destroy(tmp);
        }
    }

    // ------------------------------------------------------------------ sparks (era glow along the boundary)

    void BuildSparks()
    {
        if (sparks != null || mainSR == null) return;
        var go = SpawnBoltFX.NewFX("BlueprintSparks", transform.position);
        sparks = go.AddComponent<ParticleSystem>();
        sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = sparks.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = 0.7f;
        main.startSpeed = 0f;
        main.startSize = 0.06f;
        main.startColor = Color.white;
        main.maxParticles = 256;
        var em = sparks.emission;
        em.enabled = false;
        var sol = sparks.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
        var r = go.GetComponent<ParticleSystemRenderer>();
        // the ORE's glow (Purple 1's `thecolor`, moderately HDR) on a soft dot — purple, not a white-hot core
        var src = DroneManager.OreSourceMaterial();
        if (src != null)
        {
            sparkMat = new Material(src);
            sparkMat.SetTexture("_MainTex", SpawnBoltFX.DotTex());
            if (sparkMat.HasProperty("_Emission")) sparkMat.SetTexture("_Emission", SpawnBoltFX.DotTex());
            if (sparkMat.HasProperty("thecolor")) sparkMat.SetColor("thecolor", src.GetColor("thecolor") * 1.4f);
        }
        else sparkMat = SpawnBoltFX.NewGlowMat(SpawnBoltFX.DotTex(), out _, 0.4f);
        r.sharedMaterial = sparkMat;
        r.sortingLayerName = "Power Ups";
        r.sortingOrder = 25;
        sparks.Play();
    }

    /// <summary>A point on the expanding boundary (the contour ellipse the shader draws), in world space.</summary>
    Vector3 BoundaryPoint(float progress)
    {
        // the building's ONE circle (the shader's ring): centre of all its sprites, reaching the farthest
        // corner at 100%; points outside a sprite are fine — sparks may leave the silhouette
        float rFront = Mathf.Clamp01(progress) * ringReach;
        float a = Random.Range(0f, Mathf.PI * 2f);
        return new Vector3(ringCentre.x + Mathf.Cos(a) * rFront, ringCentre.y + Mathf.Sin(a) * rFront, 0f);
    }

    void EmitSpark(float speedMul)
    {
        if (sparks == null || mainSR == null) return;
        Vector3 p = BoundaryPoint(fillShown);
        Vector3 outward = (p - (Vector3)ringCentre);
        outward = outward.sqrMagnitude > 1e-4f ? outward.normalized : (Vector3)Random.insideUnitCircle.normalized;
        var ep = new ParticleSystem.EmitParams
        {
            position = p + (Vector3)Random.insideUnitCircle * 0.03f,
            velocity = outward * Random.Range(0.25f, 0.6f) * speedMul + (Vector3)Random.insideUnitCircle * 0.12f,
            startSize = Random.Range(0.035f, 0.075f),
            startLifetime = Random.Range(0.45f, 0.9f),
            startColor = Color.white,
        };
        sparks.Emit(ep, 1);
    }

    void BurstSparks(int n)
    {
        for (int k = 0; k < n; k++) EmitSpark(1.6f);
    }

    /// <summary>A trickle of sparks along the boundary while ore is landing; nothing when settled.</summary>
    void TickSparks()
    {
        float a = Activity;
        if (a <= 0f || sparks == null) return;
        sparkAcc += Time.deltaTime * SparkRate * a;
        while (sparkAcc >= 1f) { sparkAcc -= 1f; EmitSpark(1f); }
    }

    void ReleaseSparks()
    {
        if (sparks == null) return;
        var go = sparks.gameObject;
        sparks.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        Destroy(go, 1.5f);      // let the last sparks fade
        if (sparkMat != null) Destroy(sparkMat, 1.6f);
        sparks = null;
        sparkMat = null;
    }

    void OnDestroy()
    {
        if (GS.qutting) return;
        ChipConsumers.Unregister(this);
        ReleaseSparks();
        if (done) return;
        // cancelled / destroyed mid-build: the ore comes back as loose scrap beside the site
        RestoreVisuals();
        if (!Application.isPlaying) return;
        foreach (var s in swallowed)
        {
            var back = DroneManager.SpawnScrap(transform.position + GS.RandCircle(0.2f, 0.8f), s.size, s.element);
            if (back != null) back.refined = s.refined;
        }
        swallowed.Clear();
    }
}
