using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public class Jet : Part
{
    [SerializeField] Sprite[] sprites;
    public static List<Jet> jets = new List<Jet>();
    [HideInInspector] public float timer = 0f;
    [SerializeField] float maxtimer = 0.9f;
    public float strength = 2.5f;
    [HideInInspector] public bool acco;
    public float angleLimit = 45f;
    [SerializeField] public float rotAngle = 0f;
    [SerializeField] public SpriteRenderer baseSR;
    [SerializeField] private float rotSpeed = 90f;

    private Transform cs;
    public override void StartPart(MechaSuit mecha)
    {
        if (!jets.Contains(this)) jets.Add(this);
       
        enabled = true;
    }

    private void Start()
    {
        cs = GS.CS().transform;
    }

    public override void StopPart(MechaSuit m)
    {
        if (!jets.Contains(this)) return;
        jets.Remove(this);
        enabled = false;
        baseSR.sprite = sprites[0];
    }

    private void Update()
    {
        baseSR.transform.rotation = Quaternion.RotateTowards(baseSR.transform.rotation,CharacterScript.directionQ,rotSpeed * Time.deltaTime);
        if (acco)
        {
            timer = Mathf.Min(timer + 3f * Time.deltaTime,maxtimer);
        }
        else
        {
            if(timer <= 0f)
            {
                return;
            }
            timer -= Time.deltaTime;
        }

        float t = timer / maxtimer;
        engagement = t;
        baseSR.sprite = GS.PercentParameter(sprites, t);
   
    }
}
