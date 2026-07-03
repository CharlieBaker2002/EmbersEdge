using UnityEngine;

/// <summary>
/// LAUNCH tile (red element) — tile #5, the repulsor. Step on it and a burst of force flings the player
/// radially outward from the tile centre (`magnitude` = launch speed added to the player's velocity), then
/// it RE-ARMS after `rearm`s. A chaotic mobility/displacement hazard. Glyph: four bold outward arrows.
/// </summary>
public class LaunchTile : FloorTile
{
    protected override Mode TriggerMode => Mode.Discrete;
    protected override Color Element => R4;
    protected override Color[] Ramp() => new[] { R4, R3 };

    protected override void Fire(Unit u)
    {
        Vector2 dir = (Vector2)u.transform.position - (Vector2)transform.position;
        dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Random.insideUnitCircle.normalized;
        // Direct velocity impulse — reliable on the player regardless of `pushable` (TryAddForce/AddPush gate
        // on it). ActionScript's velocity damping then bleeds the launch off over the next few frames.
        if (u.AS != null && u.AS.rb != null)
            u.AS.rb.linearVelocity += dir * Mathf.Max(4f, magnitude <= 0f ? 13f : magnitude);
        Shockwave.Spawn(transform.position, radius * 1.4f, 0.02f, 0.4f);
        // a directional kick of embers the way the player was flung
        EmitBurst(burst, transform.position, 10, Ramp(), 3f, 6f, 0.3f, 0.6f, 0.14f * radius, 0.28f * radius,
            70f, Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg);
    }

    protected override Sprite BakeBody() => BakeSprite(128, TileWorld, (nx, ny) =>
    {
        float r = Mathf.Sqrt(nx * nx + ny * ny);
        float ang = Mathf.Atan2(ny, nx);
        Color c = Pad(nx, ny, R3);

        // four outward arrows (N/E/S/W): a narrow shaft + a flared arrowhead near the tip
        const int n = 4;
        float seg = Mathf.PI * 2f / n;
        float dC = Mathf.Abs(Mathf.Repeat(ang + seg * 0.5f, seg) - seg * 0.5f);
        if (r > 0.1f && r < 0.74f)
        {
            float halfW = r < 0.55f ? 0.06f : Mathf.Lerp(0.2f, 0f, (r - 0.55f) / 0.19f); // shaft -> arrowhead
            if (dC < halfW) c = Over(c, A(Color.Lerp(R3, R4, Mathf.Clamp01((r - 0.1f) / 0.64f)), 0.95f));
        }
        // core
        float core = Fill(r - 0.14f, AA);
        if (core > 0f) c = Over(c, A(R4, core));
        return c;
    });
}
