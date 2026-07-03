using System.Collections;
using UnityEngine;

/// <summary>
/// (6) Warden Sentry — the concrete realisation of the wall-mounted <see cref="WallSentry"/> watcher
/// (recovers Agent B's lost headline item). A single unblinking eye fixed to the rim that projects a long
/// 15-unit sightline into the arena (stopped by walls). It holds that fixed line and does nothing until it
/// gets a clear look at the player crossing it; then its eye snaps wide, it charges for a beat (the delay)
/// and looses one Waggler straight down the beam. Single use.
///
/// WallSentry is abstract (it only provides the beam, the raycast and the charge/fire telegraph); a concrete
/// subclass must decide HOW it aims. The Warden aims at a FIXED bearing — inward toward the room, offset by
/// <see cref="aimAngle"/> — so four authored copies (-45 / -15 / +15 / +45) fan a stretch of rim with four
/// crossing watch-lines. (Distinct from the Beam Trap's instant tripwire: the Warden visibly "wakes",
/// staring the player down through the charge before it fires.)
/// </summary>
public class WardenSentry : WallSentry
{
    [Header("Warden")]
    [Tooltip("Fixed watch bearing as an offset (degrees) from straight-inward. The four authored copies use " +
             "-45 / -15 / +15 / +45 to fan the rim with crossing 15u sightlines.")]
    public float aimAngle = 0f;
    [Tooltip("Continuous clear line-of-sight (seconds) the player must give it before it commits to firing.")]
    public float lockHold = 0.15f;

    SpriteRenderer eye;

    protected override IEnumerator Run()
    {
        if (beam != null) beam.widthMultiplier = beamWidth;
        Vector2 aim = InwardDir(transform.position).Rotated(aimAngle).normalized;   // fixed for life
        transform.up = aim;

        // A dim watching eye sits at the muzzle, idling until it catches the player.
        eye = MakeSprite("Eye", Reticle(), Dormant, 0.7f, order: 7);

        // Hold the watch-line; only commit once we've had a clear look for `lockHold` straight.
        float held = 0f;
        while (held < lockHold)
        {
            bool sees = Scan(transform.position, aim, out Vector2 end);
            DrawBeam(transform.position, end, sees ? Alert : IdlePulse());
            float blink = 0.5f + 0.5f * Mathf.Sin(Time.time * 3f);
            eye.color = sees ? Alert : Color.Lerp(Dormant, Search, blink);
            eye.transform.localScale = Vector3.one * (0.7f + (sees ? 0.25f : 0.05f * blink));
            held = sees ? held + Time.deltaTime : 0f;
            yield return null;
        }

        // Eye snaps wide, then it charges down the fixed beam and fires once.
        eye.color = Alert;
        eye.transform.localScale = Vector3.one * 1.0f;
        yield return StartCoroutine(ChargeAndFire(() => aim));
        Consume(0.35f);
    }
}
