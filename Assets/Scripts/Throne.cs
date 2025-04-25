using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Throne : Building, IOnDeath
{
    public override void Start()
    {
        if (RefreshManager.i.ARENAMODE)
        {
            return;
        }
        base.Start();
    }

    public override void OnDeath()
    {
        if (!TutorialManager.tutorial)
        {
            if (RefreshManager.i.LOSSPROTECTION)
            {
                Debug.LogWarning("loss protection");
            }
            else
            {
                PortalScript.i.Lose();
            }
        }
        else
        {
            BuildingTutorial.defeated = true;
        }
    }
}
