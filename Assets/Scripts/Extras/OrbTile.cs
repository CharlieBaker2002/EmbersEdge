using UnityEngine;

/// <summary>
/// ORB tile (white element). A cache the player walks over to scatter a small handful of collectible WHITE
/// (general) orbs — `magnitude` is the base count, giving Random.Range(base, base+3) so the default 3 yields
/// 3–5 orbs. One-shot: pops and is spent on contact. The Wave Forge gives it a low spawn weight so it only
/// turns up occasionally ("20% chance of spawning 3–5 white orbs"). Glyph: a ring of orbs around a core.
/// </summary>
public class OrbTile : FloorTile
{
    protected override Mode TriggerMode => Mode.OneShot;
    protected override Color Element => W4;
    protected override Color[] Ramp() => new[] { W4, W3 };

    protected override void Fire(Unit u)
    {
        int b = Mathf.Max(1, Mathf.RoundToInt(magnitude <= 0f ? 3f : magnitude));
        int n = Random.Range(b, b + 3);                       // base 3 -> 3..5
        GS.CallSpawnOrbs(transform.position, new int[] { n, 0, 0, 0 }); // index 0 = white/general orbs
    }

    protected override Sprite BakeBody() => BakeSprite(128, TileWorld, (nx, ny) =>
    {
        float r = Mathf.Sqrt(nx * nx + ny * ny);
        Color c = Pad(nx, ny, W3);

        // a ring of six little orbs
        const int n = 6;
        for (int k = 0; k < n; k++)
        {
            float a = k * (Mathf.PI * 2f / n) + 0.4f;
            Vector2 p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.52f;
            float d = Vector2.Distance(new Vector2(nx, ny), p);
            float dot = Fill(d - 0.13f, AA);
            if (dot > 0f) c = Over(c, A(Color.Lerp(W4, W3, Mathf.Clamp01(d / 0.13f)), dot));
        }
        // central orb
        float core = Fill(r - 0.17f, AA);
        if (core > 0f) c = Over(c, A(Color.Lerp(W4, W3, Mathf.Clamp01(r / 0.17f)), core));
        return c;
    });
}
