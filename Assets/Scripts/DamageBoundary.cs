using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DamageBoundary : MonoBehaviour
{
    private Dictionary<int,LifeScript> enemies = new();
    [Tooltip("Damage, not change, so positive is damage")]
    public float damage;
    public int damageType;
    public float damageOverT;
    public float damageOverTtime;
    public bool hitImmaterial = false;
    public bool hitProjectiles = false;
    public bool reflectProjectiles = false;
    public float dps;
    public float refreshCollideTimer = 0.5f;
    [Tooltip("~300 for one hit")]
    public float push = 0f;
    [Tooltip("~2 for one hit")]
    public float longPush = 0f;
    public bool charOnly = false;
    public bool EE = false;
    [Tooltip("Inflicts 'damage' parameter on self")] public LifeScript selfHarm;
    
    private void OnTriggerEnter2D(Collider2D coli)
    {
        if (coli.isTrigger || !enabled)
        {
            return;
        }
        if (!charOnly)
        {
            if (coli.CompareTag(GS.EnemyTag(tag)) || EE)
            {
                if (RBNotIn(coli))
                {
                    if (coli.GetComponentInParent<ActionScript>()!= null)
                    {
                        ActionScript AS = coli.GetComponentInParent<ActionScript>();
                        if (!AS.interactive)
                        {
                            return;
                        }
                        if (!hitProjectiles && AS.PS != null)
                        {
                            return;
                        }
                        if (!hitImmaterial && AS.immaterial)
                        {
                            return;
                        }
                        if (AS.PS == true && reflectProjectiles)
                        {
                            AS.PS.Reflect(true);
                            return;
                        }
                        if (push != 0f || longPush != 0f)
                        {
                            if (EE)
                            {
                                AS.TryAddForce(AS.mass * push * -coli.transform.position.normalized/Mathf.Max(0.1f,(transform.position-coli.transform.position).magnitude), false);
                            }
                            else
                            {
                                Vector3 dir = (coli.transform.position - transform.position).normalized;
                                if (push != 0f)  AS.TryAddForce(push * dir, false);
                                if (longPush != 0f)  AS.AddPush(1f, true, longPush * dir);
                            }
                        }
                    }
                    if (coli.GetComponentInParent<LifeScript>()!=null)
                    {
                        LifeScript ls = coli.GetComponentInParent<LifeScript>();
                        if (ls.hasDied)
                        {
                            return;
                        }
                        ls.Change(-damage, damageType);
                        if (selfHarm!=null) { selfHarm.Change(-damage, damageType); }
                        if (damageOverT != 0f)
                        {
                            ls.ChangeOverTime(-damageOverT, damageOverTtime, damageType);
                        }
                        enemies.Add(coli.attachedRigidbody.GetInstanceID(), ls);
                        StartCoroutine(RemoveAfterT(coli.attachedRigidbody.GetInstanceID()));
                    }
                }
            }
        }
        else
        {
            if(coli.name == "Character")
            {
                if (RBNotIn(coli))
                {
                    if (coli.TryGetComponent<ActionScript>(out var AS))
                    {
                        if (!AS.interactive)
                        {
                            return;
                        }
                        if (!hitProjectiles && AS.PS != null)
                        {
                            return;
                        }
                        if (!hitImmaterial && AS.immaterial)
                        {
                            return;
                        }
                        if (push != 0f)
                        {
                            AS.TryAddForce(push * (AS.transform.position - transform.position).normalized, false);
                        }
                    }
                    if (coli.TryGetComponent<LifeScript>(out var ls))
                    {
                        if (ls.hasDied)
                        {
                            return;
                        }
                        ls.Change(-damage, damageType);
                        if (selfHarm!=null) { selfHarm.Change(-damage, damageType); }
                        if (damageOverT != 0f)
                        {
                            ls.ChangeOverTime(-damageOverT, damageOverTtime, damageType);
                        }
                        enemies.Add(coli.attachedRigidbody.GetInstanceID(), ls);
                        StartCoroutine(RemoveAfterT(coli.attachedRigidbody.GetInstanceID()));
                    }
                }
            }
        }
        
    }
 private void OnTriggerStay2D(Collider2D c)
    {
        if (c.isTrigger)
        {
            return;
        }
        if (RBIn(c))
        {
            if (dps != 0)
            {
                if (RefreshManager.twentyFixFrame)
                {
                    enemies[c.attachedRigidbody.GetInstanceID()].Change(-dps * Time.fixedDeltaTime, damageType, false, false, false,false,true);
                }
                else
                {
                    enemies[c.attachedRigidbody.GetInstanceID()].Change(-dps * Time.fixedDeltaTime, damageType, false, false, false,false,false);
                }
            }
        }
        else
        {
            OnTriggerEnter2D(c);
        }
    }
   

    IEnumerator RemoveAfterT(int id)
    {
        yield return new WaitForSeconds(refreshCollideTimer);
        if (enemies.ContainsKey(id))
        {
            enemies.Remove(id);
        }
    }

    private bool RBIn(Collider2D c)
    {
        if(c.attachedRigidbody != null)
        {
            if (enemies.ContainsKey(c.attachedRigidbody.GetInstanceID()))
            {
                return true;
            }
        }
        return false;
    }

    private bool RBNotIn(Collider2D c)
    {
        if (c.attachedRigidbody != null)
        {
            if (!enemies.ContainsKey(c.attachedRigidbody.GetInstanceID()))
            {
                return true;
            }
        }
        return false;
    }

    public void RefreshAttacks()
    {
        StopAllCoroutines();
        enemies.Clear();
    }

    private void OnEnable()
    {
        enemies.Clear();
    }
}