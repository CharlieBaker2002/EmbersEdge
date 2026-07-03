using UnityEngine;

/// <summary>
/// STUN tile (red element). A concussive ward baked into the floor: step on it and a jagged red shock
/// stuns the player for `effectDuration`s, then it goes dormant and RE-ARMS after `rearm`s ("refreshes
/// every 3 sec"). Glyph: a sharp many-pointed starburst around a hot core.
/// </summary>
public class StunTile : FloorTile
{
    protected override Mode TriggerMode => Mode.Discrete;
    protected override Color Element => R4;
    protected override Color[] Ramp() => new[] { R4, R3 };

    protected override void Fire(Unit u)
    {
        GS.Stat(u, "stun", effectDuration);
        Shockwave.Spawn(transform.position, radius * 1.2f, 0.02f, 0.32f);
    }

    protected override Sprite BakeBody() => BakeSprite(128, TileWorld, (nx, ny) =>
    {
        float r = Mathf.Sqrt(nx * nx + ny * ny);
        float ang = Mathf.Atan2(ny, nx);
        Color c = Pad(nx, ny, R3);

        // jagged starburst: 9 sharp spikes tapering to points
        const int spikes = 9;
        float seg = Mathf.PI * 2f / spikes;
        float dC = Mathf.Abs(Mathf.Repeat(ang, seg) - seg * 0.5f);
        if (r > 0.1f && r < 0.7f)
        {
            float tt = Mathf.Clamp01((r - 0.1f) / 0.6f);          // 0 base -> 1 tip
            float halfW = Mathf.Lerp(seg * 0.34f, 0.012f, tt);    // wide base, needle tip
            if (dC < halfW) c = Over(c, A(Color.Lerp(R3, R4, 1f - tt), 0.95f));
        }
        // hot core
        float core = Fill(r - 0.15f, AA);
        if (core > 0f) c = Over(c, A(Color.Lerp(R4, Outline, Mathf.Clamp01((r - 0.1f) / 0.06f)), core));
        return c;
    });
}
