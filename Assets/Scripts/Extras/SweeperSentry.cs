using System.Collections;
using UnityEngine;

/// <summary>
/// (3) Sweeper Sentry. A wall-mounted sentry that stays exactly where it was painted and sweeps a medium
/// sightline (8 units) back and forth across the room like a searchlight. The instant the beam catches the
/// player it locks, charges for a second and fires a Waggler along the beam. Single use.
/// </summary>
public class SweeperSentry : WallSentry
{
    [Header("Sweep")]
    [Tooltip("Sweep rate of the beam (radians-ish per second feed into a sine).")]
    public float sweepSpeed = 2f;
    [Tooltip("Sweep half-angle (degrees) to either side of straight-inward.")]
    public float sweepArc = 55f;

    protected override IEnumerator Run()
    {
        // Anchor the sweep on 'inward' (toward the pocket centre) and oscillate the aim around it.
        Vector2 inward = InwardDir(transform.position);
        float baseAng = Mathf.Atan2(inward.y, inward.x) * Mathf.Rad2Deg;

        bool detected = false;
        while (!detected)
        {
            float off = sweepArc * Mathf.Sin(Time.time * sweepSpeed);
            Vector2 aim = GS.ATV(baseAng + off);
            transform.up = aim;
            detected = Scan(transform.position, aim, out Vector2 end);
            DrawBeam(transform.position, end, IdlePulse());
            yield return null;
        }

        // lock on the current bearing and fire.
        yield return StartCoroutine(ChargeAndFire(() => (Vector2)transform.up));
        Consume(0.35f);
    }
}
