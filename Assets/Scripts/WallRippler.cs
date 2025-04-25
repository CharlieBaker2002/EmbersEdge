using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class WallRippler : Part, IOnCollide
{
    [SerializeField] private WallBurst burst;
    private float t;
    [SerializeField] private float refresh = 11f;
    [SerializeField] private Sprite[] sprs;
    
    public override void StartPart(MechaSuit m)
    {
        if (!GS.AS.onCollides.Contains(this))
        {
            GS.AS.onCollides.Add(this);
        }
    }

    public override void StopPart(MechaSuit m)
    {
        GS.AS.onCollides.Remove(this);
    }

    private void Update()
    {  
        t -= Time.deltaTime;
        if (t < 0f)
        {
            engagement = 1f;
        }
    }

    public void OnCollide(Collision2D collision)
    {
        if(t > 0f) return;
        if (collision.collider.CompareTag("Walls") || collision.collider.gameObject.layer == LayerMask.NameToLayer("Ally Buildings") || collision.collider.gameObject.layer == LayerMask.NameToLayer("Walls"))
        {
            Instantiate(burst,collision.GetContact(0).point,Quaternion.identity,GS.FindParent(GS.Parent.fx)).Burst(collision.collider);
            t = refresh;
            StartCoroutine(GS.Animate(sr,sprs,1f));
            this.QA(() => engagement = 0f, 1.5f);
        }
    }
}
