using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DestroyAfterTime : MonoBehaviour
{
    public float time = 1f;
    public bool fromStart = true;
    LifeScript ls = null;
    
    public bool destroyOnDisable = false;
    
    bool careful = false;

    void Start()
    {
        if (destroyOnDisable)
        {
            Application.quitting += () => careful = true;
        }
        if(TryGetComponent<LifeScript>(out var l))
        {
            ls = l;
        }
        if (fromStart) { StartCoroutine(DestroyAfterT()); }
    }

    IEnumerator DestroyAfterT()
    {
        yield return new WaitForSeconds(time);
        if (ls != null)
        {
            ls.OnDie();
        }
        Destroy(gameObject);
    }

    public void Destroy()
    {
        StartCoroutine(DestroyAfterT());
    }

    public void DestroyImmediate()
    {
        if (ls != null)
        {
            ls.OnDie();
        }
        Destroy(gameObject);
    }

    public void OnDisable()
    {
        if (careful || !destroyOnDisable)
        {
            return;
        }
        Destroy(gameObject);
    }
}
