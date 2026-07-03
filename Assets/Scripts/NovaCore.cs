using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// The deployed Nova effect. It lives on its own prefab (Prefabs/AbilityProj/Nova) so
// every starting/feel value is authored in the inspector (the fields below) rather
// than hard-coded — the script only does the dynamic, per-cast work on top of them.
// NovaSpell instantiates that prefab and calls Begin(). The particle systems are built
// in code (no giant emitter YAML) and all share the unlit-red glow material + the
// JLVisual sprite-sheet animated over each particle's lifetime; per-grain colours are
// GREYSCALE (the shader applies the red). Three beats:
//   1. GATHER  — energy spirals inward while held; a central CORE knot grows in radius.
//   2. IMPLODE — on release the core particles are compressed inward (ease-in-sine) and
//      vibrate with rising frequency before the blast.
//   3. DETONATE — a rotating multi-arm pinwheel + lingering embers + a screen Shockwave.
// The whole effect is sized to the blast radius `r` (visual spread == hitbox). ONE
// element only (red, via the shader). See embers-edge palette.
public class NovaCore : MonoBehaviour
{
    const float TAU = 6.28318530718f;
    const string SortingLayer = "Power Ups"; // above the action; same layer Shockwave uses

    // ---- authored starting values (set on the Nova prefab) --------------

    [Header("Particle shade (GREYSCALE — the unlit-red shader tints it red)")]
    [Tooltip("Bright greyscale shade (→ bright red through the shader). Used for core/gather/pinwheel grains.")]
    [SerializeField] Color brightShade = new(1f, 1f, 1f);
    [Tooltip("Deep greyscale shade (→ deep red). Used for the lingering embers.")]
    [SerializeField] Color deepShade = new(0.5f, 0.5f, 0.5f);
    [Tooltip("Fraction of grains that are the BRIGHT shade at charge start — the rest are deep. The visual 'heats up' by shifting this proportion (not by tinting every grain over time).")]
    [SerializeField] float brightShareStart = 0.15f;
    [Tooltip("Fraction of grains that are the BRIGHT shade at full charge.")]
    [SerializeField] float brightShareEnd = 0.85f;

    [Header("Brightness (grey tint on the material; >~1 blooms)")]
    [Tooltip("Material tint at full charge — pushes into HDR/bloom.")]
    [SerializeField] float peakBrightness = 2.6f;
    [Tooltip("Detonation-flash floor on a quick tap (lerps up to peakBrightness with charge).")]
    [SerializeField] float detonationFlashMin = 1.4f;

    [Header("Charge")]
    [Tooltip("Seconds of hold to reach a full charge.")]
    [SerializeField] float maxCharge = 2.2f;

    [Header("Gather ring radius = baseRadius × lerp(start, end, charge) — grows as it charges")]
    [SerializeField] float gatherRadiusStart = 0.55f;
    [SerializeField] float gatherRadiusEnd = 0.72f;
    [Tooltip("How far inward (0..1) gather grains travel before fading — <1 keeps the inner section less dense (they don't all pile at the centre).")]
    [SerializeField] float gatherReach = 0.7f;

    [Header("Core knot radius = baseRadius × lerp(start, end, charge) — grows while charging")]
    [SerializeField] float coreRadiusStart = 0.05f;
    [SerializeField] float coreRadiusEnd = 0.5f;
    [Tooltip("Fraction of the implosion the core takes to rush into the singularity (small = comes in fast, then just vibrates in place).")]
    [SerializeField] float coreCompressFraction = 0.25f;

    [Header("Channel damage-over-time (vortex chews what's underneath)")]
    [Tooltip("Seconds between channel damage ticks.")]
    [SerializeField] float channelDotInterval = 0.25f;
    [Tooltip("Channel damage grows from this fraction at charge start up to full at max charge (the radius follows the growing core knot).")]
    [SerializeField] float channelChargeFloor = 0.5f;

    [Header("Blast radius = baseRadius × (floor + gain × charge)")]
    [SerializeField] float blastRadiusFloor = 0.62f;
    [SerializeField] float blastRadiusGain = 0.46f;

