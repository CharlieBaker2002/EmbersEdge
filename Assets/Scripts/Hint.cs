using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;

public class Hint : MonoBehaviour
{
    public bool doNotDisplay = false;
    public bool shown = false;
    public int timeID;

    private bool shutting = false;
    private CanvasGroup cg;
    
    // private VideoClip[] clips;
    // private VideoPlayer[] vp;
   

    private void OnEnable()
    {
        cg.LeanAlpha(1f, 1f).setEaseInSine();
    }

    public void Shut()
    {
        if(shutting) return;
        SpawnManager.instance.CancelTS(timeID);
        shown = true;
        LeanTween.cancel(gameObject);
        cg.LeanAlpha(0f, 1f).setEaseInSine().setOnComplete(() =>
        {
            shutting = false; gameObject.SetActive(false);
        });
    }
}
