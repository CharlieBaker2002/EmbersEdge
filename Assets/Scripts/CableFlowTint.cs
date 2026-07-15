using System;
using UnityEngine;

/// <summary>
/// Warms a cable's glow with recent throughput. Every debit carried across the cable is
/// Report()ed into a single accumulator (`pulse`) that halves every <see cref="halfLife"/>
/// seconds — a leaky integrator. Heat is a direct function of the accumulated energy, so a
/// big burst (a charger turret's 1s gulp) reads just as hot as the same energy trickled out
/// over seconds, and the exponential decay IS the cool-down: no window, no spring.
/// Sustained flow at r e/s settles the accumulator at r·halfLife/ln2 (≈2·r at 1.4s).
///
/// The cable material (Glow Unlit) is emissive where its _Emission texture is non-black,
/// and the shader's _Color tints exactly those details — like the battery glow rig. Heat
/// runs that colour grey → era colour → the SUPERBRIGHT era colour (GS.MatByEra) at full
/// load. A broken link (either endpoint dead/destroyed) dims the details right down,
/// mirroring how dead buildings drop their tint.
///
/// Added to the cable LineRenderer GameObject by <see cref="EnergyPylon.OnConnected"/>.
/// The LR vertex gradient is NOT used — Glow Unlit ignores vertex colour.
/// </summary>
public class CableFlowTint : MonoBehaviour
{
    [Tooltip("Seconds for the accumulated glow energy to halve — the cool-down curve after flow stops.")]
    [SerializeField] private float halfLife = 1.4f;
    [Tooltip("Detail tint at zero load.")]
    [SerializeField] private Color idleColor = new(0.45f, 0.45f, 0.45f);
    [Tooltip("ABSOLUTE accumulated energy that reads as fully hot (≈ sustained e/s × 2 at a 1.4s half-life). Never normalise against a source's rated capacity — generator DrawRates are huge/infinite and zero out low loads.")]
    [SerializeField] private float fullLoadEnergy = 4f;
    [Tooltip("Heat floor for ANY real throughput: a trickle already reads this purple, load magnitude only fills in the rest.")]
    [SerializeField] private float activeFloor = 0.8f;
    [Tooltip("Accumulated energy at which the glow already sits at activeFloor; below it the glow fades toward grey, so the decay ends smoothly instead of cliffing off the floor.")]
    [SerializeField] private float traceEnergy = 0.2f;
    [Tooltip("Detail tint when an endpoint of the connection is dead — the cable's 'dimmed' state.")]
    [SerializeField] private Color deadColor = new(0.42f, 0.42f, 0.42f);
    [Tooltip("Cable alpha when the link is fully dead (matches destroyed buildings' see-through ghost).")]
    [SerializeField] private float deadAlpha = 0.45f;

    // The Glow shaders' colour property is `thecolor` (the graph overrides the _Color
    // reference name) — writing "_Color" is silently inert. Same trap as FirestormCore.
    private static readonly int ColorId = Shader.PropertyToID("thecolor");

    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

    private LineRenderer lr;
    private Material mat;
    private Func<bool> linkDead;

    // Energy carried recently: Report() sums in, Update() halves it every halfLife seconds.
    private float pulse;
    private float dim, dimVel;

    // Transparency rides _MainTex: the Glow shader's final alpha IS the body texture's alpha
    // (colour alpha is ignored), so the dead fade swaps in a 1×1 translucent-black body.
    private Texture origBodyTex;
    private Texture2D fadeBodyTex;
    private float appliedAlpha = 1f;

    /// <summary>linkDead = true while either side of the connection is dead/destroyed.</summary>
    public void Init(LineRenderer line, Func<bool> linkDead = null)
    {
        lr = line;
        this.linkDead = linkDead;
        // Per-cable material instance so each cable heats independently.
        if (lr != null) mat = lr.material;
        if (mat != null) origBodyTex = mat.GetTexture(MainTexId);
    }

    /// <summary>Energy actually carried across the cable — call once per debit, any direction.</summary>
    public void Report(float amount)
    {
        if (amount > 0f) pulse += amount;
    }

