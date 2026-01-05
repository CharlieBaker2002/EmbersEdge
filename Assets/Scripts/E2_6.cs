using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class E2_6 : Unit
{
    public Animator[] guns;
    public Transform[] shootPoints;
    public Collider2D[] gunCols;
    public GameObject bosh;
    public Transform boshP;
    public GameObject proj;
    private bool inverted = false;
    private Transform t = null;
    private static readonly int Shoot1 = Animator.StringToHash("Shoot");
    private static readonly int CanShoot = Animator.StringToHash("CanShoot");

    protected override void Start()
    {
        base.Start();
        anim.speed = Random.Range(0.9f, 1.1f);
        defaultAnimSpeed = anim.speed;
    }

    public void Shoot()
    {
        guns[0].SetBool(Shoot1, true);
        guns[1].SetBool(Shoot1, true);
        StartCoroutine(ShootI());
    }

    public void StopShoot()
    {
        guns[0].SetBool(Shoot1, false);
        guns[1].SetBool(Shoot1, false);
    }

    public void Bosh()
    {
        Instantiate(bosh, boshP.position, transform.rotation, GS.FindParent(GS.Parent.enemies));
        gunCols[0].enabled = true;
        gunCols[1].enabled = true;
        sr.sortingLayerName = "Units Higher";
    }

    private IEnumerator ShootI()
    {
        float tBetween = 0.01f;
        int ind = 0;
        yield return new WaitForSeconds(0.2f);
        while(tBetween < 0.3f)
        {
            GS.NewP(proj, shootPoints[ind], tag, 0.1f);
            ind = (ind == 0) ? 1 : 0;
            tBetween += Random.Range(0.015f,0.03f);
            yield return WFAS(tBetween);
        }
    }

    public void FindTarget()
    {
        t = GS.FindNearestEnemy(tag, transform.position, 6.5f, false);
        if (t != null)
        {
            AS.FaceEnemyOverT(anim.GetBool(CanShoot) ? 2.5f : 1.5f,  5f * actRate, t, false,true);
            anim.SetBool(CanShoot, true);
        }
        else
        {
            AS.FaceDirectionOverT(Random.insideUnitCircle, 1.5f, actRate * 5f);
        }
    }

    public void BigVP()
    {
        GS.VP(1, transform, transform.position, 50);
    }

    public void LilVP()
    {
        GS.VP(1, guns[0].transform, guns[0].transform.position, 30);
        GS.VP(1, guns[1].transform, guns[1].transform.position, 30);
    }

    public void JumpUp()
    {
        inverted = false;
        gunCols[0].enabled = false;
        gunCols[1].enabled = false;
        sr.sortingLayerName = "Projectiles";
        GS.Stat(this,"dodging",1.75f);
        AS.AddPush(1f, false, -transform.up * 8f);
        if(t!= null)
        {
            AS.TryAddForce(300f * actRate *  Random.Range(1f,2f) * GS.PutInRange((transform.position - t.position).magnitude,1,10)* GS.Rotated(-transform.up,Random.Range(-20,20f)), true);
        }
        else
        {
            AS.TryAddForce(-transform.up * actRate * Random.Range(300,600), true);
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (inverted) return;
        inverted = true;
        if (!AS.CheckWall(collision.transform)) return;
        if (AS.interactive) return;
        AS.Reflect(collision.GetContact(0).normal, true);
        transform.up = -AS.rb.linearVelocity;
    }

    public override void UpdateActRate()
    {
        base.UpdateActRate();
        if (ls.hasDied) return;
        if (actRate == 0f)
        {
            anim.Play("E2_6Morph",0,0);
        }
    }
}
