using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using Random = UnityEngine.Random;

public class EmberCable : MonoBehaviour
{
    [SerializeField] private SpriteRenderer sr;
    [SerializeField] private Sprite[] sprs;
    [SerializeField] public EmberCable nextCable;
    [SerializeField] public EmberCable prevCable;
    [SerializeField] public bool endInFront;
    [SerializeField] public EmberConnector end;

    public IEnumerator Animate(bool forwards, List<EmberConnector> chain = null)
    {
        if (forwards)
        {
            sr.sprite = sprs[0];
            sr.sortingOrder = 0;
            yield return new WaitForFixedUpdate();
            sr.sprite = sprs[1];
            sr.sortingOrder = 1;
            yield return new WaitForFixedUpdate();
            sr.sprite = sprs[2];
            sr.sortingOrder = 2;
            yield return new WaitForFixedUpdate();
            sr.sprite = sprs[3];
            sr.sortingOrder = 3;
        }
        else
        {
            sr.sprite = sprs[2];
            sr.sortingOrder = 2;
            yield return new WaitForFixedUpdate();
            sr.sprite = sprs[1];
            sr.sortingOrder = 1;
            yield return new WaitForFixedUpdate();
            sr.sprite = sprs[0];
            sr.sortingOrder = 0;
            yield return new WaitForFixedUpdate();
            sr.sprite = sprs[3];
            sr.sortingOrder = 3;
        }

        NextAnim(forwards, chain);
    }

    void NextAnim(bool forwards, List<EmberConnector> chain)
    {
        if (nextCable != null && forwards)
        {
            nextCable.StartCoroutine(nextCable.Animate(true, null));
        }
        else if (prevCable != null && !forwards)
        {
            prevCable.StartCoroutine(prevCable.Animate(false, null));
        }
        else if (end != null && forwards == endInFront)
        {
            if (chain != null)
            {
                end.Chain(chain);
            }
            else
            {
                end.ember++;
                end.onRefresh.Invoke();
            }
        }
    }
}
