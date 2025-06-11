using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class E1_1 : Unit
{
    private bool canRetreat = true;
    public GameObject proj;
    public Transform sp; //shoot point
    private float timer = 1.5f;
    private Transform t = null;
    private Vector2 d = new Vector2(0,-1f); //direction
    private Quaternion qSave;
    private float attackRange;
    private Collider2D[] cols;
    int n;
    
    private void Awake()
    {
        cols = new Collider2D[2];
        attackRange = Random.Range(1.25f, 4f);
        anim = GetComponent<Animator>();
        AS = GetComponent<ActionScript>();
        if (!transform.InDungeon())
        {
            transform.up = -((Vector2)transform.position).normalized;
            d = transform.up * 2;
        }
    }

    // Update is called once per frame
    protected override void Update()
    {
        base.Update();
        if(t!= null)
        {
            qSave = transform.rotation;
            transform.up = t.position - transform.position;
            transform.rotation = Quaternion.Euler(0, 0, transform.rotation.eulerAngles.z);
            transform.rotation = Quaternion.Lerp(qSave, transform.rotation, 0.05f * actRate);
            float dist = Vector2.Distance(t.position, transform.position);
            if (dist > attackRange)
            {
                AS.TryAddForce(actRate * 0.04f * (7f - 0.5f * attackRange) * (t.position - transform.position).normalized, true);
            }
            else 
            {
                if (canRetreat)
                {
                    AS.TryAddForce(actRate * -0.07f * (4.5f - attackRange) * (t.position - transform.position).normalized, true);
                }
                else
                {
                    AS.TryAddForce(actRate * -0.02f * (t.position - transform.position).normalized, true);
                }
            }
        }
        else
        {
            transform.rotation = Quaternion.Lerp(transform.rotation,Quaternion.Euler(0f,0f,-Vector2.SignedAngle(d, Vector2.up)),actRate * 0.05f);
            AS.TryAddForce(d * (actRate * 0.1f), true);
        }
        timer -= Time.deltaTime * actRate;
        if(timer < 0f)
        {
            if (t != null)
            {
                if(Vector2.Distance(t.position,transform.position) < attackRange + 1f)
                {
                    canRetreat = false;
                    anim.SetBool("Trigger", true);
                    AS.maxVelocity = 2f;
                    n = 1 + Mathf.FloorToInt(0.1f + 4f* RandomManager.Rand(2));
                    timer = Random.Range(4f, 6f);
                    timer += 2 * (n-1);
                    AS.Decelerate(0.5f, 0.5f);
                    return;
                }
            }
            t = GS.FindEnemy(transform, 5f, GS.searchType.allSearch,cols, t);
            timer = 1f;
            if (t != null) { return; }
            float rot = transform.rotation.eulerAngles.z * Random.Range(0.95238095238f, 1.05f) * Mathf.Deg2Rad;
            d = new Vector2(-Mathf.Sin(rot), Mathf.Cos(rot));
            AS.maxVelocity = 1f;
            AS.Stop();
        }
    }

    public void Shoot()
    {
        AS.Stop();
        canRetreat = true;
        anim.SetBool("Trigger", false);
        StartCoroutine(ShootI());
    }

    IEnumerator ShootI()
    {
        for(int i = 0; i < n; i++)
        {
            AS.TryAddForce(-transform.up * 35f, false);
            var p = Instantiate(proj, sp.position, transform.rotation, GS.FindParent(GS.Parent.enemyprojectiles));
            p.GetComponent<ProjectileScript>().SetValues(sp.position + (Vector3) Random.insideUnitCircle * 0.1f - transform.position, tag);
            yield return new WaitForSeconds(0.25f);
        }
    }

    public void SharpUp()
    {
        AS.sharpness = 3f;
    }

    public void SharpDown()
    {
        AS.sharpness = 1f;
    }
}
