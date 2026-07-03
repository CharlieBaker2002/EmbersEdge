using UnityEngine;

/// <summary>
/// HEAL tile (white element). A small restorative mote the player walks over to mend a sliver of health
/// (`magnitude` = fraction of max HP, e.g. 0.12 = 12%). One-shot: it pops and is spent on contact. The
/// Wave Forge gives it a low spawn weight so it only turns up occasionally ("20% chance of spawning").
/// Glyph: a clean white cross.
/// </summary>
public class HealTile : FloorTile
{
    protected override Mode TriggerMode => Mode.OneShot;
    protected override Color Element => W4;
    protected override Color[] Ramp() => new[] { W4, W3 };

    protected override void Fire(Unit u)
    {
        if (u.ls == null) return;
        if (u.ls.hp >= u.ls.maxHp) return;                 // no waste at full health
        float amount = u.ls.maxHp * Mathf.Clamp01(magnitude <= 0f ? 0.12f : magnitude);
        u.ls.Change(amount, 0);                             // positive = heal (green flash fires for the player)
    }

    protected override Sprite BakeBody() => BakeSprite(128, TileWorld, (nx, ny) =>
    {
        float r = Mathf.Sqrt(nx * nx + ny * ny);
        Color c = Pad(nx, ny, W3);

        // a plus / medical cross (two rounded bars), bright mint-white
        const float halfW = 0.13f, halfLen = 0.58f;
        float barH = Mathf.Max(Mathf.Abs(ny) - halfW, Mathf.Abs(nx) - halfLen);
        float barV = Mathf.Max(Mathf.Abs(nx) - halfW, Mathf.Abs(ny) - halfLen);
        float cross = Fill(Mathf.Min(barH, barV), AA);
        if (cross > 0f && r < 0.74f) c = Over(c, A(Color.Lerp(W3, W4, 0.55f), cross));
        // soft inner glow
        float core = Fill(r - 0.18f, AA * 2f);
        if (core > 0f) c = Over(c, A(W4, core * 0.5f));
        return c;
    });
}
