using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The Hoover — a staff that harms nothing and gathers ORE. It sits in the weapon ring like any
/// gun (Tab cycles to it, right click uses it) but its trigger is a syphon:
///   • HOLD Shoot: loose ore chips inside the cone in front of the nozzle are pulled in and
///     swallowed, up to <see cref="capacity"/>. Each swallow is one ore, whatever the chip's size.
///     Started ON a Tube or Belt (the cursor over a box/tile at the moment of the press), the
///     chips of THAT tube shape / belt line lying in the cone step off one by one
///     (<see cref="tubeDrawInterval"/>) and are pulled in the same way — no other tube or belt
///     is touched, and a press started anywhere else never draws from one (user call 2026-09-14).
///   • PRESS Shoot while FULL (anywhere), or while holding ANY ore AT BASE: the load sprays back
///     out of the nozzle as chip props — into a ghost building's intake ring, a refiner's ring,
///     or just the ground for the drones to sort out.
/// Held ore is DATA (size + element per chip), so it rides the teleport home untouched while the
/// dungeon's litter is despawned. The ammo HUD shows the tank (held / capacity).
///
/// Look: ally-scheme pixel art whose emission map glows the era colour (GS.Glow, Lit level). While
/// syphoning the prefab's Syphon particle system (authored by HooverKitBuilder) streams inward
/// along the cone; each chip under pull trails a thin era-glow thread into the nozzle; a swallow
/// pulses a ring at the mouth and pops the part's scale. Single element throughout (the era).
/// </summary>
public class Hoover : WeaponScript
{
    [Header("Hoover")]
    [Tooltip("Ore the tank holds. Every swallowed chip is one ore regardless of its size class.")]
    public int capacity = 20;
    [Tooltip("Reach of the syphon cone from the nozzle (world units).")]
    public float range = 3.5f;
    [Tooltip("Half-angle of the syphon cone (degrees) around the aim.")]
    public float halfAngle = 30f;
    [Tooltip("Pull speed at the far edge of the cone → at the nozzle (u/s).")]
    public float pullSpeedFar = 2.5f;
    public float pullSpeedNear = 7f;
    [Tooltip("How fast a chip's skid is bent onto the pull (u/s²).")]
    public float pullAccel = 30f;
    [Tooltip("A chip this close to the nozzle — or to the staff between the player and the nozzle — is swallowed.")]
    public float captureRadius = 0.3f;
    [Tooltip("Inside this distance of the nozzle a chip is committed: it keeps being pulled even if the aim swings it out of the cone.")]
    public float nearRadius = 0.8f;
    [Tooltip("Seconds between chips leaving the nozzle on release.")]
    public float releaseInterval = 0.03f;
    [Tooltip("Speed the sprayed chips leave at (u/s), fanned ± sprayFanDegrees around the aim.")]
    public float releaseSpeed = 5f;
    public float sprayFanDegrees = 14f;
    [Tooltip("Recoil impulse per sprayed chip.")]
    public float sprayRecoil = 0.15f;
    [Tooltip("Seconds between chips drawn OFF A TUBE SHELF while the cone covers them (aim the held trigger at a tube to suck from it).")]
    public float tubeDrawInterval = 0.06f;

    [Header("Syphon FX")]
    [Tooltip("Particles/second the Syphon system streams while the trigger is held.")]
    public float syphonRate = 22f;
    [Tooltip("Most chips that get a glow thread at once (cheap LineRenderers, pooled).")]
    public int maxTethers = 8;
    public float tetherWidth = 0.018f;
    [Tooltip("Glow of the streaks / threads relative to the era glow (1 = spawn-strike strength).")]
    public float syphonGlow = 0.5f;
    public float tetherGlow = 0.6f;
    [Tooltip("Scale pop on a swallow (engagement above 1 → MechaSuit grows the part briefly).")]
    public float swallowPop = 0.35f;

