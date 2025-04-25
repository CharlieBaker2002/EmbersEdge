using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Copter : Part
{
    public static List<Copter> copters;
    [SerializeField] ParticleSystem ps;
    [SerializeField] private Sprite[] sprs;
    [SerializeField] private Sprite[] load;
    private float timer;
    private bool used = false;
    private bool playing = false;
    public static int coptersAvailable = 0;
    
    public override void StartPart(MechaSuit mecha)
    {   
        base.StartPart(mecha);
        copters.Add(this);
    }
    
    public override void StopPart(MechaSuit mecha)
    {
        base.StopPart(mecha);
        copters.Remove(this);
        ps.Stop();
        engagement = 0f;
    }
    
    public static void Play()
    {
        int ind = coptersAvailable - 1;
        if(ind < 0) return;
        Copter c = copters[ind];
        if(c.used) return;
        
        if (CharacterScript.CS.dashTimer <= 0f)
        {
            CharacterScript.CS.dashTimer = CharacterScript.CS.maxDashTimer;
        }
        coptersAvailable--;
        c.playing = true;
        c.used = true;
        c.ps.Play();
        GS.Stat(CharacterScript.CS, "dodging", 2f);
        GS.Stat(CharacterScript.CS, "slow", 2f);
        c.QA(() =>
        {
            c.Stop();
        }, 2f);
    }

    bool Stop()
    {
        if (!playing) return false;
        playing = false;
        engagement = 0f;
        ps.Stop();
        sr.LeanAnimateFPS(load,12, false, true);
        return true;
    }

    public static void Release()
    {
        bool any = false;
        foreach (Copter c in copters)
        {
            if (c.Stop()) any = true;
        }
        if (!any) return;
        GS.RemStat(CharacterScript.CS, "dodging");
        GS.RemStat(CharacterScript.CS, "slow");
    }

    private void Update()
    {
        if (!playing) return;
        timer += 4f * Time.deltaTime;
        if (timer > 1f)
        {
            timer -= 1f;
        }
        sr.sprite = GS.PercentParameter(sprs, timer);
    }

    public bool Reawaken()
    {
        if (playing) return false;
        engagement = 1f;
        used = false;
        sr.LeanAnimateFPS(load,12);
        return true;
    }
}
