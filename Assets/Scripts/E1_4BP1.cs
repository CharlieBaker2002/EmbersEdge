using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class E1_4BP1 : MonoBehaviour, IOnCollide
{
    public GameObject projectile;
    private float timer = 0f;
    private Animator anim;

    private void Awake()
    {
        anim = GetComponent<Animator>();
    }

    private void Update()
    {
        timer -= Time.deltaTime;
        if (timer < 0f)
        {
            GS.NewP(projectile, transform, tag,-transform.up,0f,5*Random.value);
            timer = 0.27f;
        }
    }

    public void ShootInManyDirections() //called on death through death animation
    {
        for (float deg = 0; deg < Mathf.PI * 2; deg += Mathf.PI / 10)
        {
            if(Random.Range(0,2) == 0)
            {
                GS.NewP(projectile, transform, tag, new Vector2(Mathf.Cos(deg), Mathf.Sin(deg)),0.25f,Random.value * 5);
            }
        }
    }

    public void OnCollide(Collision2D collision)
    {
        if (collision.rigidbody.CompareTag(GS.EnemyTag(tag)) && collision.rigidbody.GetComponent<ProjectileScript>()==null)
        {
            anim.SetBool("Boom", true);
        }
    }
}