    [Header("Prefab children (HooverKitBuilder wires these)")]
    [SerializeField] Transform nozzle;
    [SerializeField] ParticleSystem syphon;

    struct HeldChip { public int size, element; public bool refined; }
    readonly List<HeldChip> held = new List<HeldChip>();
    public int Held => held.Count;
    public bool Full => held.Count >= capacity;

    bool hooverStarted, syphoning, releasing;
    InputAction shootAction;
    Action<InputAction.CallbackContext> onDown, onUp;
    int rumbleId = -1;
    float pop, tubeDrawT;
    // what this press started on — the only tube shape / belt line it may draw from
    Tube sourceTube;
    Belt sourceBelt;

    // pull bookkeeping (chips currently under the cone this tick → tethers)
    readonly List<OreChip> pulled = new List<OreChip>();
    // chips whose collision with the player's body is suspended while under pull (a chip that
    // piles up against the body just short of the nozzle would otherwise never arrive)
    readonly HashSet<OreChip> ignoring = new HashSet<OreChip>();
    readonly List<OreChip> ignoreScratch = new List<OreChip>();
    Collider2D playerCol;
    readonly List<LineRenderer> tethers = new List<LineRenderer>();
    Material tetherMat, syphonMat;
    Color tetherBase, syphonBase;
    Texture2D dotTex;

    // ------------------------------------------------------------------ part lifecycle

    public override void StartPart(MechaSuit mecha)
    {
        base.StartPart(mecha);
        ApplyGlow();
        RefreshHud();
    }

    void ApplyGlow()
    {
        if (sr != null) sr.material = GS.Glow(GlowLevel.Lit);   // body lit, emission map = era glow
        // FX materials are era glow (Super); the era hue is a global, so once is enough
        if (tetherMat != null) Destroy(tetherMat);
        if (syphonMat != null) Destroy(syphonMat);
        tetherMat = SpawnBoltFX.NewGlowMat(SpawnBoltFX.ThreadTex(), out tetherBase, tetherGlow);
        syphonMat = SpawnBoltFX.NewGlowMat(DotTex(), out syphonBase, syphonGlow);
        foreach (var lr in tethers) if (lr != null) lr.sharedMaterial = tetherMat;
        if (syphon != null) syphon.GetComponent<ParticleSystemRenderer>().sharedMaterial = syphonMat;
    }

    protected override void Awake()
    {
        // the ammo HUD is the ore tank: clip = capacity, in-clip = held, no reserve
        ammoPerClip = capacity;
        ammoInClip = 0;
        totalAmmo = 0;
        maximumAmmo = 0;
        attackReset = 0f;
        option = -1f;   // hold-to-use (documentation only — input is wired below, not by the base)
    }

    protected override void Start()
    {
        CS = CharacterScript.CS;
        hooverStarted = true;
        if (syphon != null)
        {
            var em = syphon.emission;
            em.rateOverTime = 0f;
            if (!syphon.isPlaying) syphon.Play();
        }
        OnEnable();
    }

    protected override void OnEnable()
    {
        if (cd != null)
        {
            cd.offset = new Vector2(0f, sr.sprite.rect.height * 0.5f * 0.015625f);
            cd.paused = false;
        }
        transform.localPosition = transform.localPosition.normalized * 0.1f;
        engagement = 1f;
        if (!hooverStarted) return;
        shootAction = IM.i.pi.Player.Shoot;
        onDown ??= _ => OnShootDown();
        onUp ??= _ => StopSyphon();
        shootAction.started += onDown;
        shootAction.canceled += onUp;
        this.QA(RefreshHud, 0f);   // after SwapWeapons' InitWeapon has written its own text
    }

    protected override void OnDisable()
    {
        if (cd != null)
        {
            cd.offset = new Vector2(0f, sr.sprite.rect.height * 0.25f * 0.015625f);
            cd.paused = true;
        }
        StopSyphon();
        engagement = 0f;
        transform.localRotation = Quaternion.identity;
        transform.localPosition = transform.localPosition.normalized * (MechaSuit.poweredDist - sr.sprite.rect.height * 0.25f * 0.015625f);
        if (!hooverStarted || shootAction == null) return;
        shootAction.started -= onDown;
        shootAction.canceled -= onUp;
    }