    private void Update()
    {
        if (mat == null) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        bool broken = linkDead != null && linkDead();

        pulse *= Mathf.Pow(0.5f, dt / Mathf.Max(halfLife, 1e-3f));
        if (pulse < 1e-4f) pulse = 0f;

        // Heat = presence × floor-plus-magnitude. Presence ramps 0..1 over traceEnergy so the
        // tail of the decay eases to grey; above it any throughput sits at least at the
        // activeFloor, and load magnitude eases in the remainder (out-sine).
        float load = Mathf.Clamp01(pulse / Mathf.Max(fullLoadEnergy, 1e-3f));
        float presence = Mathf.Clamp01(pulse / Mathf.Max(traceEnergy, 1e-3f));
        float eased = Mathf.Sin(load * Mathf.PI * 0.5f);
        float heat = presence * Mathf.Lerp(activeFloor, 1f, eased);

        // The dead-dim spring runs fast — a killed endpoint should read immediately — and it
        // overrides the heat colour entirely at dim 1, so pulse can keep decaying underneath.
        dim = Mathf.SmoothDamp(dim, broken ? 1f : 0f, ref dimVel, 0.12f, Mathf.Infinity, dt);

        // Hot end: plain era colour at low magnitude, running to the SUPERBRIGHT era colour
        // (GS.MatByEra superBright — hue-saturated HDR, e.g. purple with B≈34) at full load.
        // NEVER a uniform ×boost on the era colour: equal-channel HDR blooms toward WHITE,
        // not deeper purple. The blend is GEOMETRIC (log-space), not linear: bloom perception
        // is roughly logarithmic, so a linear lerp toward B≈34 already blooms hard at low
        // loads — a trickling pelter out-glowed the charger's volleys. Geometric keeps low
        // loads near the plain era colour and saves the bloom for genuinely hot cables.
        Color era = GS.ColFromEra(), super = SuperbrightEraColor();
        Color hot = new Color(GeomLerp(era.r, super.r, eased),
                              GeomLerp(era.g, super.g, eased),
                              GeomLerp(era.b, super.b, eased));
        Color c = Color.Lerp(Color.Lerp(idleColor, hot, heat), deadColor, dim);
        c.a = 1f;
        mat.SetColor(ColorId, c);

        // Dead-fade transparency, eased by the same dim spring. Restored once fully alive.
        float alpha = Mathf.Lerp(1f, deadAlpha, dim);
        if (alpha < 0.999f)
        {
            if (Mathf.Abs(alpha - appliedAlpha) > 0.002f)
            {
                if (fadeBodyTex == null) fadeBodyTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                fadeBodyTex.SetPixel(0, 0, new Color(0f, 0f, 0f, alpha));
                fadeBodyTex.Apply();
                mat.SetTexture(MainTexId, fadeBodyTex);
                appliedAlpha = alpha;
            }
        }
        else if (appliedAlpha < 1f)
        {
            mat.SetTexture(MainTexId, origBodyTex);
            appliedAlpha = 1f;
        }
    }

    /// <summary>Per-channel log-space interpolation: a·(b/a)^t. Equal RATIO steps instead of
    /// equal absolute steps, matching how HDR emission is perceived.</summary>
    static float GeomLerp(float a, float b, float t)
    {
        a = Mathf.Max(a, 1e-3f);
        b = Mathf.Max(b, 1e-3f);
        return a * Mathf.Pow(b / a, t);
    }

    /// <summary>The current era's superbright material colour — the full-heat look. Reads
    /// `thecolor` off GS.MatByEra(superBright) so cables and superbright props stay in lockstep.</summary>
    static Color SuperbrightEraColor()
    {
        var m = GS.MatByEra(GS.era, superBright: true);
        return m != null && m.HasProperty(ColorId) ? m.GetColor(ColorId) : GS.ColFromEra();
    }

    private void OnDestroy()
    {
        if (mat != null) Destroy(mat);
        if (fadeBodyTex != null) Destroy(fadeBodyTex);
    }
}
