using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// The deployed Firestorm effect. It lives on its own prefab (Prefabs/AbilityProj/Firestorm)
// so every feel value is authored in the inspector. FirestormSpell instantiates it and calls
// Begin(); the player CHANNELS to whip a swirling storm of fire up around themselves, then
// RELEASES:
//   - a quick tap  -> a long (~4s), MILD firestorm aura that trails the player + a mild,
//                     long stim + chip damage to anything it sweeps over.
//   - a full hold  -> a short (~1.5s) but FEROCIOUS aura (up to 50% wider) + a huge stim +
//                     heavy damage.
// Channelling ROOTS the player (only once past `rootThreshold` ~0.35s, so a tap never roots);
// the root holds exactly while held and lifts the instant you release — and it ONLY lifts our
// own root, never touching any other CC. Build is all-code, mirroring NovaCore: the particle
// systems run on the unlit-RED glow material with the SignatureFX sprite-sheet animated over
// each grain's lifetime; per-grain colours are GREYSCALE and overall HEAT is driven by scaling
// the shader's HDR `thecolor` (genuine bloom) — "brightness like Nova", on the right property.
// A NoiseModule churns every system so the ring reads as a true firestorm, not a tidy halo.
// ONE element only (red). See nova-ability-vfx / embers-edge-palette.
public class FirestormCore : MonoBehaviour
{
    const float TAU = 6.28318530718f;
    const string SortingLayer = "Power Ups"; // top gameplay layer; same one Shockwave uses

    // ---- authored feel (set on the Firestorm prefab) --------------------

    [Header("Particle shade (GREYSCALE — the unlit-red shader tints it red)")]
    [Tooltip("Bright greyscale shade (→ bright red through the shader).")]
    [SerializeField] Color brightShade = new(1f, 1f, 1f);
    [Tooltip("Deep greyscale shade (→ deep red). Used for the embers.")]
    [SerializeField] Color deepShade = new(0.5f, 0.5f, 0.5f);
    [Tooltip("Bright-grain share while the storm is still gathering (cold).")]
    [SerializeField] float brightShareStart = 0.12f;
    [Tooltip("Bright-grain share at full charge (white-hot).")]
    [SerializeField] float brightShareEnd = 0.85f;

    [Header("Heat (scales the glow material's HDR `thecolor` → bloom)")]
    [Tooltip("Multiplier on the base red while idle / barely charged.")]
    [SerializeField] float idleBrightness = 0.8f;
    [Tooltip("Multiplier at full charge (white-hot firestorm).")]
    [SerializeField] float peakBrightness = 1.6f;
    [Tooltip("Flash multiplier the instant it ignites on release.")]
    [SerializeField] float igniteFlash = 2.1f;

    [Header("Charge / channel")]
    [Tooltip("Seconds of hold for a full charge.")]
    [SerializeField] float maxCharge = 2.5f;
    [Tooltip("Hold longer than this (seconds) and the player is ROOTED while channelling (a tap never roots).")]
    [SerializeField] float rootThreshold = 0.35f;

    [Header("Radius (world units — these ARE the AoE size, before level scaling)")]
    [Tooltip("Channel storm radius at the start of the charge (a quick tap).")]
    [SerializeField] float startRadius = 0.6f;
    [Tooltip("Channel storm radius at a full charge.")]
    [SerializeField] float maxRadius = 1.2f;
    [Tooltip("On release the AoE — and its hitbox — = the channel radius at that moment × this. 1.25 = a pop slightly bigger than the storm you were holding.")]
    [SerializeField] float explosionMultiplier = 1.25f;
    [Tooltip("Thickness of the swirling fire ring, as a fraction of its radius.")]
    [SerializeField] float ringThickness = 0.1f;

    [Header("Aura lifetime (trails the player)")]
    [Tooltip("Aura seconds on a quick tap.")]
    [SerializeField] float auraDurTap = 4f;
    [Tooltip("Aura seconds on a full hold (short but fierce); +0.5s/level → 1 / 1.5 / 2 at full charge.")]
    [SerializeField] float auraDurFull = 1f;