    [Header("Implosion wind-up")]
    [Tooltip("Collapse duration on a quick release.")]
    [SerializeField] float implosionTimeMin = 0.4f;
    [Tooltip("Full-channel collapse duration at level 1.")]
    [SerializeField] float implosionTimeMaxBase = 1.25f;
    [Tooltip("Extra full-channel collapse seconds per spell level (lvl1/2/3 = 1.25/1.75/2.25 by default).")]
    [SerializeField] float implosionTimeMaxPerLevel = 0.5f;
    [Tooltip("Core grains emitted for the implosion at level 1 (doubles per level) — the ones that compress & vibrate.")]
    [SerializeField] int implosionGrainsPerLevel = 60;
    [Tooltip("How small the core compresses to, as a fraction of baseRadius (the singularity).")]
    [SerializeField] float singularitySize = 0.008f;
    [Tooltip("Implosion vibration amplitude in WORLD UNITS — keep small; it's a tight buzz, not scaled by the blast size.")]
    [SerializeField] float implosionShakeStrength = 0.12f;
    [Tooltip("Vibration frequency the implosion ramps UP to as it collapses (builds tension). Starts at ~12% of this.")]
    [SerializeField] float implosionShakeFreqMax = 8f;

    [Header("Pinwheel arms (reads as the charge gauge)")]
    [SerializeField] int armsMin = 3; // quick release
    [SerializeField] int armsMax = 7; // full channel

    [Header("Damage & crowd-control")]
    [Tooltip("Share of full damage dealt at the very rim (centre is full).")]
    [SerializeField] float rimDamageFraction = 0.08f;
    [Tooltip("Knockback impulse coefficient (× target mass × falloff²).")]
    [SerializeField] float knockbackForce = 18f;
    [Tooltip("Centre stun seconds on a quick tap (every enemy in the blast is stunned).")]
    [SerializeField] float stunBase = 0.5f;
    [Tooltip("Extra centre stun seconds added at full charge.")]
    [SerializeField] float stunPerCharge = 0.8f;
    [Tooltip("Share of the stun duration applied at the very rim (scales up to full at the centre).")]
    [SerializeField] float stunRimFraction = 0.35f;

    [Header("Lifecycle")]
    [Tooltip("Seconds the embers linger after the blast before the object despawns.")]
    [SerializeField] float teardownDelay = 2.8f;

    // ---- runtime state (set by Begin / scaled per cast) ----------------

    Material baseMat;
    int level = 1;
    float atr;
    string casterTag = "Allies";
    float radius = 3.5f;   // full-charge blast radius (set by NovaSpell.Begin)
    float damage = 6f;     // full-charge core (centre) explosion damage (set by NovaSpell.Begin)
    float channelDps = 3f; // full-charge channel DPS (set by NovaSpell.Begin)
    float maxRange = 4.5f; // (set by NovaSpell.Begin)
    Vector3 castOrigin;    // fixed point the nova roams around — captured at cast, not the player
    Vector3 followVel;     // SmoothDamp velocity state for the cursor follow

    float chargeT;
    float charge01;
    bool charging;
    bool detonated;
    float brightness = 1f;
    float dotTimer;
    float countMul = 1f;   // particle-count multiplier; grows 50% per level so the bigger blast stays full

    readonly List<Material> mats = new();
    ParticleSystem gather, burst, ember, core;

    static Sprite[] s_frames;                  // JLVisual sprite-sheet frames, ordered 0..n

    // Big overlap buffer so the blast NEVER caps the enemies it hits. GS.FindEnemies'
    // default buffer is only 10 and OverlapCircle isn't distance-sorted, so a crowded
    // blast would drop arbitrary (often close) enemies. 256 is far beyond any real count.
    static readonly Collider2D[] s_overlap = new Collider2D[256];

    // ---- public API (called by NovaSpell) -------------------------------

    public void Begin(Material particleMat, int lvl, float intellect, string tag, float baseRadius, float baseDamage, float channelDpsFull, float range)
    {
        baseMat = particleMat != null ? particleMat : Resources.Load<Material>("Sprite-Unlit-Default");
        level = Mathf.Max(1, lvl);
        atr = intellect;
        casterTag = string.IsNullOrEmpty(tag) ? "Allies" : tag;
        radius = baseRadius;
        damage = baseDamage;
        channelDps = channelDpsFull;
        rimDamageFraction = 0.25f;          // rim explosion = 25% of the centre damage
        maxRange = range;
        maxCharge = level + 0.6f;           // max castable charge per level: lvl1/2/3 = 1.6/2.6/3.6 seconds
        armsMax = 4 + level;                // max pinwheel arms per level: lvl1/2/3 = 5/6/7
        countMul = 1f + 0.5f * (level - 1); // counts grow 50% per level to fill the larger radius

        // always spawn 0.5 units from the player (toward the aim), then forget the
        // player — from here we roam around this fixed origin (eased toward the cursor).
        Vector3 anchor = CharacterScript.CS != null ? CharacterScript.CS.transform.position : transform.position;
        Vector3 toCursor = ResolveCursor(anchor) - anchor;
        Vector3 aimDir = toCursor.sqrMagnitude > 0.0001f ? toCursor.normalized : Vector3.up;
        castOrigin = anchor + aimDir * 0.5f;
        castOrigin.z = transform.position.z;
        transform.position = castOrigin;

        gather = MakeGatherSystem();
        burst  = MakeBurstSystem();
        ember  = MakeEmberSystem();
        core   = MakeCoreSystem();
        ApplyBrightness(1f);

        charging = true;
        chargeT = 0f;
    }

