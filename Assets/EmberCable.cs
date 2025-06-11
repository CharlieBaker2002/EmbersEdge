using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EmberCable : MonoBehaviour
{
    [SerializeField] private SpriteRenderer sr;
    [SerializeField] private Sprite[] sprs;
    [SerializeField] EmberCable nextCable;
    [SerializeField] private EmberStore store;
    public bool first = false;
    
    public IEnumerator Animate()
    {
        sr.sprite = sprs[0];
        yield return new WaitForFixedUpdate();
        sr.sprite = sprs[1];
        yield return new WaitForFixedUpdate();
        sr.sprite = sprs[2];
        yield return new WaitForFixedUpdate();
        sr.sprite = sprs[3];
        NextAnim();
    }

    void NextAnim()
    {
        if (nextCable != null)
        {
            nextCable.StartCoroutine(nextCable.Animate());
        }
        else if (store != null)
        {
            store.Set(store.ember + 1);
        }
    }

    public IEnumerator Start()
    {
        if(!first) yield break; 
        while (true)
        {
            yield return new WaitForSeconds(0.5f);
            StartCoroutine(Animate());
        }
    }
}
