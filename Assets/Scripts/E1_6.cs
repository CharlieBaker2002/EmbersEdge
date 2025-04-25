using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class E1_6 : Unit, IOnCollide, IOnDeath
{
    public GameObject mine;
    float timer = 3f;
    Vector2 direction = new Vector2(0, -1);

    private void Awake()
    {
        direction = transform.up;
        anim = GetComponent<Animator>();
        AS = GetComponent<ActionScript>();
        E1_Boss.E6Count++;
    }
    protected override void Update()
    {
        base.Update();
        transform.up = Vector2.Lerp(transform.up, direction, actRate * Time.deltaTime * 5f);
        if(timer > 0f)
        {
            timer -= Time.deltaTime;
        }
        else
        {
            timer = 3f;
            anim.SetBool("Shoot", true);
        }
    }

    public void Shoot()
    {
        anim.SetBool("Shoot", false);
        for(int i = Random.Range(0,3); i < 3; i++)
        {
            GS.NewP(mine, transform, tag, -transform.up, 1, 0, 2);
        }
    }

    public void Move()
    {
        direction = Vector2.Lerp(direction, Random.insideUnitCircle.normalized, 0.4f);
        AS.TryAddForce(direction * 20f, true);
    }

    public void Fling()
    {
        AS.AddPush(1f, false, direction);
    }

    public void OnCollide(Collision2D collision)
    {
        direction *= -1;
    }

    public void OnDeath()
    {
        E1_Boss.E6Count--;
    }
}
