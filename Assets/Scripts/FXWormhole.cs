using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FXWormhole : MonoBehaviour
{
    [SerializeField] ParticleSystem ps;
    private ParticleSystem.EmissionModule em;
    private ParticleSystem.ShapeModule shap;
    [SerializeField] private float scale;
    [SerializeField] private SpriteRenderer sr;
    [SerializeField] private ParticleSystem extra;
    [SerializeField] private ParticleSystemRenderer extraR;
    [SerializeField] private ParticleSystemRenderer render;

    private ParticleSystem.VelocityOverLifetimeModule v;

    private float rotLevel;

    private bool controlled = true;
    public static FXWormhole i;

    private void Awake()
    {

        extraR.material = GS.MatByEra(GS.era, true);
        render.material = GS.MatByEra(GS.era, true);
        sr.material = GS.MatByEra(GS.era, true);
    }


    IEnumerator Start()
    {
        i = this;
        em = ps.emission;
        var main = ps.main;
        v = ps.velocityOverLifetime;

        for (float t = 0f; t < 20f; t +=4 * Time.deltaTime)
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
    }

    public void Complete()
    {
        controlled = false;
        transform.LeanRotate(Vector3.zero, 1f).setEaseInOutCirc().setOnComplete(() =>
        {
            sr.transform.LeanScale(Vector3.zero, 1.25f).setEaseInOutCirc();
            ps.transform.LeanScale(Vector3.zero, 2f).setEaseInBack().setOnComplete(() => { Destroy(gameObject); });
            extra.Play();
        });
    }

    void Update()
    {
        if (!controlled) return;
        transform.Rotate(new Vector3(1f,0f,0f), Time.deltaTime * 45f * rotLevel);
        transform.Rotate(new Vector3(0f,1f,0f), Time.deltaTime * 30f * rotLevel);
        transform.Rotate(new Vector3(0f,0f,1f), Time.deltaTime * 15f * rotLevel);
        ps.transform.Rotate(Vector3.forward,120f * Time.deltaTime);
        if(rotLevel >= 1f) v.radial = -0.1f * Mathf.Sin(1.5f * Time.time);
    }
}
