using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class LA : MonoBehaviour, ILA //Lighting Animator
{
    public float adjustCoef = 2f;
    public float maxIntensity = 1.25f;
    public float minIntensity = 0f;
    private float range;
    public Light2D l;
    SpriteRenderer sr;
    Sprite spr;
    bool active = false;

    private void Awake()
    {
        sr = GetComponentInChildren<SpriteRenderer>();
        spr = sr.sprite;
        active = false;
    }

    private void Start()
    {
        maxIntensity *= adjustCoef;
        minIntensity *= adjustCoef;
        range = maxIntensity - minIntensity;
        StartCoroutine(LightBugFix());
    }

    private void LateUpdate()
    {
        if (active || minIntensity > 0f)
        {
            SpriteCheck();
        }
    }
    
    IEnumerator LightBugFix()
    {
        yield return null;
        l.enabled = false;
        l.enabled = true;
    }

    void SpriteCheck()
    {
        if (spr != sr.sprite)
        {
            l.lightCookieSprite = sr.sprite; //had to download entire flippin package so this better work.
            spr = sr.sprite;
        }
    }

    public void On()
    {
        StopAllCoroutines();
        SpriteCheck();
        l.intensity = maxIntensity;
        active = true;
    }

    public void Off()
    {
        StopAllCoroutines();
        l.intensity = minIntensity;
        active = false;
    }

    public void FadeInQuick()
    {
        Fade(true, 0.25f);
    }

    public void FadeIn()
    {
        Fade(true, 0.5f);
    }

    public void FadeInSlow()
    {
        Fade(true, 1f);
    }

    public void FadeInVSlow()
    {
        Fade(true, 2f);
    }

    public void FadeOutQuick()
    {
        Fade(false, 0.25f);
    }

    public void FadeOut()
    {
        Fade(false, 0.5f);
    }

    public void FadeOutSlow()
    {
        Fade(false, 1f);
    }

    public void FadeOutVSlow()
    {
        Fade(false, 2f);
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
                l.intensity += range * Time.deltaTime / t;
            }
            else
            {
                l.intensity -= range * Time.deltaTime / t;
                if(l.intensity <= minIntensity)
                {
                    l.intensity = minIntensity;
                    active = false;
                    yield break;
                }
            }
            yield return null;
        }
        l.intensity = fadeIn ? maxIntensity : minIntensity;
        if (!fadeIn)
        {
            active = false;
        }
    }

    public void UpdateCoef(float coef)
    {
        adjustCoef = coef;
    }
}