    [Header("Stim granted to the caster (value = act-rate multiplier)")]
    [Tooltip("Stim seconds on a tap.")]
    [SerializeField] float stimDurTap = 4f;
    [Tooltip("Stim seconds on a full hold.")]
    [SerializeField] float stimDurFull = 1.5f;

    [Header("Damage (ring = a DamageBoundary: DPS while inside + a one-time DoT that lasts the aura's duration)")]
    [Tooltip("Outward shove on enemies the instant it ignites (× mass × charge × falloff).")]
    [SerializeField] float igniteKnockback = 6f;

    [Header("Storm motion")]
    [Tooltip("Orbital swirl speed of the fire ring (rad/s-ish). Spin direction is randomised per cast.")]
    [SerializeField] float stormSpin = 2.2f;
    [Tooltip("Turbulence strength — the churn that reads as a firestorm rather than a halo.")]
    [SerializeField] float noiseStrength = 0.55f;
    [Tooltip("Turbulence frequency.")]
    [SerializeField] float noiseFrequency = 0.7f;

    [Header("Lifecycle")]
    [Tooltip("Seconds the embers linger after the aura ends before despawn.")]
    [SerializeField] float teardownDelay = 1.6f;

    // ---- runtime state (set by Begin / scaled per cast) -----------------

    Material baseMat;
    int level = 1;
    float atr;
    string casterTag = "Allies";
    float tapDps, fullDps;    // damage-per-second under the aura (tap → full charge)
    float tapDot, fullDot;    // one-time damage-over-time dealt to enemies the ring catches
    float tapStim, fullStim;  // stim magnitude (act-rate multiplier), tap → full charge
    DamageBoundary boundary;  // the ring's hitbox while the aura burns

    float chargeT, charge01;
    bool channeling, released;
    bool selfRooted;          // did WE root the player? (so we only lift our own root)
    float spinDir = 1f;       // ± per cast so no two storms swirl the same way
    float brightness = 1f;
    float sizeScale = 1f;     // 1 / 1.5 / 2.25 at level 1/2/3 — scales the RADII (grain size stays fixed)
    float countMul = 1f;      // particle-count multiplier; grows with level so a bigger storm stays full
    float sizeCharge;         // current charge (0..1) — only the MAX of the grain-size range scales with it
    Color baseHot = new(2.996f, 0.031f, 0.047f, 1f); // captured from the material's HDR `thecolor`

    readonly List<Material> mats = new();
    ParticleSystem ring, inner, embers, burst;

    static Sprite[] s_frames;                        // SignatureFX sprite-sheet frames, ordered 0..n
    // Big overlap buffer so the aura NEVER caps the enemies it hits (GS.FindEnemies' default is 10
    // and isn't distance-sorted). See findenemies-buffer-cap.
    static readonly Collider2D[] s_overlap = new Collider2D[256];

    static readonly int ColorID = Shader.PropertyToID("_Color");
    static readonly int HotID = Shader.PropertyToID("thecolor");

    // ease-in-cubic on the charge fraction (t = held/max) — every tap→full interpolation runs
    // through this, so a quick tap gives near the floor and the payoff is back-loaded toward a
    // full channel.
    static float Ec(float t) => t * t;

    // Flat per-level perk durations (seconds), indexed [level-1].
    static readonly float[] IMMAT_DUR   = { 1f, 3.5f, 4f };    // immaterial — L1 from RELEASE, L2/L3 from charge START
    static readonly float[] REFLECT_DUR = { 0f, 3.5f, 4.5f };  // reflect — from charge START (L1 = none)

    // ---- public API (called by FirestormSpell) --------------------------