    public void Detonate(float heldSeconds)
    {
        if (detonated) return;
        detonated = true;
        charging = false;
        StartCoroutine(DetonateRoutine(Mathf.Clamp01(heldSeconds / maxCharge)));
    }

    // ---- charge --------------------------------------------------------

    void Update()
    {
        if (!charging) return;

        chargeT += Time.deltaTime;
        charge01 = Mathf.Clamp01(chargeT / maxCharge);

        FollowMouse();

        // the gathering vortex chews on anything caught underneath it (damage-over-time)
        dotTimer += Time.deltaTime;
        if (dotTimer >= channelDotInterval)
        {
            dotTimer -= channelDotInterval;
            ChannelTick();
        }

        float brightChance = Mathf.Lerp(brightShareStart, brightShareEnd, charge01); // more bright grains as it heats up
        var ep = new ParticleSystem.EmitParams();

        // inward spiral stream — the gather ring widens as it charges
        float gatherR = radius * Mathf.Lerp(gatherRadiusStart, gatherRadiusEnd, charge01);
        int n = Mathf.CeilToInt((160f + 420f * charge01) * countMul * Time.deltaTime);
        for (int i = 0; i < n; i++)
        {
            float ang = Random.value * TAU;
            Vector3 p = transform.position + new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f) * gatherR * Random.Range(0.85f, 1.05f);
            ep.position = p;
            ep.velocity = Vector3.zero; // orbital + radial handled by velocityOverLifetime
            ep.startColor = GrainShade(brightChance);
            ep.startSize = Random.Range(0.08f, 0.2f) * (0.8f + 0.5f * charge01);
            // fade before reaching the dead centre (travel only `gatherReach` of the way in)
            // so the inner section stays sparse rather than piling up
            ep.startLifetime = (gatherR / (radius * 1.3f)) * gatherReach * Random.Range(0.85f, 1.15f);
            gather.Emit(ep, 1);
        }

        // the CORE knot — a live, refreshing blob whose radius grows as it charges
        float coreR = radius * Mathf.Lerp(coreRadiusStart, coreRadiusEnd, charge01);
        int cn = Mathf.CeilToInt((60f + 160f * charge01) * countMul * Time.deltaTime);
        for (int i = 0; i < cn; i++)
        {
            float ang = Random.value * TAU;
            Vector3 d = new(Mathf.Cos(ang), Mathf.Sin(ang), 0f);
            ep.position = transform.position + d * (coreR * Mathf.Sqrt(Random.value)); // filled disk
            ep.velocity = Vector3.zero;                  // sits in place (radial is 0 until implosion)
            ep.startColor = GrainShade(brightChance);
            ep.startSize = Random.Range(0.1f, 0.22f);
            ep.startLifetime = Random.Range(0.25f, 0.5f);
            core.Emit(ep, 1);
        }

