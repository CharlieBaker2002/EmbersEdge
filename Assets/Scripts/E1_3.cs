using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class E1_3 : Unit
{
    public GameObject vulnerablePoint;
    Transform t = null;
    private bool morphed = false;
    private bool notAgain = false;
    private bool launched = false;

    private void Awake()
    {
        anim = GetComponent<Animator>();
        AS = GetComponent<ActionScript>();
    }

    protected override void Start()
    {
        base.Start();
        StartCoroutine(E1_3_Main());
    }

    protected override void Update()
    {
        base.Update();
        if (!morphed)
        {
            transform.up = Vector2.Lerp(transform.up, AS.rb.velocity, 5f * Time.deltaTime * actRate);
        }
    }
    
    IEnumerator E1_3_Main()
    {
        while (true)
        {
            while (t == null)
            {
                if(Random.Range(0,4) <= 2 || notAgain) //25% chance instant again
                {
                    notAgain = false;
                    AS.Stop();
                    transform.up = Random.insideUnitCircle;
                    float x = 3.5f * (1 - RandomManager.Rand(1, new Vector2(0.3f, 1f)));
                    for (float i = x; i > 0f; i -= Time.fixedDeltaTime)
                    {
                        AS.TryAddForce(1.5f * actRate * transform.up, true);
                        yield return new WaitForFixedUpdate();
                    }
                }
                else
                {
                    notAgain = true;
                }
                t = GS.FindNearestEnemy(tag, transform.position, 7.5f, false, false);
            }
            transform.up = (Vector2) (t.position - transform.position);
            morphed = true;
            AS.rb.velocity *= 0.3f;
            anim.SetBool("Morph", true);
            while(anim.GetBool("Morph") == true)
            {
                yield return new WaitForFixedUpdate();
            }
            t = null;
        }
    }

    public void Launch()
    {
        AS.sharpness = 4f;
        GS.Stat(this,"immaterial",1f);
        AS.maxVelocity = 5f;
        AS.TryAddForce(165 * actRate* transform.up, true);
        StartCoroutine(LaunchBool());
    }

    private IEnumerator LaunchBool()
    {
        launched = true;
        yield return new WaitForSeconds(1.5f);
        launched = false;
        AS.sharpness = 2f;
    }

    public void Decelerat()
    {
        StartCoroutine(Decelerator());
    }
    public void SetMorphFalse()
    {
        anim.SetBool("Morph", false);
        morphed = false;
    }

    IEnumerator Decelerator()
    {
        if(Random.Range(0,3) == 0)
        {
            Instantiate(vulnerablePoint, transform.position, transform.rotation, transform);
        }
        for(int i = 0; i < 20; i++) //speed to 0.15 x before over 1.5 secs;
        {
            AS.rb.velocity = AS.rb.velocity.normalized * 0.91f;
            yield return new WaitForSeconds(0.075f);
        }
        AS.maxVelocity = 1.2f;
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (morphed)
        {
            StartCoroutine(ChangeDirection());
        }
    }

    IEnumerator ChangeDirection()
    {
        yield return null;
        yield return new WaitForEndOfFrame();
        if (launched)
        {
            transform.up = AS.rb.velocity;
        }
    }
}