    void OnDestroy()
    {
        foreach (var lr in tethers) if (lr != null) Destroy(lr.gameObject);
        tethers.Clear();
        if (tetherMat != null) Destroy(tetherMat);
        if (syphonMat != null) Destroy(syphonMat);
        if (dotTex != null) Destroy(dotTex);
    }

    /// <summary>The ammo HUD's "reserve" line is repurposed as the tank readout; the base's
    /// reload (R key / auto on empty) has nothing to do here.</summary>
    public override void Reload()
    {
        RefreshHud();
    }

    void RefreshHud()
    {
        ammoInClip = held.Count;
        ammoPerClip = capacity;
        if (AmmoSlider.i == null || CharacterScript.CS == null) return;
        if (CharacterScript.CS.weapons.Count == 0 || CharacterScript.CS.weapons[CharacterScript.CS.weaponIndex] != this) return;
        AmmoSlider.i.UpdateMax(capacity);
        AmmoSlider.i.UpdateSlider(held.Count);
        if (AmmoSlider.i.clipsText != null)
            AmmoSlider.i.clipsText.text = Full ? "FULL — release" : $"{held.Count}/{capacity} Ore";
    }

    // ------------------------------------------------------------------ input

    void OnShootDown()
    {
        if (!GS.CanAct() || releasing) return;
        bool atBase = PathZone.AtBase(transform.position);
        if (held.Count > 0 && (Full || atBase))
        {
            StartCoroutine(ReleaseCo());
            return;
        }
        if (Full) return;
        PickSource();
        syphoning = true;
        SetSyphonFX(true);
        rumbleId = IM.i.Rumble(30f, 0, true, true, 0.06f, 0.14f);
    }

    void StopSyphon()
    {
        if (!syphoning) return;
        syphoning = false;
        sourceTube = null;
        sourceBelt = null;
        SetSyphonFX(false);
        if (rumbleId != -1) { IM.i.BlockVB(rumbleId); rumbleId = -1; }
        pulled.Clear();
        RestoreDroppedCollisions();
        HideTethers(0);
    }

    // ------------------------------------------------------------------ frame

    protected override void Update()
    {
        if (CharacterScript.CS != null && CharacterScript.CS.quickAim)
            transform.rotation = GS.VTQ(IM.i.MousePosition(transform.position, true));

        if (pop > 0f)
        {
            pop = Mathf.MoveTowards(pop, 0f, Time.deltaTime * 2.5f);
            engagement = 1f + pop;
        }
        else if (enabled) engagement = 1f;
    }

    /// <summary>Nozzle world position: the authored child, else the top of the sprite.</summary>
    Vector2 Mouth => nozzle != null
        ? (Vector2)nozzle.position
        : (Vector2)transform.position + (Vector2)transform.up * (sr.sprite.rect.height / sr.sprite.pixelsPerUnit) * transform.lossyScale.y;

