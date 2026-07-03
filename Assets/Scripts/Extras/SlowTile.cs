using UnityEngine;

/// <summary>
/// SLOW tile (blue element). A patch of creeping frost: while the player stands on it their movement is
/// dragged to `magnitude` of normal (e.g. 0.5 = half speed), re-stamped continuously so the chill lingers
/// `effectDuration`s after they step off. Glyph: concentric frost ripples (distinct from the Root cage).
/// </summary>
public class SlowTile : FloorTile
{
    protected override Mode TriggerMode => Mode.Continuous;
    protected override Color Element => B4;
    protected override Color[] Ramp() => new[] { B4, B3 };

    protected override void Fire(Unit u)
    {
        // value2 = kept-speed fraction (Copter/Prism convention): smaller = slower.
        GS.Stat(u, "slow", effectDuration, Mathf.Clamp(magnitude <= 0f ? 0.5f : magnitude, 0.1f, 0.95f));
    }

    protected override Sprite BakeBody() => BakeSprite(128, TileWorld, (nx, ny) =>
    {
        float r = Mathf.Sqrt(nx * nx + ny * ny);
        Color c = Pad(nx, ny, B2);

        // three concentric ripple rings
        float[] rings = { 0.28f, 0.46f, 0.64f };
        for (int i = 0; i < rings.Length; i++)
        {
            float ring = Mathf.Clamp01(1f - Mathf.Abs(r - rings[i]) / 0.045f);
            if (ring > 0f) c = Over(c, A(Color.Lerp(B3, B4, 0.4f + 0.2f * i), ring));
        }
        // frost core
        float core = Fill(r - 0.12f, AA);
        if (core > 0f) c = Over(c, A(Color.Lerp(B4, W3, 0.2f), core * 0.9f));
        return c;
    });
}
