using System;
using UnityEngine;
using UnityEngine.Serialization;

public class Skewer : Part
{
    [SerializeField] private TrailRenderer tr;
    [SerializeField] private float damage = 1f;
    [SerializeField] private float tim = 4f;
    [SerializeField] private int typ;
    [SerializeField] private float CD = 7f;
    private float timer = -1f;
    [SerializeField] private Collider2D col;
    [SerializeField] private Sprite[] sprs = new Sprite[2];
    [FormerlySerializedAs("energy")] [SerializeField] private float energycost;
    private bool canGo = true;
    [SerializeField] private float quickness = 1f;

    public override void StartPart(MechaSuit mecha)
    {
        engagement = 1f;
        base.StartPart(mecha);
    }

    private void Attack(Unit t)
    {
        canGo = false;
        var dir = t.transform.position - transform.position;
        Deploy(transform.position + dir.normalized * (dir.magnitude + 0.5f), 0.2f, 0.01f, 0.2f / quickness, () => { engagement = 0f;
            tr.enabled = false;
            sr.sprite = sprs[0];
        });
        timer = CD;
        col.enabled = false;
        if (tim > 0f)
        {
            t.ls.ChangeOverTime(-damage,tim,typ,false);
        }
        else
        {
            t.ls.Change(-damage * 0.5f,typ);
            this.QA(() =>
            {
                if (t != null)
                {
                    t.ls.Change(-damage * 0.5f, typ);
                }
            }, 0.15f / quickness);
        }
        this.QA(() =>  cd.SetValue(timer), 1f);
    }

    private void Update()
    {
        timer -= Time.deltaTime;
        if (timer < 0f)
        {
            if (!canGo)
            {
                if (ResourceManager.instance.UseEnergy(-energycost))
                {
                    col.enabled = true;
                    engagement = 1f;
                    tr.enabled = true;
                    sr.sprite = sprs[1];
                    canGo = true;
                }
            }
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!canGo) return;
        if (other.attachedRigidbody == null) return;
        if (other.attachedRigidbody.CompareTag(GS.EnemyTag(tag)))
        {
            var o = other.attachedRigidbody.GetComponent<Unit>();
            if (o != null)
            {
                Attack(o);
            }
        }
    }
}
