using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PelterTurret : Building
{
    private static readonly int HasAmmo = Animator.StringToHash("CanRefill");
    private static readonly int HasEnemy = Animator.StringToHash("HasEnemy");
    private static readonly int CanShoot = Animator.StringToHash("CanShoot");
    private static readonly int Thecolor = Shader.PropertyToID("thecolor");
    [SerializeField] private Finder f;
    [SerializeField] private Animator anim;
    [SerializeField] private GameObject[] bullets;
    [SerializeField] private Transform shootPoint;
    [SerializeField] private Transform cannon;
    [SerializeField] private SpriteRenderer anchorSR;

    [SerializeField] private Battery b;
     
    private Transform target;
    private Quaternion lookRot;
    
    private bool fastUpgrade = false;
    bool fastRefresh = false;
    private bool munitionsUpgrade = false;
    
    public override void Start()
    {
        anim.speed = 0.5f;
        base.Start();
        f.OnFound += t =>
        {
            target = t;
            if(!fastRefresh) anim.SetBool(HasEnemy, true);
        };
        f.OnLost += () =>
        {
            target = null;
            anim.SetBool(HasEnemy, false);
        }; 
        lookRot = GS.VTQ(GetNearestEE(transform) - (Vector2)transform.position);
        AddUpgradeSlot(new int[]{30,8,0,0},"Fast Refill",null,true,() =>
        {
            fastUpgrade = true;
            f.refresh = 0.8f;
            GetComponent<SpriteRenderer>().material = ColourManager.AllyMat(1);
            Color cola = sr.material.GetColor(Thecolor);
            Debug.Log(cola);
            sr.material = ColourManager.AllyMat(1, false, cola);
            anchorSR.material = ColourManager.AllyMat(1);
        },3);
        AddUpgradeSlot(new int[] { 75, 0, 2, 0 }, "Bigger Bullet", null, true, () =>
        {
            munitionsUpgrade = true;
            GetComponent<SpriteRenderer>().material = ColourManager.AllyMat(2);
            sr.material = ColourManager.AllyMat(2, false, sr.material.GetColor(Thecolor));
            anchorSR.material = ColourManager.AllyMat(2);
        }, 6,false, null, null,()=>fastUpgrade);
        MapManager.OnUpdateMap += () => lookRot = GS.VTQ(GetNearestEE(transform)-(Vector2)transform.position);
        b.act += e =>
        {
            anim.SetBool(HasAmmo, e > 0.1f);
        };
    }
    
    protected override void BEnable()
    {
        f.engaged = true;
        if (Finder.turretsOn)
        {
            f.enabled = true;
        
        }
        else
        {
            StartCoroutine(TurnOffInASec());
        }
    }

    protected override void BDisable()
    {
        f.engaged = false;
    }

    IEnumerator TurnOffInASec()
    {
        yield return new WaitForSeconds(0.5f);
        if (!Finder.turretsOn)
        {
            f.enabled = false;
        }
    }
    
    public void FastRefill()
    {
        if(!fastUpgrade)return;
        anim.speed = 2.5f;
        fastRefresh = true;
        anim.SetBool(HasEnemy,false);
        this.QA(() =>
        {
            anim.speed = 0.5f;
            fastRefresh = false;
            anim.SetBool(HasEnemy,target != null);
        }, 1.2f);
    }
    
    private void Update()
    {
        if (target != null)
        {
            cannon.transform.rotation = Quaternion.RotateTowards(cannon.transform.rotation,
                GS.VTQ(target.position - cannon.transform.position),270* Time.deltaTime);
        }
        else
        {
            cannon.transform.rotation = Quaternion.Lerp(cannon.transform.rotation,lookRot,0.25f*Time.deltaTime);
        }
    }
    
    public void ShootBig()
    {
        if (target != null)
        {
            if (munitionsUpgrade)
            {
                for (int i = 0; i < 2; i++)
                {
                    var g = Instantiate(bullets[0], shootPoint.position,
                        GS.VTQ(shootPoint.position - transform.position), GS.FindParent(GS.Parent.allyprojectiles));
                    g.GetComponent<Seeking>().target = target;
                    if (i == 0)
                    {
                        g.GetComponent<Rigidbody2D>().velocity =
                            5f * GS.Rotated(target.transform.position - g.transform.position, 17.5f);
                    }
                    else
                    {
                        g.GetComponent<Rigidbody2D>().velocity =
                            5f * GS.Rotated(target.transform.position - g.transform.position, -17.5f);
                    }
                }
    
                var b = Instantiate(bullets[1], shootPoint.position, GS.VTQ(shootPoint.position - transform.position),
                    GS.FindParent(GS.Parent.allyprojectiles));
                b.GetComponent<Seeking>().target = target;
                b.GetComponent<Rigidbody2D>().velocity =
                    6f * (target.transform.position - b.transform.position);
            }
            else
            {
                var g = Instantiate(bullets[0], shootPoint.position,
                    GS.VTQ(shootPoint.position - transform.position), GS.FindParent(GS.Parent.allyprojectiles));
                g.GetComponent<Seeking>().target = target;
                g.GetComponent<Rigidbody2D>().velocity =
                    5f * (target.transform.position - g.transform.position);
            }
            b.Use(0.05f);
        }
    }
    
    public void Shoot()
    {
        if (target != null)
        {
            var g = Instantiate(bullets[0], shootPoint.position,
                GS.VTQ(shootPoint.position - transform.position), GS.FindParent(GS.Parent.allyprojectiles));
            g.GetComponent<Seeking>().target = target;
            g.GetComponent<Rigidbody2D>().velocity =
                5f * (target.transform.position - g.transform.position);
            b.Use(0.025f);
        }
    }
}
