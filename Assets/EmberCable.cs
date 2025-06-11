using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

public class EmberCable : MonoBehaviour
{
    [SerializeField] private SpriteRenderer sr;
    [SerializeField] private Sprite[] sprs;
    [SerializeField] EmberCable nextCable;
    [SerializeField] private EmberCable prevCable;
    [SerializeField] public bool storeInfront;
    [SerializeField] private EmberStore store;
    public bool first = false;
    
    public IEnumerator Animate(bool forwards)
    {
        if (forwards)
        {
            sr.sprite = sprs[0];
            yield return new WaitForFixedUpdate();
            sr.sprite = sprs[1];
            yield return new WaitForFixedUpdate();
            sr.sprite = sprs[2];
            yield return new WaitForFixedUpdate();
            sr.sprite = sprs[3];
        }
        else
        {
            sr.sprite = sprs[2];
            yield return new WaitForFixedUpdate();
            sr.sprite = sprs[1];
            yield return new WaitForFixedUpdate();
            sr.sprite = sprs[0];
            yield return new WaitForFixedUpdate();
            sr.sprite = sprs[3];
        }
        NextAnim(forwards);
    }

    void NextAnim(bool forwards)
    {
        if (nextCable != null && forwards)
        {
            nextCable.StartCoroutine(nextCable.Animate(true));
        }
        else if (prevCable != null && !forwards)
        {
            prevCable.StartCoroutine(prevCable.Animate(false));
        }
        else if (store != null && forwards == storeInfront)
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
            StartCoroutine(Animate(GS.Chance(10)));
        }
    }
}
