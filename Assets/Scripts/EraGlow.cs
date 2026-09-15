using UnityEngine;

/// <summary>The glow brightness levels — the ONLY era-glow materials (Resources/Glow).
/// Dim = the plain era material, Bright = the ore/ember glow, Lit = the lit-graph body glow,
/// Super = the HDR superbright (souls, embers, cables, the core), Line = the core/boundary/wave
/// line glow (the old L0MatGlow).</summary>
public enum GlowLevel { Dim = 0, Bright = 1, Lit = 2, Super = 3, Line = 4 }

/// <summary>
/// Era colour = global shader colours. The Glow Lit / Glow Unlit graphs multiply a material's
/// greyscale `thecolor` (its BRIGHTNESS) by the global colour of the material's `_EraLevel`
/// (1..5 = Dim..Line; 0 = untouched, which is every non-era material on those graphs). Turning
/// the era just re-sets the globals (<see cref="Apply"/>) from <see cref="EraGlowPalette"/>; no
/// material is ever swapped and no per-era material exists. Runtime copies of a Glow material
/// inherit `_EraLevel`, so scaling their `thecolor` stays era-correct; a script that paints an
/// EXPLICIT colour must zero `_EraLevel` on its copy.
/// </summary>
public static class EraGlow
{
    public static readonly int EraLevelId = Shader.PropertyToID("_EraLevel");
    public static readonly int TheColorId = Shader.PropertyToID("thecolor");
    static readonly int[] GlobalIds =
    {
        Shader.PropertyToID("_EraGlowDim"), Shader.PropertyToID("_EraGlowBright"), Shader.PropertyToID("_EraGlowLit"),
        Shader.PropertyToID("_EraGlowSuper"), Shader.PropertyToID("_EraGlowLine"),
    };
    static readonly string[] MatPaths = { "Glow/Glow Dim", "Glow/Glow Bright", "Glow/Glow Lit", "Glow/Glow Super", "Glow/Glow Line" };
    static readonly Material[] mats = new Material[MatPaths.Length];
    static EraGlowPalette palette;

    /// <summary>The shared material of a brightness level (Resources/Glow).</summary>
    public static Material Mat(GlowLevel level)
    {
        int i = Mathf.Clamp((int)level, 0, mats.Length - 1);
        if (mats[i] == null) mats[i] = Resources.Load<Material>(MatPaths[i]);
        return mats[i];
    }

    public static EraGlowPalette Palette
        => palette != null ? palette : (palette = Resources.Load<EraGlowPalette>("EraGlowPalette"));

    static EraGlowPalette.EraRow Row(int era) => Palette != null ? Palette.Row(era) : EraGlowPalette.Default(era);

    /// <summary>A level's brightness — the greyscale `thecolor` of its material.</summary>
    public static float Brightness(GlowLevel level)
    {
        var m = Mat(level);
        return m != null && m.HasProperty(TheColorId) ? m.GetColor(TheColorId).maxColorComponent : 1f;
    }

    /// <summary>The colour a level actually renders in an era (brightness × palette) — exactly the
    /// old per-era material's `thecolor`. For scripts that paint explicit colours onto NON-era
    /// materials (cable heat, EE icon fades, ore hue).</summary>
    public static Color Colour(GlowLevel level, int era = -1)
    {
        Color m = Row(era < 0 ? GS.era : era).Level(level);
        float k = Brightness(level);
        return new Color(m.r * k, m.g * k, m.b * k, 1f);
    }

    /// <summary>The dungeon-wall healthy outline colour of the era (raw HDR, LifeScript).</summary>
    public static Color Outline(int era = -1) => Row(era < 0 ? GS.era : era).outline;

    /// <summary>Set the global era colours — the whole visual era swap.</summary>
    public static void Apply(int era)
    {
        var row = Row(era);
        for (int i = 0; i < GlobalIds.Length; i++) Shader.SetGlobalColor(GlobalIds[i], row.Level((GlowLevel)i));
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        System.Array.Clear(mats, 0, mats.Length);   // no-domain-reload: never trust leaked statics
        palette = null;
        Apply(GS.era);
    }
}
