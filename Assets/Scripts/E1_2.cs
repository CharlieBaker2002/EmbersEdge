using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class E1_2 : Unit, IOnCollide
{
    private Transform target;
    public GameObject proj;
    public Transform shootP;
    private bool left = true;
    private bool hasMorphed = false;
    public bool shooting = false;

    private void Awake()
    {
        anim = GetComponent<Animator>();
        AS = GetComponent<ActionScript>();
    }

    protected override void Update()
    {
        base.Update();
        if (shooting)
        {
            if (target)
            {
                transform.up = Vector2.Lerp(transform.up,target.position - transform.position,actRate * Time.deltaTime * 3f); 
            }
            else
            {
                transform.up = Vector2.Lerp(transform.up, -AS.rb.velocity, actRate *  Time.deltaTime * 3f);
            }
        }
        else
        {
            transform.up = Vector2.Lerp(transform.up, AS.rb.velocity, actRate * Time.deltaTime * 3f);
        }
    }

    public void TryFindNewTarget()
    {
        target = GS.FindNearestEnemy(tag, transform.position, 7.5f, false, false);
    }

    public void Push()
    {
        Vector2 y;
        if (target != null)
        {
            y = (target.position - transform.position).normalized;
        }
        else if (left)
        {
            y = new Vector2(Random.value, Random.value);
        }
        else
        {
            y = transform.up;
        }
        Vector2 x = Vector3.Cross(y, Vector3.forward);
        x *= left ? -1 : 1;
        AS.AddPush(0.4f, false, actRate * (1.5f * y + Random.Range(0.35f,1f)*x));
        left = !left;
    }

    public void MoveBack()
    {
        GS.VP(1, shootP, shootP.position, 50f);
        AS.sharpness = 2.25f;
        AS.AddPush(0.5f, false, -transform.up * 2f * actRate);
    }

    public void Shoot()
    {
        var p = Instantiate(proj, shootP.position, Quaternion.identity, GS.FindParent(GS.Parent.enemyprojectiles)).GetComponent<ProjectileScript>();
        p.SetValues(transform.up,tag, ActRateProjectileStrength());
        ls.race = 3;
    }

    public void OnCollide(Collision2D c)
    {
        if (c.collider.CompareTag(GS.EnemyTag(tag)))
        {
            if (!hasMorphed)
            {
                if(Random.Range((int)SetM.difficulty,4) == 3)
                {
                    this.QA(() => GS.Stat(this,"Stim",4f,1.4f),2.5f / defaultActRate);
                }
                AS.turniness += 1;
                hasMorphed = true;
                anim.SetBool("Morph", true);
                left = true;
            }
            // else if (c.collider.GetComponent<ProjectileScript>() == null)
            // {
            //     StartCoroutine(MiniFade());
            // }
        }
    }

    // private IEnumerator MiniFade()
    // {
    //     AS.Stop();
    //     AS.Decelerate(1.75f, 0.5f);
    //     yield return new WaitForSeconds(0.5f);
    // }
}
