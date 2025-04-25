using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public class Part : MonoBehaviour
{
    public SpriteRenderer sr;
    [Header("Constancy Modifiers")]
    [Tooltip("Never destroy. Lives effectively within main core")]
    public bool innerinner;
    [Tooltip("Permament")]
    public bool permanent;
    [Tooltip("Survives to panic mode")]
    public bool stubborn = false;
    [Tooltip("Doesn't survive teleport")]
    public bool temporary = false;
    public CD cd;
    
    [HideInInspector]
    public string description;

    [HideInInspector]
    public int ring = 0;
    public float engagement;

    [SerializeField] private Part[] series;
    [SerializeField] public int level = 1;
    
    protected Transform e;
    protected Quaternion initRot;

    public enum PartType
    {
        Weapon,
        Ability,
        Boost,
        Automation,
        Defence,
        Kinematic,
        Energy,
        Utility,
        Melee,
        Standalone
    }

    public enum RingClassifier
    {
        Core,
        Powered,
        External,
        Follower
    }
    
    public static RingClassifier Ring(PartType pt)
    {
        return pt switch
        {
            PartType.Utility or PartType.Energy or PartType.Kinematic => RingClassifier.Core,
            PartType.Weapon or PartType.Ability or PartType.Automation => RingClassifier.Powered,
            PartType.Defence or PartType.Melee => RingClassifier.External,
            PartType.Boost or PartType.Standalone => RingClassifier.Follower,
            _ => throw new ArgumentOutOfRangeException(nameof(pt), pt, null)
        };
    }
    
    public RingClassifier Ring()
    {
        return Ring(taip);
    }

    public PartType taip;

    public virtual void StartPart(MechaSuit mecha)
    {
       cd = Instantiate(Resources.Load<GameObject>("CD"), transform.position, Quaternion.identity, GS.FindParent(GS.Parent.fx)).GetComponent<CD>();
       cd.follow = transform;
       cd.SetColour(taip);
    }

    public virtual void StopPart(MechaSuit m)
    {
        
    }

    public virtual void RefreshInteractions(MechaSuit m)
    {
        
    }

    protected void QuickReturn(float returnTime = 2f)
    {
        if (e != null)
        {
            StopCoroutine(nameof(DeployI));
            StartCoroutine(Return(returnTime));
        }
    }
    
    protected Coroutine Deploy(Vector3 pos, float t, float wait = 1f, float returnTime = 2f, Action act = null)
    {
        StopCoroutine(nameof(DeployI));
        return StartCoroutine(DeployI(pos, t, wait, returnTime, act));
    }
 
    IEnumerator DeployI(Vector3 pos, float t, float wait = 1f, float returnTime = 2f, Action act = null)
    {
        returnTime *= 2f;
        if (e == null)
        {
            initRot = transform.localRotation;
            yield return null;
            e = Instantiate(Resources.Load<GameObject>("Empty"), transform.position, transform.rotation,transform.parent).transform;
        }
        transform.parent = null;
        LeanTween.move(gameObject, pos, t).setEaseOutQuint();
        yield return new WaitForSeconds(t + wait);
        yield return StartCoroutine(Return(returnTime));
        act?.Invoke();
    }


    protected IEnumerator Return(float returnTime)
    {
        Vector3 pos = transform.position;
        Quaternion rot = transform.rotation;
    LeanTween.value(gameObject, 0f, 1f, returnTime).setOnUpdate(( float f) =>
    {
        transform.position = Vector3.Lerp(pos, e.position, f);
        transform.rotation = Quaternion.Lerp(rot, e.rotation, f);
    }).setEaseOutQuint();
    yield return new WaitForSeconds(returnTime);
    // Ensure final alignment
    transform.position = e.position;
    transform.rotation = e.rotation;
    transform.parent = e.parent;
    yield return null;
    if (e != null)
    {
        Destroy(e.gameObject);
    }
    else
    {
        Debug.Log(gameObject, gameObject);
        Debug.Break();
    }
    e = null;
    transform.localRotation = initRot;
}

    public virtual bool CanAddThisPart()
    {
        return true;
    }
}
