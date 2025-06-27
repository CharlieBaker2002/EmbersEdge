using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

public class OrbMagnet : MonoBehaviour
{
    private static readonly int Send = Animator.StringToHash("Send");
    private static readonly int Receive = Animator.StringToHash("Receive");

    public enum OrbType {Task, Pylon, Store}
    public OrbType typ;
    public List<OrbScript> orbs = new List<OrbScript>();
    public int transientOrbs = 0;
    public int n;
    public int orbType = 0;
    public int capacity = 10;
    public Action action;
    public List<OrbMagnet> siblingTs = new List<OrbMagnet>();
    [SerializeField]
    public float demand = 0f;
    [HideInInspector]
    public bool init = false;
    [Header("For Init Pylons")]
    public bool initialMag = false;

    void Start()
    {
        n = 0;
        transientOrbs = 0;
        orbs = new List<OrbScript>();
        demand = 0f;
        if (typ != OrbType.Store)
        {
            ResourceManager.instance.ChangeMagnets(this, true);
        }

        if (typ == OrbType.Pylon)
        {
            ResourceManager.instance.orbCaps[orbType] += capacity;
            ResourceManager.instance.UpdateResourceUI();
        }
    }

    public void SendOrb(OrbMagnet om, bool accelerate, bool consume)
    {
        if (orbs.Count > 0)
        {
            OrbScript o = orbs[0];
            if (accelerate)
            {
                o.state = OrbScript.OrbState.accelerate;
            }
            else
            {
                o.state =  OrbScript.OrbState.decelerate;
            }
            o.transform.parent = om.transform;
            om.transientOrbs++;
            orbs.RemoveAt(0);
            n = orbs.Count + transientOrbs;
            om.n = om.orbs.Count + om.transientOrbs;
            om.StartCoroutine(om.ReceiveOrb(consume, o));
        }
    }

    public void DepositOrb(OrbScript o)
    {
        o.state =  OrbScript.OrbState.deposit;
        o.transform.parent = transform;
        o.transform.position = CharacterScript.CS.transform.position + GS.RandCircle(0.1f,0.3f);
        float thet = GS.FixedAngle(Mathf.PI + Mathf.Atan2(o.transform.localPosition.y, o.transform.localPosition.x), false);
        o.theta = thet;
        o.gameObject.SetActive(true);
        transientOrbs++;
        n = orbs.Count + transientOrbs;
        StartCoroutine(ReceiveOrb(false, o));
    }

    public IEnumerator ReceiveOrb(bool consume, OrbScript orb)
    {
        while (orb.transform.localPosition.sqrMagnitude > 0.2f)
        {
            yield return null;
        }
        
        orbs.Add(orb);
        transientOrbs--;
        if (consume)
        {
            orb.ReturnToPool();
            if (orbs.Count == capacity)
            {
                foreach (OrbMagnet om in siblingTs)
                {
                    if (om.orbs.Count != om.capacity)
                    {
                        yield break;
                    }
                }
                if (init)
                {
                    foreach (Behaviour b in GetComponentsInChildren<Behaviour>(true))
                    {
                        b.enabled = true;
                    }
                }
                foreach (OrbMagnet om in siblingTs)
                {
                    ResourceManager.instance.ChangeMagnets(om, false);
                    Destroy(om);
                }
                action?.Invoke();
                ResourceManager.instance.ChangeMagnets(this, false);
                Destroy(this);
            }
        }
        else
        {
            orb.Hover(typ == OrbType.Store);
        }
        yield return null;
    }

    public void DestroyMagnet()
    {
        StopAllCoroutines();
        ResourceManager.instance.ChangeMagnets(this, false);
        if (typ != OrbType.Task)
        {
            ResourceManager.instance.orbCaps[orbType] -= capacity;
            ResourceManager.instance.UpdateResourceUI();
            ResourceManager.instance.orbs[orbType] -= n;
        }
        else
        {
            ResourceManager.instance.orbs[orbType] += capacity - n;
        }
        Destroy(this);
    }


}
