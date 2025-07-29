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
    private float storeRad = 0.3f;
    [SerializeField] private Ember ember;
    public float radius = 3f;
    [SerializeField] bool visual = true;
    public bool constructing = false;

    [SerializeField] private Sprite[] upgradeSpritesBeam; 
    [SerializeField] private Sprite[] upgradeSpritesLarge; 
    [SerializeField] private Sprite[] upgradeicons;
    [SerializeField] private Sprite[] baseSprites;
    public List<Building> tasks;
    public EmberConnector connect;
    private int maxStores = 5;

    private Action act;
    
    bool isBeam = false;
    private bool isLarge = false;


    public override void Start()
    {
        maxStores = 5;
        act = RefreshMax;
        EnergyManager.toBeBuilt.Add(this);
        base.Start();
        AddUpgradeSlot(new int[] {0,0,0,1},"Long-Range Constructor",upgradeicons[0],true, UpgradeToBeam,5,false,null,() => !isLarge);
        AddUpgradeSlot(new int[] {0,10,0,0},"Heavy Constructor",upgradeicons[1],true, UpgradeToLargeConstructor,5,false,null,() => !isBeam);
        if (builtYet)
        {
            connect.ember = 5;
            connect.maxEmber = 5;
            // connect.ember = 12;
            // connect.maxEmber = 12;
            // UpgradeToLargeConstructor();
            //UpgradeToBeam();
        }
        RefreshMax();
        GridManager.i.RebuildRangeCache(); 
        GridManager.i.RefreshEnergyCells();
        connect.onRefresh += RefreshStores;
        GS.OnNewEra += SetMat;
    }

    private void RefreshMax()
    {
        connect.maxEmber = maxStores;
        if (maxStores == 12f) //adjusted angle
        {
            for (int i = 0; i < maxStores; i++)
            {
                if(stores.Count > i && stores[i] != null) continue;
                if (stores.Count > i)
                {
                    stores[i] = Instantiate(tinyStore,transform.position + storeRad*GS.ATV3(45f + 360f * i / maxStores),Quaternion.identity,GS.FindParent(GS.Parent.buildings));
                }
                else
                {
                    stores.Add(Instantiate(tinyStore,transform.position + storeRad*GS.ATV3(45f + 360f * i / maxStores),Quaternion.identity,GS.FindParent(GS.Parent.buildings)));
                }
            }
        }
        else
        {
            for (int i = 0; i < maxStores; i++)
            {
                if(stores.Count > i && stores[i] != null) continue;
                if (stores.Count > i)
                {
                    stores[i] = Instantiate(tinyStore,transform.position + storeRad*GS.ATV3(18f + 360f * i / maxStores),Quaternion.identity,GS.FindParent(GS.Parent.buildings));
                }
                else
                {
                    stores.Add(Instantiate(tinyStore,transform.position + storeRad*GS.ATV3(18f + 360f * i / maxStores),Quaternion.identity,GS.FindParent(GS.Parent.buildings)));
                }
            }
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
        storeRad = 0.5f;
        isLarge = true;
        sprs = upgradeSpritesLarge;
        stick.sprite = sprs[0];
        sr.sprite = baseSprites[1];
        RefreshMax();
    }
    
    protected override void BEnable()
    {
        EnergyManager.constructors.Add(this);
        EnergyManager.i.CreateCableConnections();
        SpawnManager.instance.onWaveComplete += act;
        SetMat(0);
        EnergyManager.toBeBuilt.Remove(this);
        GridManager.i.RebuildRangeCache();
        GridManager.i.RefreshEnergyCells();
    }

    public override void OnDestroy()
    {
        if(GS.qutting)return;
        base.Start();
        GridManager.i.RebuildRangeCache();
        GridManager.i.RefreshEnergyCells();
        GS.OnNewEra -= SetMat;
    }
    
    private void RefreshStores()
    {
        int n = connect.ember - stores.Count(x => x.connect.ember > 0);
        if(n==0)return;
        if (n > 0)
        {
            stores.Where(x=>x.connect.ember == 0).Take(n).ToList().ForEach(x =>
            {
                x.connect.ember = 1;
                x.Refresh();
            });
        }
        else
        {
            stores.Where(x=>x.connect.ember == 1).Take(Mathf.Abs(n)).ToList().ForEach(x =>
            {
                x.connect.ember = 0;
                x.Refresh();
            });
        }
    }
    
    private void SetMat(int i)
    {
        stick.material = GS.MatByEra(GS.era, false, false, true);
    }

    protected override void BDisable()
    {
        EnergyManager.constructors.Remove(this);
        EnergyManager.i.CreateCableConnections();
        SpawnManager.instance.onWaveComplete -= act;
    }

    public void Construct(Building b)
    {
        for (int i = stores.Count - 1; i >= 0; i--)
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
        Vector3 p = b.icons[b.icons.Count - b.numIconsTrue].transform.position;
        EnergyManager.i.UpdateEmber();
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
        e.AdjustTrail((p - transform.position).magnitude);
        e.onComplete = b.RemoveIcon;
        e.onComplete += () => constructing = false;
    }
}