    public void Begin(Material particleMat, int lvl, float intellect, string tag,
                      float dpsTap, float dpsFull, float dotTap, float dotFull, float stimTap, float stimFull)
    {
        baseMat = particleMat != null ? particleMat : Resources.Load<Material>("Sprite-Unlit-Default");
        if (baseMat != null && baseMat.HasProperty(HotID)) baseHot = baseMat.GetColor(HotID);
        level = Mathf.Max(1, lvl);
        atr = intellect;
        casterTag = string.IsNullOrEmpty(tag) ? "Allies" : tag;
        tapDps = dpsTap; fullDps = dpsFull;
        tapDot = dotTap; fullDot = dotFull;
        tapStim = stimTap; fullStim = stimFull;
        sizeScale = 1f + 0.5f * (level - 1);   // radii grow 50%/level — grain SIZE stays fixed
        countMul = sizeScale * sizeScale;      // counts grow with AREA so the fixed-size grains keep the bigger storm full
        spinDir = Random.value < 0.5f ? 1f : -1f;

        transform.position = PlayerPos();

        ring   = MakeRingSystem();
        inner  = MakeInnerSystem();
        embers = MakeEmberSystem();
        burst  = MakeBurstSystem();
        ApplyBrightness(idleBrightness);

        // perks that kick in the moment you start charging (flat per-level durations):
        if (CharacterScript.CS != null && level >= 2)
        {
            int li = Mathf.Clamp(level, 1, 3) - 1;
            GS.Stat(CharacterScript.CS, "immaterial", IMMAT_DUR[li]);        // L2 3.5s / L3 4s
            if (REFLECT_DUR[li] > 0f)
                GS.Stat(CharacterScript.CS, "reflect", REFLECT_DUR[li], 1f); // L2 3.5s / L3 4.5s (value2≠0 ⇒ TIMED)
        }

        channeling = true;
        chargeT = 0f;
    }

    public void Release(float heldSeconds)
    {
        if (released) return;
        released = true;
        channeling = false;
        ReleaseRoot();
        StartCoroutine(IgniteAndBurn(Mathf.Clamp01(heldSeconds / maxCharge)));
    }

    // ---- channel --------------------------------------------------------

    void Update()
    {
        if (!channeling) return;

        chargeT += Time.deltaTime;
        charge01 = Mathf.Clamp01(chargeT / maxCharge);
        sizeCharge = charge01;              // grains grow their max a touch as you charge

        transform.position = PlayerPos();   // the storm gathers on you and follows

        // plant the feet: a real ROOT *status* once past the grip threshold — it shows the cc
        // icon, calls AS.Stop() (kills any momentum you had, not just future input) and sets
        // rooted. Applied once; lifted surgically on release.
        if (chargeT >= rootThreshold && !selfRooted && CharacterScript.CS != null)
        {
            GS.Stat(CharacterScript.CS, "root", maxCharge, 0f);
            selfRooted = true;
        }

        // heat climbs with charge
        ApplyBrightness(Mathf.Lerp(idleBrightness, peakBrightness, charge01));

        float brightChance = Mathf.Lerp(brightShareStart, brightShareEnd, charge01);
        float ecc = Ec(charge01);
        float R = Mathf.Lerp(startRadius, maxRadius, ecc) * sizeScale;
        // particle COUNT ramps hard with the charge (≈10×) so a bigger, hotter storm visibly
        // carries more DPS — density reads as damage.
        float dpsBoost = 0.25f + 2.25f * ecc;
        EmitRing(ring, R, ringThickness,
                 Mathf.CeilToInt(200f * dpsBoost * countMul * Time.deltaTime), brightChance, 0.5f, 1.0f);
        EmitRing(inner, R * 0.55f, ringThickness * 1.3f,
                 Mathf.CeilToInt(110f * dpsBoost * countMul * Time.deltaTime), Mathf.Min(1f, brightChance + 0.15f), 0.4f, 0.8f);
        if (charge01 > 0.1f)
            EmitEmbers(R, Mathf.CeilToInt(55f * dpsBoost * countMul * Time.deltaTime));

        // safety: never let a held firestorm linger forever
        if (chargeT >= maxCharge + 0.5f) Release(maxCharge);
    }

    Vector3 PlayerPos()
    {
        if (CharacterScript.CS == null) return transform.position;
        Vector3 p = CharacterScript.CS.transform.position;
        p.z = transform.position.z;
        return p;
    }

    // Lift ONLY our root status — RemStat targets the "root" type alone, so every other CC /
    // status the player is carrying is left untouched.
    void ReleaseRoot()
    {
        if (!selfRooted) return;
        selfRooted = false;
        if (CharacterScript.CS != null) GS.RemStat(CharacterScript.CS, "root");
    }

