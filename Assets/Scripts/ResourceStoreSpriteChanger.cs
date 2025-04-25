using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class ResourceStoreSpriteChanger : Building
{
    public Sprite[] sprites = new Sprite[5];
    private SpriteRenderer spr;
    private OrbMagnet om;
    private int max;
    private Vector2 range;

    private void Awake()
    {
        om = GetComponent<OrbMagnet>();
        max = om.capacity;
        spr = GetComponent<SpriteRenderer>();
        range = new Vector2(0f, 0.2f) * max;
    }

    public override void Start()
    {
        base.Start();
        spr.sprite = sprites[0];
    }

    private void Update()
    {
        float n = om.orbs.Count;
        if(n > range.y || n < range.x)
        {
            float ratio = n / max;
            if(ratio <= 0.2f)
            {
                spr.sprite = sprites[0];
                range = max * new Vector2(0, 0.2f);
            }
            else if (ratio <= 0.4f)
            {
                spr.sprite = sprites[1];
                range = max * new Vector2(0.2f, 0.4f);
            }
            else if (ratio <= 0.6f)
            {
                spr.sprite = sprites[2];
                range = max * new Vector2(0.4f, 0.6f);
            }
            else if (ratio <= 0.8f)
            {
                spr.sprite = sprites[3];
                range = max * new Vector2(0.6f, 0.8f);
            }
            if (ratio > 0.8f)
            {
                spr.sprite = sprites[4];
                range = max * new Vector2(0.8f, 1f);
            }
        }
    }
}
