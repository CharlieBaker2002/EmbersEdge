using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Constructor : Building
{
    [SerializeField] private Sprite[] sprs;
    [SerializeField] private SpriteRenderer stick;
    [SerializeField] private Transform nose;
    [SerializeField] public EmberStore store;
    [SerializeField] private Ember ember;
    public float radius = 3f;
    [SerializeField] bool visual = true;
    public bool constructing = false;
    public int max = 5;
    [SerializeField] private ParticleSystem ps;
    private ParticleSystem.VelocityOverLifetimeModule mod;

    [SerializeField] private Sprite[] upgradeSpritesBeam; 
    [SerializeField] private Sprite[] upgradeSpritesLarge; 
    [SerializeField] private Sprite[] upgradeicons;
    [SerializeField] private Sprite[] baseSprites;
    public List<Building> tasks;
    
    bool isBeam = false;
    private bool isLarge = false;

    public override void Start()
    {
        base.Start();
        AddUpgradeSlot(new int[] {0,0,0,1},"Long-Range Constructor",upgradeicons[0],true, UpgradeToBeam,5,false,null,() => !isLarge);
        AddUpgradeSlot(new int[] {0,10,0,0},"Heavy Constructor",upgradeicons[1],true, UpgradeToLargeConstructor,5,false,null,() => !isBeam);
    }

    void UpgradeToBeam()
    {
        radius = 6f;
        isBeam = true;
        sprs = upgradeSpritesBeam;
        stick.sprite = sprs[0];
        sr.sprite = baseSprites[0];
    }

    void UpgradeToLargeConstructor()
    {
        store.maxEmber = 15;
        isLarge = true;
        sprs = upgradeSpritesLarge;
        stick.sprite = sprs[0];
        sr.sprite = baseSprites[1];
    }
    
    protected override void BEnable()
    {
        EnergyManager.i.constructors.Add(this);
        GS.OnNewEra += SetMat;
        SpawnManager.instance.onWaveComplete += () =>
        {
            store.maxEmber = max;
        };
        SetMat(0);
        EnergyManager.i.UpdateEmber();
        GridManager.i.RebuildRangeCache();
        GridManager.i.RefreshEnergyCells();
        mod = ps.velocityOverLifetime;
    }

    private void SetMat(int i)
    {
        stick.material = GS.MatByEra(GS.era, false, false, true);
    }

    protected override void BDisable()
    {
        EnergyManager.i.constructors.Remove(this);
        EnergyManager.i.UpdateEmber();
    }

    public void Construct(Building b)
    {
        store.Use(1, true);
        EnergyManager.i.UpdateEmber();
        Vector3 p = b.icons[b.icons.Count - b.numIconsTrue].transform.position;
        constructing = true;
        if (!visual)
        {
            this.QA(() => DoConstruct(b,p),1.31f);
            return;
        }
        stick.transform.LeanRotate(GS.TsTV(stick.transform.position,b.transform.position), 1.3f).setEaseInOutSine();
        stick.LeanAnimate(sprs, 1.3f,true).setOnComplete(() => DoConstruct(b,p));
    }
    void DoConstruct(Building b, Vector3 p)
    {
        var e = Instantiate(ember,nose.position,Quaternion.identity,GS.FindParent(GS.Parent.fx));
        e.to = p;
        e.onComplete = b.RemoveIcon;
        e.onComplete += () => constructing = false;
        mod.yMultiplier = (p - transform.position).magnitude;
        ps.Play();
    }
}