    // ---- release: ignite, then burn while trailing the player -----------

    IEnumerator IgniteAndBurn(float charge)
    {
        // every tap→full interpolation runs through ease-in-cubic (t = held/maxCharge)
        float ec       = Ec(charge);
        float releaseR = Mathf.Lerp(startRadius, maxRadius, ec) * sizeScale; // the storm size at the instant of release
        float auraR    = releaseR * explosionMultiplier;                  // the AoE — visual spread == hitbox
        float durLvl   = 0.5f * (level - 1);                              // duration grows 0.5s/level at both ends
        float auraDur  = Mathf.Lerp(auraDurTap + durLvl, auraDurFull + durLvl, ec); // 4→1 / 4.5→1.5 / 5→2 by level
        sizeCharge = charge;                                              // the aura keeps the release charge's grain size
        float stimDur = Mathf.Lerp(stimDurTap, stimDurFull, ec);
        float stimMag = Mathf.Lerp(tapStim, fullStim, ec);
        float dps     = Mathf.Lerp(tapDps, fullDps, ec);
        float dot     = Mathf.Lerp(tapDot, fullDot, ec);

        // the player's payoff: a stim for the effect duration (all levels). Level 1 also turns
        // immaterial here on release; level 2+ is already immaterial from the channel (set once).
        if (CharacterScript.CS != null)
        {
            GS.Stat(CharacterScript.CS, "stim", stimDur, stimMag);
            if (level < 2)
                GS.Stat(CharacterScript.CS, "immaterial", IMMAT_DUR[0]); // L1: flat 1s from release
        }

        // ignition: a punchy flash + a spray of sparks + outward shove
        transform.position = PlayerPos();
        ApplyBrightness(igniteFlash + 0.8f);                               // a brighter pop than the channel peak
        IgniteBurst(auraR, charge);
        EmitEmbers(auraR, Mathf.RoundToInt((45f + 90f * charge) * countMul)); // sparks thrown on release
        IgniteShove(auraR, charge);

        // the ring's damage IS a DamageBoundary that trails the player: DPS while inside + a
        // single damage-over-time burn the moment an enemy is caught.
        boundary = SpawnBoundary(auraR, dps, dot, auraDur);

        // a denser aura when the release packs more DPS (count represents damage)
        float auraDps = 0.4f + 1.8f * ec;
        float brightChance = Mathf.Lerp(brightShareStart + 0.25f, brightShareEnd, ec);
        float t = 0f;
        while (t < auraDur)
        {
            t += Time.deltaTime;
            float a = Mathf.Clamp01(t / auraDur);
            transform.position = PlayerPos();   // the aura (and its hitbox child) trails you

            // heat eases from the ignite flash down to a dying glow as it burns out (a quick
            // bright flash on the first beat, then settle)
            ApplyBrightness(Mathf.Lerp(igniteFlash + 0.8f, idleBrightness * 0.6f, Mathf.SmoothStep(0f, 1f, a)));

            float density = (1f - 0.45f * a) * auraDps; // thins as it dies, scaled by release DPS
            EmitRing(ring, auraR, ringThickness,
                     Mathf.CeilToInt(200f * density * countMul * Time.deltaTime), brightChance, 0.45f, 0.9f);
            EmitRing(inner, auraR * 0.5f, ringThickness * 1.3f,
                     Mathf.CeilToInt(110f * density * countMul * Time.deltaTime), Mathf.Min(1f, brightChance + 0.1f), 0.35f, 0.7f);
            EmitEmbers(auraR, Mathf.CeilToInt(35f * density * countMul * Time.deltaTime));

            yield return null;
        }

        // the aura is over — stop the hitbox before the embers finish fading
        if (boundary != null) Destroy(boundary.gameObject);

        // burn out: stop replenishing, let the last grains finish, then despawn
        if (ring != null)   ring.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        if (inner != null)  inner.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        if (embers != null) embers.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        yield return new WaitForSeconds(teardownDelay);
        Destroy(gameObject);
    }

