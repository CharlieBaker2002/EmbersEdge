using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;

public class DamageOverTime : ProjectileScript
{
    public float damageOverT;
    public float t;
    public bool noKill;

    public override void OnCollide(Collision2D collision)
    {
        if(collision.rigidbody == null)
        {
            return;
        }
        if (collision.rigidbody.CompareTag(GS.EnemyTag(tag)))
        {
            if (collision.rigidbody.TryGetComponent<LifeScript>(out var ls))
            {
                if (Array.IndexOf(enemiesHit,collision.rigidbody.GetInstanceID()) == -1)
                {
                    ls.ChangeOverTime(-damageOverT, t, lifeScript.race, noKill);
                }
            }
        }
        base.OnCollide(collision);
    }
}
