using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

public class E1_Boss : Unit, IOnDeath, IRoomUnit
{
    public GameObject homingMissile; //moves in arc towards player.
    public GameObject mine; //near-statinoary projectile that lasts 8 seconds.
    public GameObject spinner;
    public Transform[] shootPoints;
    public GameObject jellyPrefab;
    public ParticleSystem PS;
    public Transform vpPoint;
    public static int E6Count = 0;
    private Room r;

    private int counter = 0;
    
    bool hasMorphed = false;
    bool nextisCannon = false;
    int bopCount = 3;
    Transform cs;

    bool hasDied = false;

    private void Awake()
    {
        ls = GetComponent<LifeScript>();
        anim = GetComponent<Animator>();
        AS = GetComponent<ActionScript>();
        anim.speed = 0.9f;
        E6Count = 0;
    }

    protected override void Start()
    {
        base.Start();
        cs = GS.CS();
        ls.onDamageDelegate += delegate { if (ls.hp < 0.5f * ls.maxHp && hasMorphed == false) { hasMorphed = true; GS.Stat(this,"Stim",15f,1.15f); nextisCannon = true; } }; ;
    }

    public void Bop() //spread mines
    {
        for(int i = UnityEngine.Random.Range(0,2); i < 3; i++)
        {
            GS.NewP(mine, transform, tag, Vector2.zero, 1f, 15 * RandomManager.Rand(1));
        }
        AS.TryAddForce(actRate * 550 * RandomManager.Rand(0,0.1f) * (cs.position - transform.position).normalized, true);
        RandomChange();
    }

    public void MultiShoot()
    {
        anim.SetInteger("State", 0);
        StartCoroutine(IMultiShoot());
    }

 
    IEnumerator IMultiShoot() //shoot missiles
    {
        AS.FaceEnemyOverT(1f, actRate * 7f, cs, false, true);
        yield return new WaitForSeconds(1f);
        foreach (Transform t in shootPoints)
        {
            GS.NewP(homingMissile, t, tag,0f,ActRateProjectileStrength()).GetComponent<ProjectileScript>().father = transform;
        }
        yield return new WaitForSeconds(0.3f);
        if (hasMorphed)
        {
            foreach (Transform t in shootPoints)
            {
                GS.NewP(spinner, t, tag,0,3f + ActRateProjectileStrength()).GetComponent<ProjectileScript>().father = transform;
            }
        }
    }

    public void Spin()
    {
        AS.FaceEnemyOverT(1f, 7f, cs, false, true);
    }

    public void Spawn()
    {
        anim.SetInteger("State", 0);
        StartCoroutine(ISpawn());
    }

    private IEnumerator ISpawn() //make baby jellyfish
    {
        PS.Play();
        int max = hasMorphed ? 7 : 4;
        GS.NewP(spinner, shootPoints[3], tag, 0,ActRateProjectileStrength());
        for (int i = UnityEngine.Random.Range(0, 3); i < max; i++)
        {
            if (r != null)
            {
                r.alives.Add(Instantiate(jellyPrefab, transform.position + (Vector3)UnityEngine.Random.insideUnitCircle, Quaternion.Euler(0, 0, 180 + transform.rotation.eulerAngles.z), GS.FindParent(GS.Parent.enemies)));
            }
            else
            {
                Instantiate(jellyPrefab, transform.position + (Vector3)UnityEngine.Random.insideUnitCircle, Quaternion.Euler(0, 0, 180 + transform.rotation.eulerAngles.z), GS.FindParent(GS.Parent.enemies));
            }
       
            yield return new WaitForSeconds(0.1f);
        }
    }

    public void Cannon() //shoot bare shit
    {
        AS.FaceEnemyOverT(2f, 8f * actRate, cs, false,true);
        anim.SetInteger("State", 0);
        StartCoroutine(ICannon());
    }

    public void FaceUp()
    {
        transform.up = Vector2.up;
    }

    IEnumerator ICannon()
    {
        yield return new WaitForSeconds(0.5f);
        PS.Play();
        float timer = 2f;
        GS.VP(2, transform, vpPoint.position,66.67f);
        while(timer > 0f)
        {
            int rand = UnityEngine.Random.Range(0, 15);
            if(rand < 10)
            {
                GS.NewP(mine, transform, tag, -transform.up, 0.2f, 4 + ActRateProjectileStrength(), 8);
            }
            else if(rand < 13)
            {
                GS.NewP(homingMissile, transform, tag, -transform.up, 0.1f, 3 + ActRateProjectileStrength(), 3);
            }
            else if (rand == 13)
            {
                GS.NewP(spinner, transform, tag, -transform.up, 0,3 + ActRateProjectileStrength(),1);
            }
            else
            {
                if (E6Count < 14)
                {
                    if (r != null)
                    {
                        r.alives.Add(Instantiate(jellyPrefab, transform.position + (Vector3)UnityEngine.Random.insideUnitCircle, Quaternion.Euler(0, 0, 180 + transform.rotation.eulerAngles.z), GS.FindParent(GS.Parent.enemies)));
                    }
                    else
                    {
                        Instantiate(jellyPrefab, transform.position + (Vector3)UnityEngine.Random.insideUnitCircle, Quaternion.Euler(0, 0, 180 + transform.rotation.eulerAngles.z), GS.FindParent(GS.Parent.enemies));
                    }
                }
                else
                {
                    GS.NewP(mine, transform, tag, -transform.up, 0.2f, 4, 8);
                }
            }
            for(int i = UnityEngine.Random.Range(0,2); i < 3; i++)
            {
                yield return new WaitForFixedUpdate();
                timer -= Time.fixedDeltaTime;
            }
        }
    }

    public void RandomChange()
    {
        if(bopCount > 0)
        {
            bopCount--;
        }
        else
        {
            bopCount = UnityEngine.Random.Range(2, 5);
            while (true)
            {
                if (nextisCannon)
                {
                    anim.SetInteger("State", 3);
                    nextisCannon = false;
                    return;
                }
                int a = UnityEngine.Random.Range(0, 4);
                if (a == 3)
                {
                    if (!hasMorphed)
                    {
                        continue;
                    }
                    anim.SetInteger("State", 3); //cannon
                    return;
                }
                else if (a == 2)
                {
                    if (hasMorphed)
                    {
                        if(E6Count > 7)
                        {
                            continue;
                        }
                    }
                    else if(E6Count > 4)
                    {
                        continue;
                    }
                    anim.SetInteger("State", 2); //spawn
                    return;
                }
                else if (a == 1)
                {
                    anim.SetInteger("State", 1); //spread
                    return;
                }
                else if (a == 0)
                {
                    if (counter > 2)
                    {
                        counter = 0;
                        if (hasMorphed)
                        {
                            anim.SetInteger("State", 3);
                        }
                        else
                        {
                            anim.SetInteger("State", 1);
                        }
                        return;
                    }
                    counter++;
                    return;
                }
            }
        }
    }

    public void OnDeath()
    {
        if (!hasDied)
        {
            hasDied = true;
            GS.KillNonAlloc(DM.i.activeRoom.transform.position, 20f, new string[] { "Enemy Units", "Enemy Projectiles" });
            if (GS.era == 0)
            {
                PortalScript.i.DefeatedBoss();
            }
            else
            {
                PortalScript.i.Win();
            }
        }
    }

    public void RecieveRoom(Collider2D bounds, Vector2 pos)
    {
        r = bounds.GetComponent<Room>();
    }
}
