using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class D0_6 : Unit, IOnDeath, IOnCollide
{
    private Vector2 force;
    private float t;
    [SerializeField] private Sprite[] sprs;
    [SerializeField] private float convertTime = 6f;
    [SerializeField] private float maxTime;
    [SerializeField] private D0_6Continuer dc;
    [SerializeField] private LA l;
    private Transform cs;
    [SerializeField] private Rotator rot;
    private bool lit = false;
    private bool cancelled = false;
    
    protected override void Start()
    {
        inDungeon = transform.InDungeon();
        if (inDungeon)
        {
            cs = GS.CS();
            convertTime = 5f;
            maxTime = 13f;
        }
        base.Start();
        ShieldUtility.DecayInShield(this,3f,maxTime,999f);
    }
    
    private void FixedUpdate()
    {
        base.Update();
        t += Time.deltaTime * actRate;
        float p = 0.5f + 0.1f * Mathf.Clamp(t,0f,maxTime);
        AS.mass = p;
        transform.localScale = p * Vector3.one;
        sr.sprite = GS.PercentParameter(sprs, 1f - t / maxTime);
        float fear = actRate * Mathf.Clamp(convertTime - t,-maxTime,maxTime);
        if (lit == false && fear < 0f)
        {
            lit = true;
            l.FadeInSlow();
        }
        rot.omega = 30f * fear;
        AS.TryAddForce(fear*(!inDungeon ? transform.position.normalized : -(cs.position - transform.position).normalized), true);
    }
    
    public void OnDeath()
    {
        var x = Instantiate(dc, transform.position, transform.rotation, GS.FindParent(GS.Parent.enemies));
        x.transform.localScale = transform.localScale;
        x.Do(Mathf.Clamp01(actRate * t / maxTime), maxTime, rot.omega, sr.sprite, cancelled || t < convertTime);
    }

    public void OnCollide(Collision2D collision)
    {
        if (!(t > convertTime)) return;
        if (!collision.transform.CompareTag(GS.EnemyTag(tag))) return;
        if (collision.gameObject.layer == LayerMask.NameToLayer("Ally Projectiles")) return;
        Debug.Log(LayerMask.LayerToName(collision.gameObject.layer));
        OnDeath();
        Destroy(gameObject);
    }

    public override void UpdateActRate()
    {
        base.UpdateActRate();
        if (actRate == 0f && t > convertTime)
        {
            cancelled = true;
            this.QA(() =>
            {
                OnDeath();
                Destroy(gameObject);
            },1f);
            
        }
    }
}
