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

    public List<EmberConnector> job = null;
    public static List<List<EmberConnector>> lostjobs;
    private float timer;
    public bool waitForDeac = false;

    private void Awake()
    {
        if (GS.era != 0)
        {
            UpdateColour(GS.era);
        }
    }

    void UpdateColour(int era)
    {
        sr.material = GS.MatByEra(era, true, false, true);
    }

    public IEnumerator Animate(bool forwards, List<EmberConnector> chain)
    {
        timer = 0.25f;
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

    private void Update()
    {
        timer -= Time.deltaTime;
        sr.color = new Color(1f, 1f, 1f, timer * 3.4f + 0.15f);
        if (timer < 0f)
        {
            timer = 0f;
            if (!waitForDeac)
            {
                enabled = false;
            }
        }
    }

    void NextAnim(bool forwards, List<EmberConnector> chain)
    {
        job = null;
        waitForDeac = false;
        if (nextCable != null && forwards)
        {
            GS.CopyList(ref nextCable.job, chain);
            nextCable.waitForDeac = true;
            nextCable.enabled = true;
            nextCable.StartCoroutine(nextCable.Animate(true, chain));
        }
        else if (prevCable != null && !forwards)
        {
            GS.CopyList(ref prevCable.job, chain);
            prevCable.waitForDeac = true;
            prevCable.enabled = true;
            prevCable.StartCoroutine(prevCable.Animate(false, chain));
        }
        else if (end != null && forwards == endInFront)
        {
            if (chain[^1] != end)
            {
                end.ember++;
                end.emberTravel--;
                end.Chain(chain);
            }
            else
            {
                end.ember++;
                end.emberTravel--;
                end.onRefresh?.Invoke();
            }
        }
    }

    public void OnDestroy()
    {
        if(job!=null && job.Count > 0) lostjobs.Add(job);
    }
}
