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

    public List<Building> tasks;
    
    public override void Start()
    {
        base.Start();
        EnergyManager.i.constructors.Add(this);
        GS.OnNewEra += SetMat;
        SetMat(0);
    }

    private void SetMat(int i)
    {
        stick.material = GS.MatByEra(GS.era, false, false, true);
    }
    
    public void Construct(Building b)
    {
        store.Use(1, true);
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
    }
}