    // The ring's hitbox: a DamageBoundary on a trigger circle that rides the player (child of
    // this core, which trails the player each frame). It deals `dps` per second to anything
    // inside (intellect-scaled) and a single `dot` damage-over-time burn the instant an enemy
    // is caught — refreshCollideTimer outlasts the aura so the burn lands ONCE. Mirrors the
    // Ripple setup: trigger collider, no rigidbody, on the caster's projectile layer.
    DamageBoundary SpawnBoundary(float r, float dps, float dot, float life)
    {
        var g = new GameObject("FirestormHitbox");
        g.transform.SetParent(transform, false);
        g.tag = casterTag;
        int layer = LayerMask.NameToLayer(casterTag == "Allies" ? "Ally Projectiles" : "Enemy Projectiles");
        if (layer >= 0) g.layer = layer;

        var col = g.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = r;

        float intel = 1f + 0.1f * atr;
        var db = g.AddComponent<DamageBoundary>();
        db.damage = 0f;                       // no separate one-shot hit — just DPS + the DoT
        db.damageType = 0;
        db.dps = dps * intel;
        db.damageOverT = dot * intel;
        db.dotKills = true;                   // the firestorm's burn CAN finish enemies off
        db.damageOverTtime = life;            // the DoT lasts the aura's own duration (level-scaled, 4→1.5 .. 5→2.5)
        db.refreshCollideTimer = life + 1f;   // longer than the aura → the DoT applies once
        db.hitImmaterial = true;              // the firestorm burns through immaterial enemies (from level 1)
        db.hitProjectiles = false;
        return db;
    }

    // ---- detonation/ignite pieces --------------------------------------

    void IgniteBurst(float r, float charge)
    {
        if (burst == null) return;
        int n = Mathf.RoundToInt((90f + 180f * charge) * countMul);
        var ep = new ParticleSystem.EmitParams();
        for (int i = 0; i < n; i++)
        {
            float ang = Random.value * TAU;
            Vector3 dir = new(Mathf.Cos(ang), Mathf.Sin(ang), 0f);
            float life = Random.Range(0.4f, 0.85f) * (0.8f + 0.5f * charge);
            float dist = r * Random.Range(0.4f, 0.95f);
            ep.position = transform.position + dir * (r * 0.12f);
            ep.velocity = dir * (dist / life);
            ep.startColor = GrainShade(Mathf.Lerp(0.5f, 0.95f, charge));
            ep.startSize = Random.Range(0.05f, 0.125f * (1f + 0.5f * charge)); // fixed min; max creeps up with charge
            ep.startLifetime = life;
            burst.Emit(ep, 1);
        }
    }

    void IgniteShove(float r, float charge)
    {
        if (igniteKnockback <= 0f) return;
        var foes = GS.FindEnemies(casterTag, transform.position, r, false, false, s_overlap);
        if (foes == null) return;
        foreach (Transform f in foes)
        {
            if (f == null) continue;
            if (!f.TryGetComponent<ActionScript>(out var asc)) continue;
            Vector2 off = (Vector2)(f.position - transform.position);
            Vector2 dir = off.normalized;
            if (dir == Vector2.zero) dir = Random.insideUnitCircle.normalized;
            float fall = 1f - Mathf.Clamp01(off.magnitude / Mathf.Max(0.01f, r));
            asc.AddPush(0.12f, true, dir * asc.mass * igniteKnockback * (0.5f + 0.6f * charge) * fall);
        }
    }

    // ---- emission helpers ----------------------------------------------

    // Emit a churning annulus of fire centred on the player. The orbital + noise modules do the
    // swirl/turbulence; we just seed grains around the ring with a touch of inward drift.
    void EmitRing(ParticleSystem ps, float r, float thickFrac, int n, float brightChance, float lifeMin, float lifeMax)
    {
        if (ps == null || n <= 0) return;
        var ep = new ParticleSystem.EmitParams();
        float thick = r * thickFrac;
        for (int i = 0; i < n; i++)
        {
            float ang = Random.value * TAU;
            Vector3 dir = new(Mathf.Cos(ang), Mathf.Sin(ang), 0f);
            float rr = Mathf.Max(0.02f, r + Random.Range(-thick, thick));
            ep.position = transform.position + dir * rr;
            ep.velocity = dir * (Random.Range(-0.3f, 0.1f) * r); // mostly a gentle inward lick
            ep.startColor = GrainShade(brightChance);
            ep.startSize = Random.Range(0.045f, 0.105f * (1f + 0.5f * sizeCharge)); // fixed min; max creeps up with charge
            ep.startLifetime = Random.Range(lifeMin, lifeMax);
            ps.Emit(ep, 1);
        }
    }

