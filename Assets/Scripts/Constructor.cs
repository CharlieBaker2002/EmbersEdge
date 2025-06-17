using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.Serialization;

public class Constructor : Building
{
    [SerializeField] private Sprite[] sprs;
    [SerializeField] private SpriteRenderer stick;
    [SerializeField] private Transform nose; 
    public List<EmberStoreBuilding> stores;
    [SerializeField] private EmberStoreBuilding tinyStore;
    private float storeRad = 0.5f;
    [SerializeField] private Ember ember;
    public float radius = 3f;
    [SerializeField] bool visual = true;
    public bool constructing = false;
    [SerializeField] private ParticleSystem ps;
    private ParticleSystem.VelocityOverLifetimeModule mod;

    [SerializeField] private Sprite[] upgradeSpritesBeam; 
    [SerializeField] private Sprite[] upgradeSpritesLarge; 
    [SerializeField] private Sprite[] upgradeicons;
    [SerializeField] private Sprite[] baseSprites;
    public List<Building> tasks;
    public EmberConnector connect;
    private int maxStores = 4;

    private Action act;
    
    bool isBeam = false;
    private bool isLarge = false;

    public override void Start()
    {
        base.Start();
        act = RefreshMax;
        AddUpgradeSlot(new int[] {0,0,0,1},"Long-Range Constructor",upgradeicons[0],true, UpgradeToBeam,5,false,null,() => !isLarge);
        AddUpgradeSlot(new int[] {0,10,0,0},"Heavy Constructor",upgradeicons[1],true, UpgradeToLargeConstructor,5,false,null,() => !isBeam);
        if (builtYet)
        {
            connect.ember = 4;
            connect.maxEmber = 4;
           RefreshMax();
        }

        connect.onRefresh += RefreshStores;
    }

    private void RefreshMax()
    {
        connect.maxEmber = maxStores;
        for (int i = 0; i < maxStores; i++)
        {
            if(stores.Count > i) continue;
            stores.Add(Instantiate(tinyStore,transform.position + storeRad*GS.ATV3(45f + 360f * i / maxStores),Quaternion.identity,GS.FindParent(GS.Parent.buildings)));
        }
        RefreshStores();
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
        for (int i = 0; i < stores.Count; i++)
        {
            Destroy(stores[i].gameObject);
            stores.RemoveAt(i);
            i--;
        }
        connect.maxEmber = 12;
        maxStores = 12;
        storeRad = 0.75f;
        isLarge = true;
        sprs = upgradeSpritesLarge;
        stick.sprite = sprs[0];
        sr.sprite = baseSprites[1];
        RefreshMax();
    }
    
    protected override void BEnable()
    {
        EnergyManager.i.constructors.Add(this);
        EnergyManager.i.CreateCableConnections();
        GS.OnNewEra += SetMat;
        SpawnManager.instance.onWaveComplete += act;
        SetMat(0);
        GridManager.i.RebuildRangeCache();
        GridManager.i.RefreshEnergyCells();
        mod = ps.velocityOverLifetime;
    }
    
    private void RefreshStores()
    {
        int n = connect.ember - stores.Count(x => x.connect.ember > 0);
        stores.Where(x=>x.connect.ember == 0).Take(n).ToList().ForEach(x =>
        {
            x.connect.ember = 1;
            x.Refresh();
        });
    }
    
    private void SetMat(int i)
    {
        stick.material = GS.MatByEra(GS.era, false, false, true);
    }

    protected override void BDisable()
    {
        EnergyManager.i.constructors.Remove(this);
        EnergyManager.i.CreateCableConnections();
        SpawnManager.instance.onWaveComplete -= act;
    }

    public void Construct(Building b)
    {
        for (int i = 0; i < stores.Count; i++)
        {
            if (stores[i].connect.ember > 0)
            {
                stores[i].Fracture(transform.position);
                stores[i] = null;
                stores.RemoveAt(i);
                break;
            }
        }
        connect.ember--;
        connect.maxEmber--;
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
