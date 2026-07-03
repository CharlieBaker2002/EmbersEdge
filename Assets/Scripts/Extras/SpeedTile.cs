using UnityEngine;

/// <summary>
/// SPEED tile (green element). A burst of verdant haste: while the player stands on it they are stimmed to
/// `magnitude`× movement/act-rate (e.g. 1.5 = +50%), re-stamped continuously so the boost lingers
/// `effectDuration`s after stepping off. Glyph: outward motion streaks (green) around a bright core.
/// </summary>
public class SpeedTile : FloorTile
{
    protected override Mode TriggerMode => Mode.Continuous;
    protected override Color Element => G4;
    protected override Color[] Ramp() => new[] { G4, G3 };

    protected override void Fire(Unit u)
    {
        // "stim" raises actRate AND (on the player) adds move force via StimWheels. value2 = multiplier.
        GS.Stat(u, "stim", effectDuration, Mathf.Max(1.05f, magnitude <= 0f ? 1.5f : magnitude));
    }

    protected override Sprite BakeBody() => BakeSprite(128, TileWorld, (nx, ny) =>
    {
        float r = Mathf.Sqrt(nx * nx + ny * ny);
        float ang = Mathf.Atan2(ny, nx);
        Color c = Pad(nx, ny, G3);

        // outward motion streaks (8 thin radial lines in the outer band)
        const int n = 8;
        float seg = Mathf.PI * 2f / n;
        float dC = Mathf.Abs(Mathf.Repeat(ang, seg) - seg * 0.5f);
        if (r > 0.34f && r < 0.74f && dC < 0.05f)
            c = Over(c, A(Color.Lerp(G3, G4, Mathf.Clamp01((r - 0.34f) / 0.4f)), 0.95f));

        // inner ring + bright core
        float ring = Mathf.Clamp01(1f - Mathf.Abs(r - 0.27f) / 0.04f);
        if (ring > 0f) c = Over(c, A(G4, ring));
        float core = Fill(r - 0.13f, AA);
        if (core > 0f) c = Over(c, A(G4, core));
        return c;
    });
}
