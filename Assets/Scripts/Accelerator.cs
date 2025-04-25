using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Accelerator : Part
{
    [SerializeField] private Sprite[] sprs;
    [SerializeField] private Sprite[] onSprs;
    private float timer = 0f;
    private float secondTimer = 0f;
    [SerializeField] private float maxTime;
    public float force = 200f;
    public static List<Accelerator> accels;
    public bool on = false;
    public Vector2 direction; //used by CS
    [SerializeField] private ParticleSystem ps;
    public void Update()
    {
        if (on && CharacterScript.moving)
        {
            if (!ps.isPlaying)
            {
                ps.Play();
            }
            engagement = 1f;
            timer += Time.deltaTime;
            if (timer > maxTime)
            {
                timer = maxTime;
            }
        }
        else
        {
            if (ps.isPlaying)
            {
                ps.Stop();
            }
            engagement = 0f;
            secondTimer = 0f;
            timer -= Time.deltaTime;
            if (timer < 0f)
            {
                timer = 0f;
            }
        }
        transform.rotation = Quaternion.RotateTowards(transform.rotation,CharacterScript.aimQ,1080f * Time.deltaTime);
        Anim();
    }

    void Anim()
    {
        if (timer >= maxTime)
        {
            secondTimer += Time.deltaTime;
            sr.sprite = onSprs[secondTimer % 0.125f > 0.0625f ? 1 : 0]; //0.0625f is the time it takes to switch between sprites
        }
        else
        {
            sr.sprite = GS.PercentParameter(sprs, timer / maxTime);
        }
    }
    
    public override void StartPart(MechaSuit mecha)
    {
        accels.Add(this);
    }

    public override void StopPart(MechaSuit m)
    {
        base.StopPart(m);
        accels.Remove(this);
    }

}
