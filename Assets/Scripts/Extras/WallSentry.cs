using System.Collections;
using UnityEngine;

/// <summary>
/// Shared behaviour for the wall-mounted sentries (2/3/4): a visible raytraced sightline that stops on
/// walls, line-of-sight player detection, and a single one-second "charge then fire one waggler" volley.
/// Each concrete sentry only decides HOW it aims/moves and what counts as a detection; the beam, the
/// raycast and the charge/fire telegraph live here.
/// </summary>
public abstract class WallSentry : SentryExtra
{
    [Header("Sentry")]
    public GameObject wagglerProjectile;   // reused Waggler shot (E1-2 P)
    public float beamLength = 8f;          // sightline reach (stops sooner if it meets a wall)
    public float fireDelay = 1f;           // charge time once the player is spotted
    public float beamWidth = 0.07f;
    public float projStrength = 0f;        // scales the shot (ProjectileScript.SetValues)

    protected LineRenderer beam;
    protected int charMask;     // just the player
    protected int blockMask;    // anything the sightline can hit / be stopped by
    // Start the raycast a touch ahead of the muzzle so a sentry sitting on the rim never trips on the
    // boundary collider (or its own trigger) at the origin; the drawn beam still starts at the body.
    protected float muzzleOffset = 0.35f;

    protected override void OnReady()
    {
        wallMounted = true;
        charMask = LayerMask.GetMask("Character");
        blockMask = LayerMask.GetMask("Character", "Walls", "Ally Buildings", "Ally Units");
        beam = MakeBeam(beamWidth, Search);
        StartCoroutine(Run());
    }

    protected abstract IEnumerator Run();

    /// <summary>Raycast the sightline from `origin` along `dir`; out `end` is where it stops. Returns
    /// true only when the FIRST thing the beam meets is the player (clear line of sight).</summary>
    protected bool Scan(Vector2 origin, Vector2 dir, out Vector2 end)
    {
        dir = dir.normalized;
        Vector2 o = origin + dir * muzzleOffset;
        RaycastHit2D hit = Physics2D.Raycast(o, dir, beamLength, blockMask);
        if (hit.collider != null)
        {
            end = hit.point;
            return GS.IsInLayerMask(hit.collider.gameObject, charMask);
        }
        end = o + dir * beamLength;
        return false;
    }

    protected void DrawBeam(Vector2 a, Vector2 b, Color c)
    {
        if (beam == null) return;
        beam.SetPosition(0, a);
        beam.SetPosition(1, b);
        SetBeamColor(beam, c);
    }

    /// <summary>One-shot: charge for `fireDelay` (beam reddens/thickens, body brightens), then loose a
    /// single waggler along the live aim. `aim` is sampled each frame so a tracking sentry leads.</summary>
    protected IEnumerator ChargeAndFire(System.Func<Vector2> aim)
    {
        for (float t = 0f; t < fireDelay; t += Time.deltaTime)
        {
            float k = t / fireDelay;
            Vector2 dir = aim().normalized;
            Scan(transform.position, dir, out Vector2 end);
            DrawBeam(transform.position, end, Color.Lerp(Search, Alert, k));
            if (beam != null) beam.widthMultiplier = beamWidth * (1f + 3f * k);
            SetBody(Color.Lerp(Search, Alert, k), 1f + 3f * k);
            transform.localScale = baseScale * (1f + 0.12f * k * Mathf.Sin(t * 30f));
            yield return null;
        }
        transform.localScale = baseScale;

        Vector2 fd = aim().normalized;
        if (wagglerProjectile != null) GS.NewP(wagglerProjectile, transform, TAG, fd, 0f, projStrength);
        Shockwave.Spawn(transform.position, 0.8f, 0.012f, 0.3f);
        // brief recoil flash on the spent beam
        DrawBeam(transform.position, (Vector2)transform.position + fd * beamLength, Color.white);
    }

    // gentle idle breathing for a beam that hasn't found anything yet
    protected Color IdlePulse() => Color.Lerp(Dormant, Search, 0.5f + 0.5f * Mathf.Sin(Time.time * 3f));
}
