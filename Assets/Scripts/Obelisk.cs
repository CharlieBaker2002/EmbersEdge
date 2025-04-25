using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class Obelisk : MonoBehaviour, IOnDeath
{
    public ParticleSystem p;
    ParticleSystem.EmissionModule ps;
    public Light2D l;
    public Rigidbody2D rb;
    public int[] orbs;

    bool upd = false;

    private Action<int> kill;

    private void Start()
    {
        kill = _ => { Destroy(gameObject); };
        GS.OnNewEra += kill;
    }

    public void OnDeath()
    {
        upd = true;
        ps = p.emission;
        l.transform.parent = GS.FindParent(GS.Parent.misc);
        p.transform.parent = GS.FindParent(GS.Parent.misc);
        GS.QA(this,() => transform.LeanScale(Vector3.zero, 2.5f).setEaseInElastic().setOnComplete(OnShrink),2f);
        rb.constraints = RigidbodyConstraints2D.FreezePosition;
        GS.OnNewEra -= kill;
    }

    private void Update()
    {
        if (upd)
        {
            rb.angularVelocity += 90f * Time.deltaTime;
        }
    }


    void OnShrink()
    {
        ps.enabled = true;
        p.Play();
        StartCoroutine(Delight());
        GS.QA(this,() => GS.CallSpawnOrbs(transform.position,orbs),0.7f);
    }

    IEnumerator Delight()
    {
        while (ps.rateOverTime.constant > 0.75f)
        {
            l.intensity = Mathf.Lerp(l.intensity, 0f, Time.deltaTime * 3f);
            ps.rateOverTime = Mathf.Lerp(ps.rateOverTime.constant, 0f, Time.deltaTime * 0.65f);
            yield return null;
        }
        ps.enabled = false;
        Destroy(p.gameObject);
        Destroy(gameObject);
        Destroy(l.gameObject);
    }
}
