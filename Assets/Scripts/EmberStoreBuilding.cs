using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// An Ember Store — and, since 2026-09-14, a grid SOURCE: each new day it banks as much energy as
/// the ember it holds at that moment (8 ember → 8 energy), uncapped, and buildings in the four
/// cardinal cells round its footprint draw from it like from a generator. Tiered by name:
/// Small 1 / Ember Store 2 / Large 3 — both the sustained rate (energy/s) and the instabuffer
/// (a burst pool a consumer may take in one frame). The tiny store (isTiny) is art only.
/// </summary>
public class EmberStoreBuilding : Building, IEnergyAccumulator
{
    [Header("Energy source (a new day banks energy = ember held)")]
    [Tooltip("Sustained energy/s neighbours may draw. 0 = by tier from the name: Small 1 / Ember Store 2 / Large 3.")]
    public float energyDrawRate = 0f;
    [Tooltip("Burst pool on top of the rate (a whole shot in one frame). 0 = by tier: Small 1 / Ember Store 2 / Large 3.")]
    public float energyInstabuffer = 0f;

    readonly EnergyStore store = new EnergyStore(float.PositiveInfinity, 1f, 1f);
    Action newDay;

    public float Energy => store.Energy;
    public float MaxEnergy => store.MaxEnergy;
    public float DrawRate => store.DrawRate;
    public float MaxDrawThisFrame(float dt) => store.MaxDrawThisFrame(dt);
    public float PeekMaxDraw(float dt) => store.PeekMaxDraw(dt);
    public event Action<float> OnUpdate;
    public event Action OnUse;

    public bool Use(float cost)
    {
        if (!store.Use(cost)) return false;
        OnUpdate?.Invoke(store.Energy);
        OnUse?.Invoke();
        return true;
    }

    public void Add(float amount)
    {
        if (amount <= 0f) return;
        store.Add(amount);
        OnUpdate?.Invoke(store.Energy);
    }

    /// <summary>Small 1 / Ember Store 2 / Large 3 (instances carry a "(Clone)" suffix — StartsWith).</summary>
    int Tier => name.StartsWith("Large") ? 3 : name.StartsWith("Small") ? 1 : 2;

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
            // the battery sizes before base.Start: a pre-built store registers as a source in there
            int tier = Tier;
            if (energyDrawRate <= 0f) energyDrawRate = tier;
            if (energyInstabuffer <= 0f) energyInstabuffer = tier;
            store.Configure(float.PositiveInfinity, energyDrawRate, energyInstabuffer);
            base.Start();
            connect.onRefresh += Refresh;
            newDay = OnNewDay;
            if (SpawnManager.instance != null) SpawnManager.instance.OnNewDay += newDay;
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

    public override void OnDestroy()
    {
        base.OnDestroy();
        if (SpawnManager.instance != null && newDay != null) SpawnManager.instance.OnNewDay -= newDay;
    }

    void Update()
    {
        if (isTiny) return;
        store.Tick(Time.deltaTime);   // reconcile the instabuffer every frame, even when idle
    }

    /// <summary>The day rolled: bank energy equal to the ember held right now (x ember → x energy).</summary>
    void OnNewDay()
    {
        if (!builtYet || connect == null) return;
        int x = Mathf.Max(0, connect.ember);
        if (x <= 0) return;
        Add(x);
        SpawnStrikeRing.Ring(transform.position, 1f, 0.5f * Mathf.Max(size.x, size.y));
    }

    protected override void BEnable()
    {
        EnergyManager.i.RegisterEmberStore(this);
        EnergyManager.i.RegisterSource(this, anchorCell, gridSize);   // a grid source, like a generator
    }

    protected override void BDisable()
    {
        EnergyManager.i.UnregisterEmberStore(this);
        EnergyManager.i.UnregisterSource(this, anchorCell, gridSize);
    }

    protected override void OnRotated(Vector2Int oldAnchor, Vector2Int oldSize)
    {
        if (!builtYet || (oldAnchor == anchorCell && oldSize == gridSize)) return;   // square: a pure spin
        EnergyManager.i?.UnregisterSource(this, oldAnchor, oldSize);
        EnergyManager.i?.RegisterSource(this, anchorCell, gridSize);
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
