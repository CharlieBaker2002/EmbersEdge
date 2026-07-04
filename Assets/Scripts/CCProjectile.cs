using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CCProjectile : ProjectileScript, IOnCollide
{
    public string CC = "Stun";
    public float val = 0.5f;
    public float val2;

    public string CC2;
    public float val21 = 0.5f;
    public float val22;

    [Tooltip("If > 0, at most this many of these projectiles land an EFFECT within a short rolling " +
             "window — extras still get CONSUMED/deleted on contact but deal no damage or CC. Stops a " +
             "radial burst (e.g. a Sower seed's sparks) from bursting the victim down all at once.")]
    public int burstCap = 0;

    const float BurstWindow = 0.7f;   // a gap longer than this starts a fresh burst
    static int burstHits;             // capped hits landed in the current window (shared: one victim = the player)
    static float burstWindowEnd;
    bool burstCounted;                // this projectile has already been tallied against the cap

    public override void OnCollide(Collision2D coli)
    {
        if (coli.rigidbody != null)
        {
            if (coli.rigidbody.transform == father)
            {
                return;
            }
        }
        if (coli.transform.CompareTag(GS.EnemyTag(tag)))
        {
            var u = coli.rigidbody.GetComponent<Unit>();

            // Burst cap: only the first `burstCap` of these projectiles in a rolling window actually
            // hit; any beyond that are still consumed (deleted) but land no damage and no CC.
            if (burstCap > 0 && !burstCounted)
            {
                burstCounted = true;
                if (Time.time > burstWindowEnd) burstHits = 0;   // gap since last spark → new burst
                burstWindowEnd = Time.time + BurstWindow;
                burstHits++;
                if (burstHits > burstCap)
                {
                    if (lifeScript != null) lifeScript.OnDie(); else DestroyImmed();   // consume, no effect
                    return;
                }
            }

            base.OnCollide(coli);
            if (u == null)
            {
                return;
            }
            GS.Stat(u,CC,val,val2);
            if (CC2 != "")
            {
                GS.Stat(u,CC2,val21,val22);
            }
        }
        else
        {
            base.OnCollide(coli);
        }
    }
}
