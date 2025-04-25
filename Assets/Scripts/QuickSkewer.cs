using System;
using UnityEngine;
using UnityEngine.Serialization;
using System.Collections.Generic;

public class QuickSkewer : Part
{
    [SerializeField] private TrailRenderer tr;
    [SerializeField] private float damage = 1f;
    [SerializeField] private int typ;
    [SerializeField] private float CD = 7f;
    [SerializeField] private float tim;
    private float timer = -1f;
    [SerializeField] private CircleCollider2D col;
    [SerializeField] private Sprite[] sprs = new Sprite[2];
    [SerializeField] private float energycost;
    private bool canGo = true;
    [SerializeField] private float perUnitCD = 10f;
    [SerializeField] private float quickness = 1f;

    List<(Unit, float)> units = new ();

    private List<Unit> toSkewer = new List<Unit>();

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
        if(tim <= 0f)
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
        else
        {
            t.ls.ChangeOverTime(-damage,tim,typ,false);
        }
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

        for (int i = 0; i < units.Count; i++)
        {
            if (units[i].Item2 + perUnitCD < Time.time)
            {
                units.RemoveAt(i);
                i--;
            }
        }

        if (canGo)
        {
            for (int i = 0; i < toSkewer.Count; i++)
            {
                if (toSkewer[i] != null)
                {
                    Attack(toSkewer[i]);
                    return;
                }
                else
                {
                    toSkewer.RemoveAt(i);
                    i--;
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
                if (anyO(o)) return;
                if (perUnitCD > 0f)
                {
                    units.Add((o, Time.time));
                }
                toSkewer.Add(o);
            }
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.attachedRigidbody == null) return;
        if (other.attachedRigidbody.CompareTag(GS.EnemyTag(tag)))
        {
            var o = other.attachedRigidbody.GetComponent<Unit>();
            int ind = toSkewer.IndexOf(o);
            if (ind != -1)
            {
                toSkewer.RemoveAt(ind);
            }

        }
    }

    bool anyO(Unit u)
    {
        if (perUnitCD <= 0f) return false;
        for(int i = 0; i < units.Count; i++)
        {
            if (units[i].Item1 == u)
            {
                return true;
            }
        }
        return false;
    }
}