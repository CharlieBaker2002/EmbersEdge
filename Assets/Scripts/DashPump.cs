using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DashPump : Part
{
    public static List<DashPump> pumps = new();
    [SerializeField] Sprite[] sprites;
    [SerializeField] Sprite off;
    public Transform stick;

    [SerializeField] float dashTime;
    [SerializeField] private float rotSpeed = 0f;
    
    float timer = -9999f;
    public float power;
    private float origPower;
    [SerializeField] bool powerDecreases = false;

    bool acco = false;
    [SerializeField] ParticleSystem ps;
    ParticleSystem.MainModule psM;
    
    [SerializeField] SpriteRenderer str;
    [SerializeField] private Sprite on;
    private bool used = false;

    public override void StartPart(MechaSuit mecha)
    {
        base.StartPart(mecha);
        sr.sprite = on;
        origPower = power;
        pumps.Add(this);
        str.sprite = sprites[0];
        enabled = true;
        stick.gameObject.SetActive(true);
        psM = ps.main;
        engagement = 1f;
        ReawakenPumps();
    }   

    public override void StopPart(MechaSuit m)
    {
        if (!pumps.Contains(this)) return;
        enabled = false;
        str.sprite = sprites[0];
        stick.gameObject.SetActive(false);
        pumps.Remove(this);
        //sr.sprite = noConnectSprite;
    }

    private void Update()
    {
        timer -= Time.deltaTime;
        if (acco)
        {
            if (powerDecreases)
            {
                power = Mathf.Lerp(origPower,0f,1f - Mathf.Pow(timer / dashTime,3));
            }
            if (CharacterScript.moving)
            {
                stick.rotation = Quaternion.RotateTowards(stick.rotation,CharacterScript.directionQ,rotSpeed * Time.deltaTime);
            }
            psM.startSpeed = -0.5f + 0.3f * CharacterScript.CS.AS.rb.velocity.magnitude;
            if (timer <= 0f)
            {
                acco = false;
                str.sprite = off;
            }
            else
            {
               str.sprite = GS.PercentParameter(sprites, 1f - (timer / dashTime));
            }
        }
        else if (!used)
        {
            if (CharacterScript.moving)
            {
                stick.rotation = Quaternion.Slerp(stick.rotation,CharacterScript.directionQ,12f * Time.deltaTime);
            }
        }
    }

    public static int ReawakenPumps()
    {
        int n = 0;
        int index = 0;
        foreach (DashPump p in pumps)
        {
            int val = p.Awaken();
            if (val == 1)
            {
                if (Phasor.phasors.Count > index)
                {
                    Phasor.phasors[index].Reawaken();
                }
                if (Phasor.mitigators.Count > index)
                {
                    Phasor.mitigators[index].Reawaken();
                }
            }
            n += val;
            index++;
        }
        return n;
    }

    int Awaken()
    {
        if (acco) return 0;
        power = origPower;
        used = false;
        str.sprite = sprites[0];
        engagement = 0.5f;
        return 1;
    }

   
    bool Activate()
    {
        if (used) return false;
        if (CharacterScript.moving)
        {
            stick.rotation = CharacterScript.directionQ;
        }
        else return false;
        if (acco) return false;
        
        Phasor.ActivatePhasors(pumps.IndexOf(this));
        used = true;
        engagement = 1f;
        acco = true;
        timer = dashTime;
        psM.startSpeed = -0.5f + 0.3f * CharacterScript.CS.AS.rb.velocity.magnitude;
        ps.Play();
        this.QA(() =>
        {
            ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            engagement = 0f;
            cd.SetValue(Mathf.Max(0.01f,CharacterScript.CS.dashTimer));
        },dashTime + 0.1f);
        return true;
    }

    public static bool ActivatePumps()
    {
        bool any = false;
        foreach (DashPump p in pumps)
        {
            if (p.Activate())
            {
                any = true;
                break;
            }
        }
        if (!any) return false;
        if (CharacterScript.CS.dashTimer <= 0f)
        {
            CharacterScript.CS.dashTimer = CharacterScript.CS.maxDashTimer;
        }
        CharacterScript.CS.rot.omega *= -1f;
        return true;
    }


    public static Vector2 GetPumpValues()
    {
        Vector2 v = Vector2.zero;
        foreach(DashPump p in pumps)
        {
            if (p.acco)
            {
                v += p.power * (Vector2)p.stick.transform.up;
            }
        }
        return v;
    }
    
    public override bool CanAddThisPart()
    {
        return pumps.Count < MechaSuit.level;
    }
}