    // Sparks that spiral off the storm and loft outward.
    void EmitEmbers(float r, int n)
    {
        if (embers == null || n <= 0) return;
        var ep = new ParticleSystem.EmitParams();
        for (int i = 0; i < n; i++)
        {
            float ang = Random.value * TAU;
            Vector3 dir = new(Mathf.Cos(ang), Mathf.Sin(ang), 0f);
            Vector3 tang = new(-dir.y * spinDir, dir.x * spinDir, 0f);
            float rr = r * Random.Range(0.5f, 0.95f);
            float life = Random.Range(0.6f, 1.3f);
            ep.position = transform.position + dir * rr;
            ep.velocity = tang * (stormSpin * rr * 0.25f) + dir * Random.Range(0.1f, 0.4f)
                          + (Vector3)(Random.insideUnitCircle * 0.2f);
            ep.startColor = deepShade;
            ep.startSize = Random.Range(0.015f, 0.04f * (1f + 0.5f * sizeCharge)); // fixed min; max creeps up with charge
            ep.startLifetime = life;
            embers.Emit(ep, 1);
        }
    }

    // ---- brightness -----------------------------------------------------

    Color GrainShade(float brightChance) => Random.value < brightChance ? brightShade : deepShade;

    // Drive HEAT by scaling the glow shader's HDR `thecolor` (this is the property that actually
    // blooms — Nova's `_Color` write is a no-op on this shader, so we do brightness on the right
    // property here). Also writes a grey `_Color` for any fallback shader that has it.
    void ApplyBrightness(float b)
    {
        brightness = b;
        Color hot = new(baseHot.r * b, baseHot.g * b, baseHot.b * b, 1f);
        Color grey = new(b, b, b, 1f);
        for (int i = 0; i < mats.Count; i++)
        {
            if (mats[i] == null) continue;
            if (mats[i].HasProperty(HotID)) mats[i].SetColor(HotID, hot);
            if (mats[i].HasProperty(ColorID)) mats[i].SetColor(ColorID, grey);
        }
    }

    // ---- construction --------------------------------------------------