    void FixedUpdate()
    {
        if (!syphoning) return;
        if (!GS.CanAct() || Full) { StopSyphon(); return; }

        Vector2 mouth = Mouth;
        Vector2 aim = transform.up;
        Vector2 body = CharacterScript.CS != null ? (Vector2)CharacterScript.CS.transform.position : (Vector2)transform.position;
        if (playerCol == null && CharacterScript.CS != null) playerCol = CharacterScript.CS.GetComponent<Collider2D>();
        float cosHalf = Mathf.Cos(halfAngle * Mathf.Deg2Rad);
        float dt = Time.fixedDeltaTime;
        var mf = MineField.i;
        pulled.Clear();

        for (int k = OreChip.all.Count - 1; k >= 0; k--)   // Capture removes from `all`
        {
            var chip = OreChip.all[k];
            if (chip == null || chip.Absorbing || chip.rb == null) continue;
            Vector2 cp = chip.transform.position;
            Vector2 to = cp - mouth;
            float d = to.magnitude;
            if (d > range) continue;
            // swallowed at the nozzle, or hugging the staff between the body and the nozzle
            if (d <= captureRadius || DistToSegment(body, mouth, cp) <= captureRadius)
            {
                Capture(chip, mouth);
                if (Full) break;
                continue;
            }
            Vector2 dir = to / d;
            if (d > nearRadius && Vector2.Dot(dir, aim) < cosHalf) continue;   // outside the cone (near chips stay committed)
            if (mf != null && !LineOfSight(mf, mouth, cp)) continue;

            float near = 1f - d / range;
            float speed = Mathf.Lerp(pullSpeedFar, pullSpeedNear, near * near);
            Vector2 want = -dir * speed;
            chip.rb.linearVelocity = Vector2.MoveTowards(chip.rb.linearVelocity, want, pullAccel * dt);
            chip.claimedBy = null;   // the player outranks the fleet; drones re-plan if it vanishes
            chip.playerPullStamp = Time.time;   // and building magnets (Collector) keep off it
            pulled.Add(chip);
            if (playerCol != null && ignoring.Add(chip))
            {
                var cc = chip.GetComponent<Collider2D>();
                if (cc != null) Physics2D.IgnoreCollision(cc, playerCol, true);   // let it through the body
            }
        }
        if (!Full) PullFromSource(mouth, aim, cosHalf, mf, dt);
        RestoreDroppedCollisions();
    }

    /// <summary>At the press: the tube box or belt tile under the cursor (a controller aims
    /// instead — the first box/tile along the aim within range) is the ONLY store this syphon
    /// may draw from. Anywhere else = loose chips only.</summary>
    void PickSource()
    {
        sourceTube = null;
        sourceBelt = null;
        if (!IM.controller)
        {
            Vector2 p = IM.i.MouseWorld();
            sourceTube = Tube.At(BaseCell.Of(p));
            if (sourceTube == null) sourceBelt = Belt.At(BaseCell.Of(p));
            return;
        }
        Vector2 mouth = Mouth;
        Vector2 aim = transform.up;
        float step = BaseCell.Cs * 0.5f;
        for (float t = 0f; t <= range; t += step)
        {
            var c = BaseCell.Of(mouth + aim * t);
            sourceTube = Tube.At(c);
            if (sourceTube != null) return;
            sourceBelt = Belt.At(c);
            if (sourceBelt != null) return;
        }
    }

    /// <summary>Started on a tube / belt: the chip of THAT shape / line nearest the nozzle that
    /// lies inside the cone (in range, in sight) steps off — loose, at rest, stamped as the
    /// player's — and the next tick's pull takes it like any chip; one per
    /// <see cref="tubeDrawInterval"/> so it streams out rather than bursting. Re-resolved through
    /// the box/tile every tick, so a re-link (a box or tile placed nearby) keeps the source; a
    /// tube chip a drone has claimed stays put.</summary>
    void PullFromSource(Vector2 mouth, Vector2 aim, float cosHalf, MineField mf, float dt)
    {
        tubeDrawT -= dt;
        if (tubeDrawT > 0f) return;
        float bd = float.MaxValue;
        if (sourceTube != null && sourceTube.cluster != null)
        {
            var cl = sourceTube.cluster;
            TubeCluster.Entry best = null;
            for (int i = 0; i < cl.chips.Count; i++)
            {
                var e = cl.chips[i];
                var chip = e.chip;
                if (chip == null || chip.Absorbing || chip.Fading || chip.claimedBy != null) continue;
                if (InCone(e.pos, mouth, aim, cosHalf, mf, ref bd)) best = e;
            }
            if (best != null && cl.ReleaseTo(best) != null) tubeDrawT = Mathf.Max(0.01f, tubeDrawInterval);
        }
        else if (sourceBelt != null && sourceBelt.line != null)
        {
            var line = sourceBelt.line;
            BeltLine.Entry best = null;
            for (int i = 0; i < line.chips.Count; i++)
            {
                var e = line.chips[i];
                var chip = e.chip;
                if (chip == null || chip.Absorbing || chip.Fading) continue;
                if (InCone(chip.transform.position, mouth, aim, cosHalf, mf, ref bd)) best = e;
            }
            if (best != null && line.ReleaseTo(best) != null) tubeDrawT = Mathf.Max(0.01f, tubeDrawInterval);
        }
    }

