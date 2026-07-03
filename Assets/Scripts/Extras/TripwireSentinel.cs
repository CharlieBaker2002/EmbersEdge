using System.Collections;
using UnityEngine;

/// <summary>
/// (2) Tripwire Sentinel. A wall-mounted sentry that projects a long FIXED sightline into the arena
/// (15 units, stopped by walls). It does nothing until the player crosses that line; then, after a
/// one-second charge, it looses a single Waggler shot straight down the beam. Single use.
/// Build four variants at different `aimAngle`s to fan the rim with crossing tripwires.
/// </summary>
public class TripwireSentinel : WallSentry
{
    [Header("Tripwire")]
    [Tooltip("Beam direction as an offset (degrees) from straight-inward. The four authored copies use " +
             "-50 / -17 / +17 / +50 so a stretch of rim is fanned with crossing sightlines.")]
    public float aimAngle = 0f;

    protected override IEnumerator Run()
    {
        if (beam != null) { beam.widthMultiplier = beamWidth; }
        Vector2 aim = InwardDir(transform.position).Rotated(aimAngle).normalized;  // fixed for life
        transform.up = aim;

        // arm: hold the line until something crosses it.
        bool tripped = false;
        while (!tripped)
        {
            tripped = Scan(transform.position, aim, out Vector2 end);
            DrawBeam(transform.position, end, IdlePulse());
            yield return null;
        }

        // fire down the fixed beam, then expire.
        yield return StartCoroutine(ChargeAndFire(() => aim));
        Consume(0.35f);
    }
}
