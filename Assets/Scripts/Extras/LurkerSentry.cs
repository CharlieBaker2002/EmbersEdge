using System.Collections;
using UnityEngine;

/// <summary>
/// (4) Lurker Sentry. A wall-mounted sentry with only a short sightline (4 units): it idly scans the
/// nearby floor, but the moment the player strays into that short range with a clear line of sight it
/// locks on, charges briefly and fires a Waggler TOWARD the player (leading their position at the shot).
/// Single use — the close-quarters ambusher of the set.
/// </summary>
public class LurkerSentry : WallSentry
{
    [Header("Lurker")]
    [Tooltip("Amplitude (degrees) of the lazy inward scan while it waits for prey.")]
    public float scanSweep = 38f;
    public float scanSpeed = 2.2f;

    Vector2 AimAtPlayer() => (PlayerPos - (Vector2)transform.position);

    protected override IEnumerator Run()
    {
        bool detected = false;
        while (!detected)
        {
            if (DistToPlayer <= beamLength)
            {
                // player is in reach — point straight at them and check the line is clear.
                Vector2 aim = AimAtPlayer().normalized;
                detected = Scan(transform.position, aim, out Vector2 end);
                transform.up = aim;
                // tighten the colour as they get closer, so the player feels the lock coming.
                float prox = 1f - Mathf.Clamp01(DistToPlayer / beamLength);
                DrawBeam(transform.position, end, Color.Lerp(Search, Alert, prox));
            }
            else
            {
                // idle: a slow inward scan.
                Vector2 aim = InwardDir(transform.position).Rotated(scanSweep * Mathf.Sin(Time.time * scanSpeed));
                Scan(transform.position, aim, out Vector2 end);
                transform.up = aim;
                DrawBeam(transform.position, end, IdlePulse());
            }
            yield return null;
        }

        // charge while tracking, then fire where the player is at release.
        yield return StartCoroutine(ChargeAndFire(AimAtPlayer));
        Consume(0.35f);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, beamLength);
    }
}
