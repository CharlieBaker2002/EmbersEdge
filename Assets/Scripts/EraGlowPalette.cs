using UnityEngine;

/// <summary>Per-era glow colours. Each entry is a colour MULTIPLIER applied on top of a Glow level
/// material's greyscale `thecolor` (its brightness), so material brightness × row colour = the
/// exact emissive colour that level renders in that era. The rows below reproduce the retired
/// Purple / Yellow / Orange material sets bit-for-bit (era 0 rows are therefore the pure purple
/// hue; era 1/2 rows also carry those eras' relative brightness). `outline` is the dungeon-wall
/// healthy outline colour (LifeScript), raw HDR. The runtime reads Resources/EraGlowPalette;
/// a missing asset falls back to the built-in defaults.</summary>
[CreateAssetMenu(menuName = "Embers Edge/Era Glow Palette", fileName = "EraGlowPalette")]
public class EraGlowPalette : ScriptableObject
{
    [System.Serializable]
    public struct EraRow
    {
        public Color dim, bright, lit, super_, line, outline;
        public Color Level(GlowLevel l) => l switch
        {
            GlowLevel.Dim => dim, GlowLevel.Bright => bright, GlowLevel.Lit => lit,
            GlowLevel.Super => super_, _ => line,
        };
    }

    [Tooltip("Index = era. Colour multipliers per glow level (see class summary) + the dungeon outline colour.")]
    public EraRow[] eras = Defaults();

    public static EraRow[] Defaults() => new[]
    {
        new EraRow { dim = new Color(0.125654f, 0f, 1f), bright = new Color(0.125654f, 0f, 1f), lit = new Color(0.125654f, 0f, 1f), super_ = new Color(0.015f, 0.005f, 1f), line = new Color(0.337021f, 0.136793f, 1f), outline = new Color(1.5f, 1.08f, 3.6f) },
        new EraRow { dim = new Color(0.812288f, 0.738615f, 0f), bright = new Color(1.00761f, 1.13356f, 0f), lit = new Color(0.503803f, 0.566779f, 0f), super_ = new Color(0.111111f, 0.125f, 0f), line = new Color(0.932036f, 0.847502f, 0f), outline = new Color(2.75f, 2.75f, 2.1f) },
        new EraRow { dim = new Color(0.501311f, 0.0791565f, 0.0354701f), bright = new Color(1f, 0.157899f, 0.0707547f), lit = new Color(0.617159f, 0.0974487f, 0.0436669f), super_ = new Color(0.267943f, 0.042308f, 0.0189583f), line = new Color(0.806283f, 0.48487f, 0.0732984f), outline = new Color(3f, 2f, 1.6f) }
    };

    public static EraRow Default(int era)
    {
        var d = Defaults();
        return d[Mathf.Clamp(era, 0, d.Length - 1)];
    }

    public EraRow Row(int era)
    {
        if (eras == null || eras.Length == 0) return Default(era);
        return eras[Mathf.Clamp(era, 0, eras.Length - 1)];
    }
}
