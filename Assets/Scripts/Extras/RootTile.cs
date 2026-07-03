using UnityEngine;

/// <summary>
/// ROOT tile (blue element). A lattice of ice that snaps shut around the player's feet: step on it and you
/// are hard-ROOTED for `effectDuration`s (you keep acting/shooting, you just can't move), then it RE-ARMS
/// after `rearm`s. Glyph: a hexagonal frost cage with inward spokes — distinct from the blue Slow ripples.
/// </summary>
public class RootTile : FloorTile
{
    protected override Mode TriggerMode => Mode.Discrete;
    protected override Color Element => B4;
    protected override Color[] Ramp() => new[] { B4, B3 };

    protected override void Fire(Unit u)
    {
        GS.Stat(u, "root", effectDuration, 0f);
    }

    protected override Sprite BakeBody() => BakeSprite(128, TileWorld, (nx, ny) =>
    {
        float r = Mathf.Sqrt(nx * nx + ny * ny);
        float ang = Mathf.Atan2(ny, nx);
        Color c = Pad(nx, ny, B3);

        // hexagonal ice cage
        float hexEdge = Mathf.Abs(r - 0.6f * PolyRadius(ang, 6, Mathf.PI / 6f));
        float hex = Mathf.Clamp01(1f - hexEdge / 0.05f);
        if (hex > 0f) c = Over(c, A(B4, hex));

        // six inward spokes to the vertices (the cage bars)
        const int spokes = 6;
        float seg = Mathf.PI * 2f / spokes;
        float dC = Mathf.Abs(Mathf.Repeat(ang - Mathf.PI / 6f, seg) - seg * 0.5f);
        if (r > 0.08f && r < 0.62f && dC < 0.06f) c = Over(c, A(B3, 0.9f));

        // frozen core
        float core = Fill(r - 0.13f, AA);
        if (core > 0f) c = Over(c, A(Color.Lerp(B4, W3, 0.25f), core));
        return c;
    });
}
