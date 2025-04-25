
using System;
using UnityEngine;
using Random = UnityEngine.Random;

public class E0_Boss : Unit, IOnDeath
{
    // Start is called before the first frame update

    [SerializeField] private SpriteRenderer[] srs;
    [SerializeField] private Material[] mats; //0 is no border, 1 is border
    private int evolveNum = 0; 
    [SerializeField] Transform[] rotdirs;
    [SerializeField] Transform[] twoDirs;
    [SerializeField] Transform[] threeDirs;
    [SerializeField] private ProjectileScript ps1;
    [SerializeField] private ProjectileScript ps2;
    [SerializeField] private ProjectileScript ps3;
    [SerializeField] private Collider2D[] cols;

    private float counter;

    private float frac = 1f;

    private Vector3 initPos;

    private bool hasDied = false;

    [SerializeField] private LineRenderer lr;
    
    protected override void Start()
    {
        base.Start();
        ls.onDamageDelegate += Evolve;
        initPos = transform.position;
        lr.SetPosition(1,initPos);
    }

    private void FixedUpdate()
    {
        ShootSmall();
        AS.TryAddForce((initPos - transform.position) * (1f + 3.5f*frac), true);
    }

    protected override void Update()
    {
        base.Update();
        lr.SetPosition(0,transform.position);
    }

    void Evolve(float _)
    {
        frac = ls.hp / ls.maxHp;
        WiggleBossProj1.speed = 2f - frac;
        AS.rb.angularVelocity = 60f - 60f*frac;
        if (frac > 0.75f) return;
        SetBorder(frac < 0.4f ? 2 : 1);
        return;

        void SetBorder(int ind) //1 for first evolve, 2 for second evolve.
        {
            if(ind == evolveNum) return;
            ls.dmgsrs[0] = srs[ind];
            anim.SetBool("level" + ind,true);
            GS.Stat(this,"stim",5f,2f);
            evolveNum = ind;
            srs[0].material = mats[0];
            cols[0].enabled = false;
            if (ind == 1)
            {
                cols[1].enabled = true;
                srs[1].material = mats[1];
                sr = srs[1];
                srs[1].transform.localScale = Vector3.zero;
                srs[1].gameObject.SetActive(true);
                LeanTween.scale(srs[1].gameObject, Vector3.one, 1f).setEaseOutBack();
            }
            else
            {
                cols[2].enabled = true;
                cols[1].enabled = false;
                srs[2].material = mats[1];
                srs[1].material = mats[0];
                sr = srs[2];
                srs[2].transform.localScale = Vector3.zero;
                srs[2].gameObject.SetActive(true);
                LeanTween.scale(srs[2].gameObject, Vector3.one, 1f).setEaseOutBack();
            }
        }
    }

    //Fixed Update
    void ShootSmall()
    {
        counter += Time.fixedDeltaTime * 1f * (2f-frac) * actRate * actRate;
        if(counter < 1f) return;
        counter -= 1f;
        GS.NewP(ps1, rotdirs[0],tag,0f,ActRateProjectileStrength());
        GS.NewP(ps1, rotdirs[1],tag,0f,ActRateProjectileStrength());
    }
    
    
    //From animator
    public void ShootMedium()
    {
        foreach(Transform x in twoDirs) GS.NewP(ps2, x,tag,0f,ActRateProjectileStrength());
    }

    
    //From animator
    public void ShootLarge()
    {
        foreach(Transform x in threeDirs) GS.NewP(ps3, x,tag,0f,Mathf.Min(ActRateProjectileStrength(),0f));
    }

    public void OnDeath()
    {
        if (hasDied) return;
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
