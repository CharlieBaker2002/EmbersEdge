using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class E1_3 : Unit
{
    public GameObject vulnerablePoint;
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
        RunPersistent(E1_3_Main);
    }

    protected override void Update()
    {
        base.Update();
        if (!morphed)
        {
            transform.up = Vector2.Lerp(transform.up, AS.rb.linearVelocity, 5f * Time.deltaTime * actRate);
        }
    }
    
    IEnumerator E1_3_Main()
    {
        while (true)
        {
            Transform launchT = null;   // the objective actually taken for a launch (close + visible)
            while (launchT == null)
            {
                if (!MinePathManager.Decide(this, out _) || target == null)
                {
                    AS.Stop();
                    yield return new WaitForSeconds(0.5f);   // nothing to fight anywhere — hold still
                    continue;
                }
                if(Random.Range(0,4) <= 2 || notAgain) //25% chance instant again
                {
                    notAgain = false;
                    AS.Stop();
                    transform.up = RoamDir();
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
                // re-decide at leg end (no range, no memory — the objective may have shifted while
                // we travelled): the current objective, or the wall on the route when unwilling to
                // detour. The launch itself is a straight charge, so only take it once it's close
                // enough to hit AND actually visible; until then the legs travel toward it.
                if (MinePathManager.Decide(this, out _) && target != null)
                {
                    Vector2 aim = MinePath.AimPoint(target, transform.position);
                    if (Vector2.Distance(aim, transform.position) < 7.5f &&
                        MinePath.LineOfSightWide(transform.position, aim, 0.1f))
                    {
                        launchT = target;
                    }
                }
            }
            transform.up = (Vector2) MinePath.AimPoint(launchT, transform.position) - (Vector2) transform.position;
            morphed = true;
            AS.rb.linearVelocity *= 0.3f;
            anim.SetBool("Morph", true);
            while(anim.GetBool("Morph") == true)
            {
                yield return new WaitForFixedUpdate();
            }
        }
    }

    // Leg heading. TRAVELLING (objective far or unseen): purposeful — follow the route, wobbling
    // at most ~±35° off it, so patrols track their correct path around wall lines. ARRIVED
    // (objective close AND in sight): playful — any direction; the leg-end acquisition takes it.
    Vector2 RoamDir()
    {
        Vector2 rnd = Random.insideUnitCircle.normalized;
        if (rnd == Vector2.zero) rnd = Vector2.up;
        if (!MinePathManager.Decide(this, out Vector2 pathDir) || target == null) return rnd;
        Vector2 aim = MinePath.AimPoint(target, transform.position);
        if (Vector2.Distance(aim, transform.position) < 7.5f &&
            MinePath.LineOfSightWide(transform.position, aim, 0.1f)) return rnd;   // eyes on it, close — dance about
        Vector2 toward = pathDir != Vector2.zero ? pathDir : (aim - (Vector2)transform.position).normalized;
        if (toward == Vector2.zero) return rnd;
        return Quaternion.Euler(0f, 0f, Random.Range(-35f, 35f)) * toward;
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
            AS.rb.linearVelocity = AS.rb.linearVelocity.normalized * 0.91f;
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
            transform.up = AS.rb.linearVelocity;
        }
    }
}