    /// <summary>In the cone (in range, in sight) and nearer than the best so far — which it becomes.</summary>
    bool InCone(Vector2 p, Vector2 mouth, Vector2 aim, float cosHalf, MineField mf, ref float bd)
    {
        Vector2 to = p - mouth;
        float d = to.magnitude;
        if (d > range || d >= bd) return false;
        if (d > nearRadius && Vector2.Dot(to / Mathf.Max(d, 1e-4f), aim) < cosHalf) return false;   // outside the cone
        if (mf != null && !LineOfSight(mf, mouth, p)) return false;
        bd = d;
        return true;
    }

    /// <summary>Chips no longer under pull get their body collision back (swallowed ones are
    /// gone anyway). Cheap: the set only ever holds the handful of chips in the cone.</summary>
    void RestoreDroppedCollisions()
    {
        if (ignoring.Count == 0) return;
        ignoreScratch.Clear();
        foreach (var c in ignoring)
            if (c == null || c.Absorbing || !pulled.Contains(c)) ignoreScratch.Add(c);
        for (int i = 0; i < ignoreScratch.Count; i++)
        {
            var c = ignoreScratch[i];
            ignoring.Remove(c);
            if (c == null || c.Absorbing || playerCol == null) continue;
            var cc = c.GetComponent<Collider2D>();
            if (cc != null) Physics2D.IgnoreCollision(cc, playerCol, false);
        }
    }

    static float DistToSegment(Vector2 a, Vector2 b, Vector2 p)
    {
        Vector2 ab = b - a;
        float len2 = ab.sqrMagnitude;
        if (len2 < 1e-6f) return (p - a).magnitude;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
        return (p - (a + ab * t)).magnitude;
    }

    /// <summary>Cheap wall test along the segment (tile grid samples) — chips behind rock stay put.</summary>
    static bool LineOfSight(MineField mf, Vector2 a, Vector2 b)
    {
        float step = Mathf.Max(0.05f, mf.CellSize * 0.5f);
        Vector2 d = b - a;
        float len = d.magnitude;
        if (len < step) return true;
        Vector2 u = d / len;
        for (float t = step; t < len; t += step)
            if (mf.IsSolidWorld(a + u * t)) return false;
        return true;
    }

    void Capture(OreChip chip, Vector2 mouth)
    {
        held.Add(new HeldChip { size = chip.sizeClass, element = chip.element, refined = chip.refined });
        chip.AbsorbInto(nozzle != null ? nozzle : transform, 0f);
        SpawnStrikeRing.Ring(mouth, 0.7f, 0.18f);
        pop = swallowPop;
        IM.i.Rumble(0.05f, 0, true, true, 0.25f, 0.35f, 0.6f);
        RefreshHud();
        if (Full)
        {
            StopSyphon();
            CM.Message("Hoover full — right click to release", false);
        }
    }

