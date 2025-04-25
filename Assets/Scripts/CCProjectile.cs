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
