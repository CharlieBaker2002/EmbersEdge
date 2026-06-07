using System;
using UnityEngine;
using System.Collections;

public class RevealFX : MonoBehaviour
{
    [SerializeField] private ParticleSystem ps;
    private ParticleSystem.EmissionModule em;
    private ParticleSystem.ShapeModule shap;
    [SerializeField] private float scale;
    [SerializeField] private ParticleSystem extra;
    [SerializeField] private ParticleSystemRenderer extraR;
    [SerializeField] private ParticleSystemRenderer render;
    private ParticleSystem.VelocityOverLifetimeModule v;

    private bool controlled = false;
    private float rotLevel;
    
    private IEnumerator Start()
    {
        extraR.material = GS.MatByEra(GS.era, true,false,true);
        render.material = GS.MatByEra(GS.era, true,false,true);
        yield return null;
        em = ps.emission;
        var main = ps.main;
        v = ps.velocityOverLifetime;

        for (float t = 0f; t < 20f; t +=20f * Time.unscaledDeltaTime)
        {
            main.startLifetime = 3f - t * 0.1f;
            main.simulationSpeed = t * 0.1f;
            em.rateOverTime = scale * 60f * t;
            rotLevel = t * 0.05f;
            yield return null;
        }
        rotLevel = 1f;
        main.startLifetime = 1f;
        em.rateOverTime = 1200f;
        main.simulationSpeed = 2f;
        yield return new WaitForSeconds(0.5f);
        Complete();
    }

    public void Complete()
    {
        controlled = true;
        ps.transform.LeanScale(Vector3.zero, 0.75f).setEaseInBack().setIgnoreTimeScale(true).setOnComplete(() => { ps.Stop(); });
        //extra.Play();
        transform.LeanRotate(Vector3.zero, 0.5f).setEaseInOutCirc().setIgnoreTimeScale(true).setOnComplete(() =>
        {
            extra.Play();
        });
        this.QA(() => Shockwave.EEWave(transform.position, 5f), 0.75f);
        this.QA(() => Destroy(gameObject),5f);
    }

    void Update()
    {
        if (!controlled) return;
        //transform.Rotate(new Vector3(1f,0f,0f), Time.deltaTime * 45f * rotLevel);
        transform.Rotate(new Vector3(0f,1f,0f), Time.unscaledDeltaTime * 30f * rotLevel);
        transform.Rotate(new Vector3(0f,0f,1f), Time.unscaledDeltaTime * 15f * rotLevel);
        ps.transform.Rotate(Vector3.forward,120f * Time.unscaledDeltaTime);
        if(rotLevel >= 1f) v.radial = -0.1f * Mathf.Sin(1.5f * Time.time);
    }
}
