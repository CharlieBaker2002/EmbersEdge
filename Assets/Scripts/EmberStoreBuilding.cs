using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

public class EmberStoreBuilding : Building
{
    [SerializeField] Renderer r;
    [SerializeField] ParticleSystem ps;
    private List<EmberParticle> particles = new();
    [SerializeField] private EmberParticle particle;
    [SerializeField] private float rad;
    [SerializeField] private float speed;
    [SerializeField] private EmberParticle[] statics;
    [SerializeField] private SpriteRenderer[] fractureFX;
    [SerializeField] private Sprite[] fracsprs;
    [SerializeField] private GameObject boomFx;
    [SerializeField] private ParticleSystemRenderer psr;
    
    [SerializeField] bool isTiny = false;
    public EmberConnector connect;

    public override void Start()
    {
        if (!isTiny)
        {
            base.Start();
            connect.onRefresh += Refresh;
        }
        else
        {
            transform.localScale = Vector3.zero;
            LeanTween.scale(gameObject,Vector3.one,1f).setEase(LeanTweenType.easeInOutQuad);
        }
        GS.OnNewEra += UpdateEmberColours;
        r.material = GS.MatByEra(GS.era, false, false, true);
        Refresh();
    }
    
    protected override void BEnable()
    {
        EnergyManager.i.emberStores.Add(this);
        EnergyManager.i.CreateCableConnections();
    }
    
    protected override void BDisable()
    {
        EnergyManager.i.emberStores.Remove(this);
        EnergyManager.i.CreateCableConnections();
    }


    void UpdateEmberColours(int era)
    {
        r.material = GS.MatByEra(GS.era, false, false, true);
        foreach(EmberParticle p in particles)
        {
            p.sr.material = GS.MatByEra(GS.era, true, false, true);
        }
        foreach(EmberParticle p in statics)
        {
            p.sr.material = GS.MatByEra(GS.era, true, false, true);
        }
    }

    public void Refresh()
    {
        while (Mathf.Max(0,connect.ember) < particles.Count)
        {
            if (particles[0] != null)
            {
                Destroy(particles[0].gameObject);
            }
            particles.RemoveAt(0);
        }

        while (connect.ember > particles.Count)
        {
            var p = Instantiate(particle, transform.position, Quaternion.identity, transform);
            p.rad = rad;
            p.speed = speed;
            p.eb = this;
            particles.Add(p);
        }

        var e = ps.emission;
        e.rateOverTime = connect.ember * 7.5f;
    }

    public void Hit(Vector2 v)
    {
        float ang = GS.VTA(v);
        statics[Mathf.RoundToInt((statics.Length-1) * ang / 360f)].Light();
    }

    public void Fracture(Vector3 p)
    {
        if (GS.era != 0)
        {
            psr.material = GS.MatByEra(GS.era, true, false, true);
        }
        particles[0].enabled = false;
        sr.enabled = false;
        foreach (EmberParticle z in statics)
        {
            z.gameObject.SetActive(false);
        }
        LeanTween.move(particles[0].gameObject, p, 0.5f).setEaseInBack();
        boomFx.SetActive(true);
        for (int i = 0; i < 4; i++)
        {
            var g = fractureFX[i];
            g.material = GS.MatByEra(GS.era, true, false, true);
            g.gameObject.SetActive(true);
            g.gameObject.LeanMove(transform.position + GS.ATV3(45f + i * 90f * Random.Range(-10f,10f)), 1f).setEaseOutSine();
            g.gameObject.LeanScale(Vector3.zero, 1f).setEaseInSine();
            g.gameObject.LeanDelayedCall(1f, () => Destroy(g));
            g.LeanAnimate(fracsprs, 1f);
        }
        LeanTween.delayedCall(1.4f,() => Destroy(gameObject));
    }
}
