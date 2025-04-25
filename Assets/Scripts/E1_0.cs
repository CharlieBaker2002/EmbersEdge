using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

public class E1_0 : Unit, IOnCollide, IOnDeath
{
    private Rigidbody2D rb;
    private float timer = 5f;
    public GameObject vp;
    public ProjectileScript proj;
    private Vector2 direction = new Vector2();
    public Transform[] spawnPoints;
    public GameObject dieCopy;
    
    private void Awake()
    {
        if (!transform.InDungeon())
        {
            direction = -transform.position.normalized;
        }
        else
        {
            direction = Random.insideUnitCircle;
        }
        AS = GetComponent<ActionScript>();
        timer = Random.Range(3f, 6f);
        anim = GetComponent<Animator>();
        rb = GetComponent<Rigidbody2D>();
    }

    protected override void Start()
    {
        rb.angularVelocity = 50f * GS.PlusMinus();
    }

    protected override void Update()
    {
        base.Update();
        timer -= Time.deltaTime * actRate;
        AS.TryAddForce(0.5f * actRate * Mathf.Pow(timer, 1.5f) * direction, true);
        if (timer < 0f)
        {
            timer = Random.Range(5f, 9f);
            ShootState();
        }
    }

    public void MakeProjectiles()
    {
        anim.SetBool("Trigger", false);
        direction = Random.insideUnitCircle.normalized * Random.Range(0.6f,1f);
        if (actRate == 0f) return;
        foreach(Transform t in spawnPoints)
        {
            var p = Instantiate(proj, t.position, t.rotation, GS.FindParent(GS.Parent.enemyprojectiles));
            p.SetValues(t.position - transform.position, tag, ActRateProjectileStrength());
        }
    }

    private void ShootState()
    {
        if(anim.GetBool("Trigger") == false)
        {
            AS.Stop();
            rb.angularVelocity = actRate * GS.PlusMinus() * Random.Range(50f, 100f);
            if(Random.Range(0,2) == 0)
            {
                Instantiate(vp, transform.position, transform.rotation, transform);
            }
            anim.SetBool("Trigger", true);
            timer = Random.Range(5f, 8f);
            direction = Vector2.zero;
        }
    }

    public override void UpdateActRate()
    {
        base.UpdateActRate();
        rb.angularVelocity *= actRate;
    }

    public void OnCollide(Collision2D collision)
    {
        if (collision.transform.CompareTag(GS.EnemyTag(tag)))
        {
            ShootState();
        }
    }

    public void OnDeath()
    {
        Instantiate(dieCopy,transform.position,transform.rotation,GS.FindParent(GS.Parent.enemies));
    }
}
