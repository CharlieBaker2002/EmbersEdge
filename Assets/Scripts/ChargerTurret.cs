using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ChargerTurret : Building
{
    private static readonly int Level = Animator.StringToHash("Level");
    private static readonly int Charge = Animator.StringToHash("Charge");
    private static readonly int DeCharge = Animator.StringToHash("DeCharge");
    private static readonly int Attack = Animator.StringToHash("Attack");
    public GameObject p;
    [SerializeField]
    Finder find;
    [SerializeField]
    Animator anim;
    private int n = 13;
    private int level = 1;
    private Transform T;
    private bool shooting = false;
    private float interPWait = 0.12f;
    private float wait = -100f;
    private float waitRefresh = 5f;
    private bool canActivate = true;
    [SerializeField] Sprite tileSprite;
    [SerializeField] private Sprite morphSprite;
    [SerializeField] private Sprite upgradeBaseSprite;
    [Tooltip("Maximum locally-buffered energy. Bursts drain it; a background coroutine refills via Power.DrawEnergy at the adjacent pads' combined drawRate.")]
    [SerializeField] private float bufferMax = 1f;
    private float energyBuffer;
    private Coroutine refillCo;

    private void Update()
    {
        if(wait > Time.deltaTime)
        {
            wait -= Time.deltaTime;
        }
        else if(wait != -100f)
        {
            wait = -100f;
            find.enabled = true;
            canActivate = true;
        }
    }

    protected override void BEnable()
    {
        find.engaged = true;
        if (refillCo == null) refillCo = StartCoroutine(RefillBuffer());
    }

    protected override void BDisable()
    {
        find.engaged = false;
        if (refillCo != null) { StopCoroutine(refillCo); refillCo = null; }
    }

    IEnumerator RefillBuffer()
    {
        while (true)
        {
            float needed = bufferMax - energyBuffer;
            if (needed > 1e-4f && Power.Energy > 0f)
            {
                // DrawEnergy yields each frame at the rate cap; we add to the buffer in lockstep.
                float startRemaining = needed;
                float drawn = 0f;
                while (drawn < startRemaining - 1e-4f && energyBuffer < bufferMax)
                {
                    float rate = Power.DrawRate;
                    float available = Power.Energy;
                    float step = Mathf.Min(startRemaining - drawn, rate * Time.deltaTime, available, bufferMax - energyBuffer);
                    if (step > 0f && Power.Use(step))
                    {
                        drawn += step;
                        energyBuffer += step;
                    }
                    yield return null;
                }
            }
            else
            {
                yield return null;
            }
        }
    }

    public override void OnDeath()
    {
        transform.parent.GetComponent<SpriteRenderer>().color = new Color(200, 200, 200, 1);
        base.OnDeath();
    }

    public void LevelUp()
    {
        anim.SetFloat(Level, 1f); //anim level is one less than script level
        level++;
        n = 39;
        find.UpdateRadius(8f);
        find.refresh /= 2f;
        interPWait = 0.035f;
        waitRefresh = 2.8f;
        transform.parent.GetComponent<SpriteRenderer>().sprite = upgradeBaseSprite;
    }

    public IEnumerator Shoot()
    {
        int x = 0;
        for(float i = 0; i < Mathf.RoundToInt(Random.Range(n * 0.75f, n)); i++)
        {
            float cost = level == 1 ? 0.01f : 0.015f;
            // Consume from the buffer the background RefillBuffer coroutine maintains —
            // attack loop never waits on Power directly, so it can keep firing while
            // batteries refill in parallel.
            if (energyBuffer < cost) yield break;
            energyBuffer -= cost;
            x++;
            if(level == 1)
            {
                GS.NewP(p, transform, tag, 1.8f * (1.2f - (i / n)), 7 * (1 - (i / n)), 3f).transform.localScale =
                    Vector3.one * Random.Range(0.85f, 1.15f);
                yield return new WaitForSeconds(interPWait);
            }
            else
            {
                GS.NewP(p, transform, tag, 2.4f * (1.2f - (i / n)), 24 * (1 - (i / n)), 6f).transform.localScale =
                    Vector3.one * Random.Range(1f, 1.3334f);
                yield return new WaitForSeconds(interPWait);
            }
        }
        find.enabled = false;
        wait = waitRefresh;
    }

    public override void Start()
    {
        base.Start();
        find.OnFound += GetTarget;
        transform.parent.GetComponent<SpriteRenderer>().color = Color.white;
        AddUpgradeSlot(new int[] { 100, 0, 5, 0 }, "Thick Spreader", tileSprite, true, LevelUp, 11,true,CallMorph);
    }

    public void CallMorph()
    {
        GS.QuickMorphWithOrbs(gameObject, morphSprite, transform.parent);
    }

    private void GetTarget(Transform x)
    {
        if (shooting)
        {
            return;
        }
        if(T == null || (T.position - transform.position).sqrMagnitude > find.radius * find.radius)
        {
            T = x;
        }
        if (T != null && energyBuffer > 0.015f && canActivate)
        {
            anim.SetBool(Charge, true);
            canActivate = false;
        }
    }

    public IEnumerator PrepareToShoot()
    {
        for (float i = Random.Range(1.5f,4f- level); i >= 0f; i -= Time.fixedDeltaTime)
        {
            if(T!= null)
            {
                transform.up = Vector2.Lerp(transform.up,(Vector2)(T.position - transform.position).normalized,Time.fixedDeltaTime * level * (3f-i));
            }
            yield return new WaitForFixedUpdate();
        }
        if(T== null)
        {
            anim.SetBool(DeCharge, true);
            canActivate = true;
            yield break;
        }
        else
        {
            shooting = true;
            anim.SetBool(Attack, true);
        }
        for (float i = 1.5f; i >= 0f; i -= Time.fixedDeltaTime)
        {
            if (T != null)
            {
                transform.up = Vector2.Lerp(transform.up, (Vector2)(T.position - transform.position).normalized, Time.fixedDeltaTime * level * 2 * (3.5f-i));
            }
            yield return new WaitForFixedUpdate();
        }
        shooting = false;
    }
}