        // safety: never let a held nova linger forever
        if (chargeT >= maxCharge + 0.5f) Detonate(maxCharge);
    }

    // Trail the cursor in world space, clamped to the fixed cast origin (not the
    // player). SmoothDamp eases velocity in and out (no snap from the get-go), and
    // the smooth-time grows along a SmoothStep curve so it gently locks in as it
    // charges — a smooth deceleration rather than an abrupt slowdown.
    void FollowMouse()
    {
        if (IM.i == null) return;
        Vector3 target = ResolveCursor(castOrigin);
        target.z = transform.position.z;
        // higher levels start quicker (smaller smooth-time early), still easing toward
        // ~0 motion (locked) by full charge: lvl1/2/3 begin at 0.50/0.34/0.18.
        float startSt = 0.5f - 0.16f * (level - 1);
        float st = Mathf.Lerp(startSt, 3.2f, Mathf.SmoothStep(0f, 1f, charge01));
        transform.position = Vector3.SmoothDamp(transform.position, target, ref followVel, st, Mathf.Infinity, Time.deltaTime);
    }

    // World cursor clamped to maxRange around `anchor`. Controller-safe: falls back
    // to the aim stick's direction (MousePosition reads the mouse device otherwise).
    Vector3 ResolveCursor(Vector3 anchor)
    {
        if (IM.controller)
        {
            Vector2 aim = IM.i.MousePosition(anchor, true); // normalized aim direction
            if (aim.sqrMagnitude < 0.0001f) aim = Vector2.up;
            return anchor + (Vector3)(aim.normalized * maxRange);
        }
        Vector3 cursor = (Vector3)IM.i.MousePosition();      // true world cursor
        Vector3 off = cursor - anchor;
        if (off.magnitude > maxRange) off = off.normalized * maxRange;
        return anchor + off;
    }

    IEnumerator DetonateRoutine(float charge)
    {
        float power = 0.55f + 0.65f * charge;        // 0.55 (tap) .. 1.2 (full)
        float r = radius * (blastRadiusFloor + blastRadiusGain * charge); // blast radius == visual spread == hitbox; small early → big full

        // Per-cast stochasticity: how "nova-y" (pinwheel-like) this blast reads, and
        // which way it spins. Short channels stay sparse/sprayy; long ones bloom into a
        // full pinwheel — and it's jittered so no two casts look identical.
        float novaness = Mathf.Clamp01(charge * Random.Range(0.78f, 1.12f));
        float spin = Random.value < 0.5f ? 1f : -1f;

        // Earlier release → quick, snappy implosion+blast; a long channel earns a slow,
        // weighty collapse before it lets go. (Scales UP with charge; the ceiling grows
        // with level — 1.25 / 1.75 / 2.25s at lvl 1/2/3 by default.)
        float implosionTimeMax = implosionTimeMaxBase + implosionTimeMaxPerLevel * (level - 1);
        float implosionTime = Mathf.Lerp(implosionTimeMin, implosionTimeMax, charge);
        yield return StartCoroutine(Implode(implosionTime, charge));

        ApplyBrightness(Mathf.Lerp(detonationFlashMin, peakBrightness, charge)); // detonation flash scales with charge
        FireBurst(charge, power, r, novaness, spin);
        FireEmbers(charge, r);
        FireFlash(power, r);

        // hitbox/shockwave are 25% smaller than the visual spread (the VFX `r` stays full)
        float hitR = r * 0.75f;

        // screen-space shockwave ring — radius matches the (reduced) damage circle
        Shockwave.Spawn(transform.position, hitR, 0.05f + 0.03f * charge, 0.9f, 0.5f);

        ApplyDamage(hitR, charge);

        if (gather != null) gather.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        yield return new WaitForSeconds(teardownDelay);
        Destroy(gameObject);
    }

    // Collapse before the blast. The already-gathered cloud (still on screen, no longer
    // replenished after release) falls in on its own. The CORE knot — the blob that grew
    // while charging — is compressed inward to the singularity with an ease-in-sine squeeze
    // and vibrates with rising frequency. Duration scales UP with charge.
    IEnumerator Implode(float t, float charge)
    {
        if (t <= 0.06f) yield break;

        float brightChance = Mathf.Lerp(brightShareStart, brightShareEnd, charge);

        float r0 = radius * Mathf.Lerp(coreRadiusStart, coreRadiusEnd, charge);

        // The core grains outlive the implosion a little so they DON'T vanish in a flash frame at
        // the blast: the implosion is the first `CoreHoldEnd` of their life (full alpha), then they
        // fade out + finish their animation over the short remaining tail (see MakeCoreSystem).
        const float CoreHoldEnd = 0.8f;
        float life = t / CoreHoldEnd;

        // Strictly-inward, ease-OUT compression that lands EXACTLY on the singularity size.
        // Velocity is a precise LINEAR ramp 1→0 over [0,fNorm] (a triangle, exact area 0.5·fNorm)
        // so the integral — hence the distance travelled — is exact; it never dips below 0 (no
        // outward spring). The grains start on a shell whose thickness equals the singularity, so
        // they all land INSIDE the singularity and none overshoots past the centre (no bounce).
        float f = Mathf.Clamp(coreCompressFraction, 0.05f, 1f);
        float fNorm = f * CoreHoldEnd;                              // compression completes within the implosion
        float targetR = radius * Mathf.Max(singularitySize, 0.003f); // implode all the way down to the singularity size
        float travel = Mathf.Max(0f, r0 - targetR);
        float slope = -1f / fNorm;
        var velCurve = new AnimationCurve(
            new Keyframe(0f, 1f, 0f, slope),
            new Keyframe(fNorm, 0f, slope, 0f),
            new Keyframe(1f, 0f, 0f, 0f));                          // exactly linear 1→0, then flat 0
        float area = 0.5f * fNorm;                                 // exact ∫ of the triangle
        float shellJit = Mathf.Clamp01(1f - 0.85f * targetR / Mathf.Max(r0, 0.001f)); // shell ≈ singularity-thin
        var cvol = core.velocityOverLifetime;
        cvol.radial = new ParticleSystem.MinMaxCurve(-(travel / (life * Mathf.Max(area, 0.0001f))), velCurve);

        // play the JLVisual frames in REVERSE during the implosion / vibration
        var ctsa = core.textureSheetAnimation;
        ctsa.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

        // ...and it vibrates: FREQUENCY climbs over the collapse (building energy), AMPLITUDE stays
        // tiny (absolute world-units, never scatters), and the jitter also ROTATES the grains.
        var noise = core.noise;
        noise.enabled = true;
        noise.damping = false;
        noise.strength = implosionShakeStrength;     // small, absolute world-units (keep it low)
        noise.scrollSpeed = 1.5f;
        noise.frequency = implosionShakeFreqMax * 0.12f;
        noise.rotationAmount = 3f;                   // the vibration spins the grains too

        int count = Mathf.RoundToInt(implosionGrainsPerLevel * countMul * (0.4f + 0.7f * charge));
        var ep = new ParticleSystem.EmitParams();
        for (int i = 0; i < count; i++)
        {
            float ang = Random.value * TAU;
            Vector3 dir = new(Mathf.Cos(ang), Mathf.Sin(ang), 0f);
            ep.position = transform.position + dir * (r0 * Random.Range(shellJit, 1f)); // singularity-thin shell → lands on the singularity, no overshoot
            ep.velocity = Vector3.zero;                  // radial module does the ease-out compression
            ep.startColor = GrainShade(brightChance);
            ep.startSize = Random.Range(0.1f, 0.2f);
            ep.startLifetime = life;                      // persist past the blast, then fade in the tail
            core.Emit(ep, 1);
        }

        float startB = brightness;
        float endB = Mathf.Lerp(detonationFlashMin, peakBrightness, charge);
        float t0 = 0f;
        while (t0 < t)
        {
            t0 += Time.deltaTime;
            float nrm = Mathf.Clamp01(t0 / t);
            noise.frequency = Mathf.Lerp(implosionShakeFreqMax * 0.12f, implosionShakeFreqMax, nrm); // speeds up as it collapses
            ApplyBrightness(Mathf.Lerp(startB, endB, nrm));
            yield return null;
        }

        // held singularity: a beat of stillness before it blows
        float hold = Mathf.Lerp(0.08f, 0.2f, charge);
        ApplyBrightness(endB);
        yield return new WaitForSeconds(hold);
    }

    // ---- detonation pieces --------------------------------------------

    // The blast reaches out to the blast radius (visible spread == hitbox); its arm
    // count is a clean charge signal (armsMin → armsMax), while texture (per-arm
    // density, spiral tightness, off-arm spray) scales with `novaness` and `spin`
    // randomises the swirl direction so no two casts read the same.
    void FireBurst(float charge, float power, float r, float novaness, float spin)
    {
        if (burst == null) return;

        // swirl scales with charge (almost none for a quick tap → full pinwheel)
        var bvol = burst.velocityOverLifetime;
        bvol.orbitalZ = new ParticleSystem.MinMaxCurve(spin * Mathf.Lerp(0.15f, 1.3f, charge));

        // arm count is a clean charge signal: armsMin spokes on a quick release →
        // armsMax on a full channel. (Stochasticity lives in spin/spray/sizes.)
        int arms = Mathf.RoundToInt(Mathf.Lerp(armsMin, armsMax, charge));
        int perArm = Mathf.RoundToInt(Mathf.Lerp(20f, 46f, novaness) * countMul); // more grains per arm at higher level
        float baseAng = Random.value * TAU;
        float spiral = spin * Mathf.Lerp(0.12f, 0.9f + 0.2f * level, novaness); // sweep packed per arm
        float spray = (1f - novaness) * 0.4f;          // low charge → grains scatter off the arms
        float brightChance = Mathf.Lerp(brightShareStart, brightShareEnd, charge); // bright/deep mix by charge
        var ep = new ParticleSystem.EmitParams();
        for (int a = 0; a < arms; a++)
        {
            for (int k = 0; k < perArm; k++)
            {
                float t = k / (float)perArm;
                float jitter = Random.Range(-0.05f, 0.05f) + Random.Range(-spray, spray);
                float ang = baseAng + a * (TAU / arms) + t * spiral + jitter;
                Vector3 dir = new(Mathf.Cos(ang), Mathf.Sin(ang), 0f);
                float life = Random.Range(0.8f, 1.4f) * (0.85f + 0.45f * charge);
                float dist = r * (0.12f + 0.88f * t) * Random.Range(0.8f, 1.08f); // inner → rim
                float spd = dist / life;               // no velocity-limit on this system → reaches ~dist
                ep.position = transform.position + dir * Random.Range(0.0f, 0.12f);
                ep.velocity = dir * spd;
                ep.startColor = GrainShade(brightChance);
                ep.startSize = Random.Range(0.1f, 0.26f) * (0.85f + 0.4f * power);
                ep.startLifetime = life;
                burst.Emit(ep, 1);
            }
        }
    }

    void FireEmbers(float charge, float r)
    {
        if (ember == null) return;
        int count = Mathf.RoundToInt((30 + 30 * charge) * countMul);
        var ep = new ParticleSystem.EmitParams();
        for (int i = 0; i < count; i++)
        {
            float ang = Random.value * TAU;
            Vector3 dir = new(Mathf.Cos(ang), Mathf.Sin(ang), 0f);
            float life = Random.Range(1.4f, 2.4f);
            float dist = r * Random.Range(0.2f, 1.0f);
            float spd = dist / life;
            ep.position = transform.position + dir * Random.Range(0.0f, r * 0.2f);
            ep.velocity = dir * spd + (Vector3)(Random.insideUnitCircle * 0.25f);
            ep.startColor = deepShade;                 // deeper shade of the same ramp
            ep.startSize = Random.Range(0.05f, 0.13f);
            ep.startLifetime = life;
            ember.Emit(ep, 1);
        }
    }

    void FireFlash(float power, float r)
    {
        // a few small bright grains for the initial central pop
        if (burst == null) return;
        var ep = new ParticleSystem.EmitParams();
        const int n = 10;
        for (int i = 0; i < n; i++)
        {
            ep.position = transform.position + (Vector3)(Random.insideUnitCircle * (r * 0.08f));
            ep.velocity = (Vector3)(Random.insideUnitCircle * (r * 0.25f));
            ep.startColor = brightShade;
            ep.startSize = Random.Range(0.18f, 0.4f) * (0.8f + 0.5f * power);
            ep.startLifetime = Random.Range(0.35f, 0.6f);
            burst.Emit(ep, 1);
        }
    }

    // Centre-weighted damage: decent in the middle, a tiny bit out to the rim, plus a
    // stun on everything caught in the blast.
    void ApplyDamage(float r, float charge)
    {
        // pass the big buffer so a crowded blast hits ALL enemies, not an arbitrary 10
        var foes = GS.FindEnemies(casterTag, transform.position, r, false, false, s_overlap);
        if (foes == null) return;
        foreach (Transform f in foes)
        {
            if (f == null) continue;
            float dist = (f.position - transform.position).magnitude;
            float r0 = Mathf.Max(0.01f, r);
            // full-power grace zone (fraction of radius) per level: 60% / 47.25% / 35%,
            // then fall off to the rim
            float innerFrac = new[] { 0.60f, 0.4725f, 0.35f }[Mathf.Clamp(level, 1, 3) - 1];
            float falloff = dist <= innerFrac * r0
                ? 1f
                : Mathf.Clamp01((r0 - dist) / (r0 * (1f - innerFrac)));
            float curve = falloff * falloff;           // steep — concentrates damage toward the centre
            // charge curve: insta-release = 60% of full, scaling up to 100% over the charge
            float chargeMul = Mathf.Lerp(0.6f, 1f, charge);
            if (f.TryGetComponent<LifeScript>(out var ls) && !ls.hasDied)
            {
                // centre→rim falloff (rim = rimDamageFraction of centre) × charge curve × intellect.
                float dmg = damage * Mathf.Lerp(rimDamageFraction, 1f, curve) * chargeMul * (1f + 0.1f * atr);
                ls.Change(-dmg, 0);
            }
            if (f.TryGetComponent<ActionScript>(out var asc))
            {
                Vector2 dir = ((Vector2)(f.position - transform.position)).normalized;
                if (dir == Vector2.zero) dir = Random.insideUnitCircle.normalized;
                asc.AddPush(0.12f, true, dir * asc.mass * knockbackForce * curve * (0.6f + 0.5f * charge));
            }
            // EVERY enemy caught in the blast is stunned (no core-zone gate); the
            // duration scales linearly with distance — full at the centre, down to
            // stunRimFraction at the rim — and grows with charge.
            // stun: insta-release = 60% of the full-charge stun, scaling up to 100%.
            if (f.TryGetComponent<Unit>(out var u))
                GS.Stat(u, "stun", (stunBase + stunPerCharge) * chargeMul * Mathf.Pow(2f, level - 1) * Mathf.Lerp(stunRimFraction, 1f, falloff));
        }
    }

    // Per-tick channel damage to whatever is standing in the gathering vortex while
    // the spell is held. Small inner radius, grows 50% → 100% with charge.
    void ChannelTick()
    {
        // the damage zone IS the growing core knot, so it grows with the charge over time
        float tickR = radius * Mathf.Lerp(coreRadiusStart, coreRadiusEnd, charge01);
        var foes = GS.FindEnemies(casterTag, transform.position, tickR, false, false, s_overlap);
        if (foes == null) return;
        float grow = Mathf.Lerp(channelChargeFloor, 1f, charge01); // DPS ramps 50% → 100% with charge
        float tickDmg = channelDps * grow * channelDotInterval * (1f + 0.1f * atr); // channelDps = DPS at full charge
        // lvl2/3: the vortex also slows what it chews — 25% (×0.75) / 50% (×0.5). Refreshed
        // every tick (duration a touch over the interval) so it persists while held.
        float slowKeep = level >= 3 ? 0.5f : level >= 2 ? 0.75f : 1f;
        foreach (Transform f in foes)
        {
            if (f == null) continue;
            if (f.TryGetComponent<LifeScript>(out var ls) && !ls.hasDied) ls.Change(-tickDmg, 0);
            if (slowKeep < 1f && f.TryGetComponent<Unit>(out var u))
                GS.Stat(u, "slow", channelDotInterval * 1.5f, slowKeep);
        }
    }

    // Per-grain shade: a MIX of the two (greyscale) shades. `brightChance` is the odds of
    // the bright shade — shifting this proportion (rather than tinting every grain the
    // same colour over time) is what makes the energy read as "heating up". Prettier.
    Color GrainShade(float brightChance) => Random.value < brightChance ? brightShade : deepShade;

    static readonly int ColorID = Shader.PropertyToID("_Color");

    // Tint every material copy by a single brightness scalar — only if the material has a
    // "_Color" property. (The unlit-red glow shader carries the red via its own `thecolor`,
    // so this is a no-op there; the guard avoids per-frame warnings on shaders without _Color.)
    void ApplyBrightness(float b)
    {
        brightness = b;
        Color c = new(b, b, b, 1f);
        for (int i = 0; i < mats.Count; i++)
            if (mats[i] != null && mats[i].HasProperty(ColorID)) mats[i].SetColor(ColorID, c);
    }

    // ---- construction --------------------------------------------------

    ParticleSystem MakeSystem(string name, int order)
    {
        var g = new GameObject(name);
        g.transform.SetParent(transform, false);
        var ps = g.AddComponent<ParticleSystem>();
        // AddComponent auto-plays it (default playOnAwake); fully stop before we
        // touch main.duration/simulationSpace, which can't be set while playing.
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var psr = g.GetComponent<ParticleSystemRenderer>();

        var frames = LoadFrames();
        var m = new Material(baseMat);                       // unlit-red glow material (its `thecolor` = the red)
        if (frames.Length > 0) m.mainTexture = frames[0].texture;
        mats.Add(m);
        psr.sharedMaterial = m;
        psr.renderMode = ParticleSystemRenderMode.Billboard;
        psr.sortingLayerName = SortingLayer;
        psr.sortingOrder = order;

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = true;                  // keep simulating; emission is manual
        main.duration = 8f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 6000;
        main.startSpeed = 0f;
        main.gravityModifier = 0f;

        var em = ps.emission; em.enabled = false;
        var sh = ps.shape; sh.enabled = false;

        // JLVisual sprite sheet animated across each particle's lifetime (frame 0 → last)
        if (frames.Length > 0)
        {
            var tsa = ps.textureSheetAnimation;
            tsa.enabled = true;
            tsa.mode = ParticleSystemAnimationMode.Sprites;
            for (int i = 0; i < frames.Length; i++) tsa.AddSprite(frames[i]);
            tsa.animation = ParticleSystemAnimationType.WholeSheet;
            tsa.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
            tsa.cycleCount = 1;
        }
        return ps;
    }

    ParticleSystem MakeGatherSystem()
    {
        var ps = MakeSystem("Gather", 48);
        var vol = ps.velocityOverLifetime;
        vol.enabled = true;
        vol.space = ParticleSystemSimulationSpace.World;
        vol.orbitalZ = new ParticleSystem.MinMaxCurve(2.4f);        // swirl
        vol.radial = new ParticleSystem.MinMaxCurve(-radius * 1.3f); // pull inward
        SetColorOverLifetime(ps, 0.15f, 0.85f);
        SetSizeOverLifetime(ps, new AnimationCurve(
            new Keyframe(0f, 0.2f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0f)));
        ps.Play();
        return ps;
    }

    ParticleSystem MakeBurstSystem()
    {
        var ps = MakeSystem("Burst", 52);
        var vol = ps.velocityOverLifetime;
        vol.enabled = true;
        vol.space = ParticleSystemSimulationSpace.World;
        vol.orbitalZ = new ParticleSystem.MinMaxCurve(1.3f);        // curve rays into spiral arms
        // no velocity limit: grains travel velocity·lifetime, so reach is exactly the blast radius
        SetColorOverLifetime(ps, 0.04f, 0.6f);
        SetSizeOverLifetime(ps, new AnimationCurve(
            new Keyframe(0f, 0.35f), new Keyframe(0.12f, 1f), new Keyframe(1f, 0.15f)));
        ps.Play();
        return ps;
    }

    ParticleSystem MakeEmberSystem()
    {
        var ps = MakeSystem("Ember", 50);
        var lim = ps.limitVelocityOverLifetime;
        lim.enabled = true; lim.dampen = 0.18f; lim.limit = new ParticleSystem.MinMaxCurve(2f);
        SetColorOverLifetime(ps, 0.1f, 0.45f);
        SetSizeOverLifetime(ps, new AnimationCurve(
            new Keyframe(0f, 1f), new Keyframe(0.6f, 0.8f), new Keyframe(1f, 0f)));
        ps.Play();
        return ps;
    }

    // The core knot — a particle system like the others. While charging it's a growing
    // blob (radial 0, emitted in Update); on implosion its velocityOverLifetime.radial
    // (the ease-in-sine squeeze) and noise (the vibration) are configured per-cast.
    ParticleSystem MakeCoreSystem()
    {
        var ps = MakeSystem("Core", 53);
        var vol = ps.velocityOverLifetime;
        vol.enabled = true;
        vol.space = ParticleSystemSimulationSpace.World; // radial toward the system centre
        SetColorOverLifetime(ps, 0.05f, 0.8f);           // full alpha through the implosion (≈first 80%), then fade out the tail
        SetSizeOverLifetime(ps, new AnimationCurve(
            new Keyframe(0f, 0.5f), new Keyframe(0.15f, 1f), new Keyframe(0.8f, 1f), new Keyframe(1f, 0f)));
        ps.Play();
        return ps;
    }

    void SetColorOverLifetime(ParticleSystem ps, float riseEnd, float holdEnd)
    {
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, riseEnd),
                new GradientAlphaKey(1f, holdEnd),
                new GradientAlphaKey(0f, 1f)
            });
        col.color = new ParticleSystem.MinMaxGradient(g);
    }

    void SetSizeOverLifetime(ParticleSystem ps, AnimationCurve curve)
    {
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, curve);
    }

    // Load + cache the JLVisual sprite-sheet frames (JLVisual_0..n) in order.
    static Sprite[] LoadFrames()
    {
        if (s_frames != null) return s_frames;
        var all = Resources.LoadAll<Sprite>("JLVisual");
        var list = new List<Sprite>();
        if (all != null)
            foreach (var s in all)
                if (s != null && s.name.StartsWith("JLVisual_")) list.Add(s);
        list.Sort((a, b) => FrameIndex(a.name) - FrameIndex(b.name));
        s_frames = list.ToArray();
        return s_frames;
    }

    static int FrameIndex(string n)
    {
        int u = n.LastIndexOf('_');
        return (u >= 0 && int.TryParse(n.Substring(u + 1), out var v)) ? v : 0;
    }

}