    void LateUpdate()
    {
        if (!syphoning || pulled.Count == 0) { HideTethers(0); return; }
        Vector2 mouth = Mouth;
        int n = Mathf.Min(pulled.Count, maxTethers);
        for (int i = 0; i < n; i++)
        {
            var chip = pulled[i];
            var lr = Tether(i);
            if (chip == null) { lr.enabled = false; continue; }
            lr.transform.position = mouth;
            Vector3 end = (Vector2)chip.transform.position - mouth;
            if (end.sqrMagnitude < SpawnBoltFX.MIN_SEG * SpawnBoltFX.MIN_SEG) end = Vector3.up * SpawnBoltFX.MIN_SEG;
            lr.enabled = true;
            lr.SetPosition(0, end);            // tail out at the chip …
            lr.SetPosition(1, Vector3.zero);   // … hot head at the mouth (strip texture u = 1)
            float near = 1f - Mathf.Clamp01(end.magnitude / range);
            lr.widthMultiplier = tetherWidth * (0.6f + 0.5f * near);
            SpawnBoltFX.SetBrightness(lr.sharedMaterial, tetherBase, 0.25f + 0.55f * near);
        }
        HideTethers(n);
    }

    LineRenderer Tether(int i)
    {
        while (tethers.Count <= i)
        {
            var go = new GameObject("HooverTether") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(GS.FindParent(GS.Parent.fx), false);
            if (tetherMat == null) tetherMat = SpawnBoltFX.NewGlowMat(SpawnBoltFX.ThreadTex(), out tetherBase, tetherGlow);
            var lr = SpawnBoltFX.NewLR(go, new Material(tetherMat), 27);   // own copy: per-thread brightness
            lr.positionCount = 2;
            lr.enabled = false;
            tethers.Add(lr);
        }
        return tethers[i];
    }

    void HideTethers(int from)
    {
        for (int i = from; i < tethers.Count; i++) if (tethers[i] != null) tethers[i].enabled = false;
    }

    void SetSyphonFX(bool on)
    {
        if (syphon == null) return;
        if (syphonMat != null)
        {
            var psr = syphon.GetComponent<ParticleSystemRenderer>();
            if (psr.sharedMaterial != syphonMat) psr.sharedMaterial = syphonMat;
        }
        var em = syphon.emission;
        em.rateOverTime = on ? syphonRate : 0f;
        if (on && !syphon.isPlaying) syphon.Play();
    }

    /// <summary>Soft radial dot for the syphon streaks (cached per Hoover).</summary>
    Texture2D DotTex()
    {
        if (dotTex != null) return dotTex;
        const int S = 16;
        dotTex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = (x + 0.5f) / S - 0.5f, dy = (y + 0.5f) / S - 0.5f;
                float r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                float a = 1f - Mathf.SmoothStep(0.15f, 1f, r);
                dotTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        dotTex.Apply();
        return dotTex;
    }

    // ------------------------------------------------------------------ release

    IEnumerator ReleaseCo()
    {
        releasing = true;
        StopSyphon();
        var AS = CharacterScript.CS != null ? CharacterScript.CS.AS : null;
        if (cd != null) cd.SetValue(Mathf.Max(0.1f, held.Count * releaseInterval));
        IM.i.Rumble(Mathf.Max(0.1f, held.Count * releaseInterval), 0, true, true, 0.2f, 0.4f);
        while (held.Count > 0)
        {
            var h = held[held.Count - 1];
            held.RemoveAt(held.Count - 1);
            Vector2 mouth = Mouth;
            Vector2 aim = transform.up;
            Vector2 dir = aim.Rotated(UnityEngine.Random.Range(-sprayFanDegrees, sprayFanDegrees));
            Vector3 p = (Vector3)(mouth + dir * 0.12f);
            var chip = DroneManager.SpawnScrap(p, h.size, h.element, mouth - aim * 0.5f);
            if (chip != null) chip.refined = h.refined;
            if (chip != null && chip.rb != null)
                chip.rb.linearVelocity = dir * (releaseSpeed * UnityEngine.Random.Range(0.8f, 1.2f));
            if (AS != null) AS.TryAddForce(-aim * sprayRecoil, true);
            pop = Mathf.Max(pop, 0.12f);
            RefreshHud();
            yield return new WaitForSeconds(releaseInterval);
        }
        releasing = false;
        RefreshHud();
    }
}
