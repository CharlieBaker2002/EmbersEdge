using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class E1_4 : Unit, IOnCollide
{
    public Animator engine;
    public Animator body;
    [HideInInspector]
    float timer;
    public Transform[] ts;
    public GameObject p0;
    public GameObject p1;
    Vector2 dir = Vector2.down;
    public GameObject vp;
    public GameObject bigVp;
    public ParticleSystem pfx;
    bool charging = false;

    private void Awake()
    {
        AS = GetComponent<ActionScript>();
    }

    protected override void Start()
    {
        base.Start();
        StartCoroutine(E1_4_Main());
    }

    public void ShootSmall(int ind)
    {
        for(int i = Random.Range(0,4); i < 4; i++)
        {
            GS.NewP(p0, ts[ind], tag, 0.4f, 2*Random.value + ActRateProjectileStrength());
        }
    }

    public void ShootBig()
    {
        GS.NewP(p1, transform,tag, 0.1f, ActRateProjectileStrength());
    }

    private void MakeVps()
    {
        foreach(Transform t in ts)
        {
            if(Random.Range(0,4) == 0)
            {
                Instantiate(vp, t.position, t.rotation, transform);
            }
        }
    }

    IEnumerator E1_4_Main()
    {
        Transform enemy = GS.FindNearestEnemy(tag, transform.position, 6f, false, false);
        AS.FaceEnemyOverT(1.5f,5 * actRate, enemy, true);
        yield return StartCoroutine(WaitForActSeconds(1.5f));
        while (true)
        {
            enemy = GS.FindNearestEnemy(tag, transform.position, 10f, false, false);
            AS.FaceEnemyOverT(2f, 3.5f * actRate, enemy, true);
            yield return new WaitForSeconds(1.25f);
            if (Random.Range(0, 2) == 0)
            {
                Instantiate(bigVp, transform.position + transform.up * 0.5f, transform.rotation, transform);
            }
            engine.SetBool("Ignite", true);
            timer = 2f;
            charging = true;
            while (timer >= 0f)
            {
                dir = transform.up;
                AS.TryAddForce(9 * timer * actRate * dir, true);
                transform.up = Vector2.Lerp(transform.up, dir, Mathf.Min(1, 5 * Time.deltaTime));
                timer -= Time.fixedDeltaTime * actRate;
                yield return new WaitForFixedUpdate();
            }
            charging = false;
            AS.Decelerate(1.5f, 0.935f);
            engine.SetBool("Ignite", false);
            timer = 2.25f;
            StartCoroutine(ShootSpin());
            while(timer > 0f)
            {
                transform.rotation = Quaternion.Euler(0f, 0f, transform.rotation.eulerAngles.z + 280f * Time.deltaTime * actRate);
                timer -= Time.deltaTime * actRate;
                yield return null;
            }
            enemy = GS.FindNearestEnemy(tag, transform.position, 10f, false, false);
            AS.FaceEnemyOverT(1.5f, 5, enemy, true);
            body.SetBool("Charge", true);
            MakeVps();
            yield return StartCoroutine(WaitForActSeconds(1.25f));
        }
    }

    private IEnumerator ShootSpin()
    {
        float c = 0f;
        for (float t = 0f; t < 2.25f; t += Time.fixedDeltaTime)
        {
            yield return new WaitForFixedUpdate();
            c += actRate * 20f;
            if (!GS.Chance(c)) continue;
            c -= 100f;
            ShootSmall(Random.Range(0, 4));
        }
    }
    
    public void OnCollide(Collision2D collision)
    {
        if(charging)
        {
            if (AS.CheckWall(collision.transform))
            {
                timer = -1f;
            }
        }
    }
}
