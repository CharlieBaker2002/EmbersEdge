using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Omnimove : Part
{
    public static List<Omnimove> omnimoves = new List<Omnimove>();
    public SpriteRenderer[] srs;
    [SerializeField] Sprite[] sprites;

    public float[] timers = new float[4];
    const float fireTime = 0.3f;

    public static bool[] dirBools = new bool[4];

    public override void StartPart(MechaSuit mecha)
    {
        if (!omnimoves.Contains(this)) omnimoves.Add(this);
        enabled = true;
    }

    public override void StopPart(MechaSuit m)
    {
        enabled = false;
        SetDefaultSprites();
        if (omnimoves.Contains(this)) omnimoves.Remove(this);
    }

    private void SetDefaultSprites()
    {
        foreach(SpriteRenderer sr in srs)
        {
            sr.sprite = sprites[0];
        }
    }

    private void Update()
    {
        //update sprites
        for(int i = 0; i < 4; i++)
        {
            if (timers[i] > 0f)
            {
                timers[i] -= Time.deltaTime;
                srs[i].sprite = GS.PercentParameter(sprites, 1f - timers[i] / fireTime);
            }
        }
    }

    public static void Activate(Vector2 v)
    {
        foreach(Omnimove om in omnimoves)
        {
            if (v.y > 0 && dirBools[0] == false)
            {
                om.FireOne(2);
            }
            else if (v.y < 0 && dirBools[2] == false)
            {
                om.FireOne(0);
            }
            if (v.x > 0 && dirBools[3] == false)
            {
                om.FireOne(1);
            }
            else if (v.x < 0 && dirBools[1] == false)
            {
                om.FireOne(3);
            }
        }
        SetDirectionBools(v);
    }

    private void FireOne(int index)
    {
        if (timers[index] <= 0f)
        {
            timers[index] = fireTime;
        }
    }

    public static Vector2 Fire()
    {
        Vector2 vtot = Vector2.zero;

        foreach(Omnimove m in omnimoves)
        {
            for(int i = 0; i < 4; i++)
            {
                if (m.timers[i] > 0f)
                {
                    var v = i switch
                    {
                        0 => new Vector2(0, -1f),
                        1 => new Vector2(1f, 0f),
                        2 => new Vector2(0, 1f),
                        _ => new Vector2(-1f, 0f),
                    };
                    v *= (fireTime - 2 * Mathf.Abs(0.5f * fireTime - m.timers[i]));
                    vtot += v;
                }
            }
        }
        return vtot;
    }

    static void SetDirectionBools(Vector2 v)
    {
        dirBools[0] = v.y > 0;
        dirBools[2] = v.y < 0;
        dirBools[1] = v.x < 0;
        dirBools[3] = v.x > 0;

        if (v == Vector2.zero)
        {
            Array.Clear(dirBools, 0, dirBools.Length);
        }
    }
}
