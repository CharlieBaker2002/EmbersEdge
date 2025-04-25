using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ConnectorBase : MonoBehaviour
{
    [SerializeField] Sprite[] spritesOn;
    [SerializeField] Sprite[] spritesOff;
    bool on = false;
    [SerializeField] SpriteRenderer sr;
    float t;

    private void Awake()
    {
        sr.sprite = spritesOff[GS.era];
    }

    private void Update()
    {
        t += Time.deltaTime;
        if(t >= 3f)
        {
            t -= 3f;
            on = !on;
            if (on)
            {
                sr.sprite = spritesOn[GS.era];
            }
            else
            {
                sr.sprite = spritesOff[GS.era];
            }
        }
    }
}
