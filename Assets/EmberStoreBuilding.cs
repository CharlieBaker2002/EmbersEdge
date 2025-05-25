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
        for (int x = 0; x < 30; x++)
        {
            store.Set(x);
            Refresh();
            yield return new WaitForSeconds(1f);
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
        e.rateOverTime = store.ember * 5f;
    }

    public void Hit(Vector2 v)
    {
        float ang = GS.VTA(v);
        statics[Mathf.FloorToInt(statics.Length * ang / 360f)].Light();
    }
}
