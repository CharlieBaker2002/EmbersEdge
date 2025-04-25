using System;
using System.Collections;
using UnityEngine.Rendering.Universal;
using UnityEngine;

public class LAMulti : MonoBehaviour, ILA
{
    public float adjustCoef = 2;
    public float[] intensities = new float[] { };
    public float[] minIntensities = new float[] { 0, 0 };
    private float[] ranges;
    public Light2D[] ls;
    public SpriteRenderer[] srs;
    bool active = false;

    private void LateUpdate()
    {
        if (active || Mathf.Max(minIntensities) > 0)
        {
            SpriteCheck();
        }
    }
    
    

    private void Start()
    {
        for(int i = 0; i < intensities.Length; i++)
        {
            intensities[i] *= adjustCoef;
            minIntensities[i] *= adjustCoef;
        }
        ranges = new float[intensities.Length];
        for(int i = 0; i < intensities.Length; i++)
        {
            ranges[i] = intensities[i] - minIntensities[i];
        }
        StartCoroutine(LightBugFix());
    }

    IEnumerator LightBugFix()
    {
        yield return null;
        foreach(Light2D l in ls)
        {
            l.enabled = false;
            l.enabled = true;
        }
    }

    void SpriteCheck()
    {
        for (int i = 0; i < srs.Length; i++)
        {
            if (ls[i].lightCookieSprite != srs[i].sprite)
            {
                ls[i].lightCookieSprite = srs[i].sprite;
            }
        }
    }

    public void OnM()
    {
        StopAllCoroutines();
        SpriteCheck();
        for (int i = 0; i < srs.Length; i++)
        {
            ls[i].intensity = intensities[i];
        }
        active = true;
    }

    public void OffM()
    {
        StopAllCoroutines();
        for (int i = 0; i < srs.Length; i++)
        {
            ls[i].intensity = minIntensities[i];
        }
        active = false;
    }

    public void FadeInQuickM()
    {
        Fade(true, 0.25f);
    }

    public void FadeInM()
    {
        Fade(true, 0.5f);
    }

    public void FadeInSlowM()
    {
        Fade(true, 1f);
    }

    public void FadeOutQuickM()
    {
        Fade(false, 0.25f);
    }

    public void FadeOutM()
    {
        Fade(false, 0.5f);
    }

    public void FadeOutSlowM()
    {
        Fade(false, 1f);
    }

    private void Fade(bool fadeIn, float t)
    {
        SpriteCheck();
        if (fadeIn)
        {
            active = true;
        }
        StartCoroutine(IFade(fadeIn, t));
    }

    private IEnumerator IFade(bool fadeIn, float t)
    {
        float timer = t;
        while (timer > 0f)
        {
            timer -= Time.deltaTime;
            if (fadeIn)
            {
                for (int i = 0; i < srs.Length; i++)
                {
                    ls[i].intensity += ranges[i] * Time.deltaTime / t;
                }
            }
            else
            {
                for (int i = 0; i < srs.Length; i++)
                {
                    ls[i].intensity -= ranges[i] * Time.deltaTime / t;
                    if (ls[i].intensity <= minIntensities[i])
                    {
                        for (int j = 0; j < srs.Length; j++)
                        {
                            ls[i].intensity = minIntensities[i];
                        }
                        active = false;
                        yield break;
                    }
                }
            }
            yield return null;
        }
        for(int i = 0; i < srs.Length; i++)
        {
            ls[i].intensity = fadeIn ? intensities[i] : minIntensities[i];
        }
        if (!fadeIn)
        {
            active = false;
        }
    }

    public void UpdateCoef(float c)
    {
        adjustCoef = c;
    }
}