    ParticleSystem MakeSystem(string name, int order)
    {
        var g = new GameObject(name);
        g.transform.SetParent(transform, false);
        var ps = g.AddComponent<ParticleSystem>();
        // AddComponent auto-plays (default playOnAwake); fully stop before touching
        // main.duration/simulationSpace (can't be set while playing).
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var psr = g.GetComponent<ParticleSystemRenderer>();

        var frames = LoadFrames();
        var m = new Material(baseMat);                 // unlit-red glow material copy (its `thecolor` = the red)
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
        main.maxParticles = 8000;
        main.startSpeed = 0f;
        main.gravityModifier = 0f;

        var em = ps.emission; em.enabled = false;
        var sh = ps.shape; sh.enabled = false;

        // SignatureFX sprite sheet animated across each particle's lifetime (frame 0 → last)
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

    // The storm body — a churning annulus that orbits the player and is turbulised by noise.
    ParticleSystem MakeRingSystem()
    {
        var ps = MakeSystem("Ring", 50);
        var vol = ps.velocityOverLifetime;
        vol.enabled = true;
        vol.space = ParticleSystemSimulationSpace.World;
        vol.orbitalZ = new ParticleSystem.MinMaxCurve(stormSpin * spinDir);     // swirl
        vol.radial = new ParticleSystem.MinMaxCurve(-0.25f * maxRadius * sizeScale); // gentle inward pull keeps the ring
        var noise = ps.noise;
        noise.enabled = true;
        noise.damping = false;
        noise.strength = noiseStrength;
        noise.frequency = noiseFrequency;
        noise.scrollSpeed = 0.8f;
        noise.rotationAmount = 1.2f;
        SetColorOverLifetime(ps, 0.12f, 0.7f);
        SetSizeOverLifetime(ps, new AnimationCurve(
            new Keyframe(0f, 0.25f), new Keyframe(0.4f, 1f), new Keyframe(1f, 0.2f)));
        ps.Play();
        return ps;
    }

    // The hot inner eye-wall — tighter, faster, more turbulent.
    ParticleSystem MakeInnerSystem()
    {
        var ps = MakeSystem("Inner", 52);
        var vol = ps.velocityOverLifetime;
        vol.enabled = true;
        vol.space = ParticleSystemSimulationSpace.World;
        vol.orbitalZ = new ParticleSystem.MinMaxCurve(stormSpin * spinDir * 1.5f);
        vol.radial = new ParticleSystem.MinMaxCurve(-0.1f * maxRadius * sizeScale);
        var noise = ps.noise;
        noise.enabled = true;
        noise.damping = false;
        noise.strength = noiseStrength * 1.4f;
        noise.frequency = noiseFrequency * 1.3f;
        noise.scrollSpeed = 1.1f;
        noise.rotationAmount = 1.6f;
        SetColorOverLifetime(ps, 0.1f, 0.65f);
        SetSizeOverLifetime(ps, new AnimationCurve(
            new Keyframe(0f, 0.3f), new Keyframe(0.35f, 1f), new Keyframe(1f, 0.15f)));
        ps.Play();
        return ps;
    }

    // Sparks flung off the storm.
    ParticleSystem MakeEmberSystem()
    {
        var ps = MakeSystem("Embers", 51);
        var lim = ps.limitVelocityOverLifetime;
        lim.enabled = true; lim.dampen = 0.12f; lim.limit = new ParticleSystem.MinMaxCurve(3f);
        var noise = ps.noise;
        noise.enabled = true;
        noise.damping = false;
        noise.strength = noiseStrength * 0.8f;
        noise.frequency = noiseFrequency;
        noise.scrollSpeed = 1f;
        SetColorOverLifetime(ps, 0.08f, 0.5f);
        SetSizeOverLifetime(ps, new AnimationCurve(
            new Keyframe(0f, 1f), new Keyframe(0.6f, 0.8f), new Keyframe(1f, 0f)));
        ps.Play();
        return ps;
    }

    // The ignition pop — grains thrown straight outward (only a light swirl).
    ParticleSystem MakeBurstSystem()
    {
        var ps = MakeSystem("Burst", 53);
        var vol = ps.velocityOverLifetime;
        vol.enabled = true;
        vol.space = ParticleSystemSimulationSpace.World;
        vol.orbitalZ = new ParticleSystem.MinMaxCurve(stormSpin * spinDir * 0.6f);
        SetColorOverLifetime(ps, 0.05f, 0.55f);
        SetSizeOverLifetime(ps, new AnimationCurve(
            new Keyframe(0f, 0.4f), new Keyframe(0.12f, 1f), new Keyframe(1f, 0.1f)));
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

    // Load + cache the SignatureFX sprite-sheet frames (SignatureFX_0..n) in order.
    static Sprite[] LoadFrames()
    {
        if (s_frames != null) return s_frames;
        var all = Resources.LoadAll<Sprite>("SignatureFX");
        var list = new List<Sprite>();
        if (all != null)
            foreach (var s in all)
                if (s != null && s.name.StartsWith("SignatureFX_")) list.Add(s);
        list.Sort((a, b) => FrameIndex(a.name) - FrameIndex(b.name));
        s_frames = list.ToArray();
        return s_frames;
    }

    static int FrameIndex(string n)
    {
        int u = n.LastIndexOf('_');
        return (u >= 0 && int.TryParse(n.Substring(u + 1), out var v)) ? v : 0;
    }

    // Safety net: if this object is torn down mid-channel (e.g. the part is removed), make sure
    // we don't leave the player rooted. (Immaterial is a bounded-duration status that expires on
    // its own, so it needs no cleanup.)
    void OnDestroy() => ReleaseRoot();
}
