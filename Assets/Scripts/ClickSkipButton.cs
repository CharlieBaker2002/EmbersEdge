using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ClickSkipButton : MonoBehaviour, IClickable
{
    public void OnClick()
    {
        if (SpawnManager.eeactive)
        {
            CM.Message("The Ember's Edge is already active!");
            return;
        }
        if (GS.CS().InDungeon())
        {
            CM.Message("Return to base to activate the next wave...");
            return;
        }
        SpawnManager.instance.AccelerateWave(false);
        LeanTween.cancel(gameObject);
        LeanTween.scale(gameObject, new Vector3(1.1f, 1.1f), 0.3f).setOnComplete(() => LeanTween.scale(gameObject, new Vector3(1f, 1f), 0.3f));
    }
}