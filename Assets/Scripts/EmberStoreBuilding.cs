using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EmberStoreBuilding : MonoBehaviour
{
    [SerializeField] private EmberStore store;
    [SerializeField] Renderer r;
    [SerializeField] ParticleSystem ps;
    private List<EmberParticle> particles = new();
    [SerializeField] private EmberParticle particle;
    [SerializeField] private float rad;
    [SerializeField] private float speed;
    [SerializeField] private EmberParticle[] statics;

    private void Start()
    {
        GS.OnNewEra += UpdateEmberColours;
        r.material = GS.MatByEra(GS.era, false, false, true);
        Refresh();
        StartCoroutine(Test());
    }

    IEnumerator Test()
    {
        if (rad == 0.08f)
        {
            yield return new WaitForSeconds(1f);
            for (int x = 0; x <= store.maxEmber; x++)
            {
                store.Set(x);
                Refresh();
                yield return new WaitForSeconds(3f/store.maxEmber);
            }
        }
        else if (rad == 0.14f)
        {
            yield return new WaitForSeconds(4f);
            for (int x = 0; x <= store.maxEmber; x++)
            {
                store.Set(x);
                Refresh();
                yield return new WaitForSeconds(10f/store.maxEmber);
            }
        }
        else
        {
            yield return new WaitForSeconds(14f);
            for (int x = 0; x <= store.maxEmber; x++)
            {
                store.Set(x);
                Refresh();
                yield return new WaitForSeconds(20f/store.maxEmber);
            }
        }
       
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

    private void OnDestroy()
    {
        GS.OnNewEra -= UpdateEmberColours;
    }

    public void Refresh()
    {
        while (store.ember < particles.Count)
        {
            Destroy(particles[0].gameObject);
            particles.RemoveAt(0);
        }

        while (store.ember > particles.Count)
        {
            var p = Instantiate(particle, transform.position, Quaternion.identity, transform);
            p.rad = rad;
            p.speed = speed;
            p.eb = this;
            particles.Add(p);
        }

        var e = ps.emission;
        e.rateOverTime = store.ember * 7.5f;
    }

    public void Hit(Vector2 v)
    {
        float ang = GS.VTA(v);
        statics[Mathf.RoundToInt((statics.Length-1) * ang / 360f)].Light();
    }
}
