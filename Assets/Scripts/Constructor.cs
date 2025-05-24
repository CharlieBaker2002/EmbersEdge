using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Constructor : Building
{
    [SerializeField] private Sprite[] sprs;
    [SerializeField] private SpriteRenderer stick;
    [SerializeField] private Transform nose;
    [SerializeField] private Ember ember;
    [SerializeField] private Building b;
    public float radius = 3f;
    [SerializeField] bool visual = true;
    
    public void Construct()
    {
        if (visual)
        {
            DoConstruct();
            return;
        }
        stick.transform.LeanRotate(GS.TsTV(stick.transform.position,b.transform.position), 1f).setEaseInOutSine();
        stick.LeanAnimateFPS(sprs, 6,true).setOnComplete(() => DoConstruct());
    }
    void DoConstruct()
    {
        var e = Instantiate(ember,nose.position,Quaternion.identity,GS.FindParent(GS.Parent.fx));
        e.to = b.transform.position;
        e.onComplete = b.RemoveIcon;
    }
}
