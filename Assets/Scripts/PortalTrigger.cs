using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PortalTrigger : MonoBehaviour
{
    public static PortalTrigger i;
    public Collider2D col;
    public SpriteRenderer sr;
    public Animator anim;


    private void Awake()
    {
        i = this;
    }

    // Morph-overlay tints pulled from the current dungeon era, so the portal
    // flash matches GS.MatByEra()/ColFromEra() like the rest of the portal VFX.
    // (The overlay fades its colour tint, so ColFromEra is the right lever here
    //  rather than the era Material the additive glow sprites swap to.)
    private Color BrightEraCol() => Color.Lerp(GS.ColFromEra(), Color.white, 0.35f);
    private Color DimEraCol()    => Color.Lerp(GS.ColFromEra(), Color.black, 0.5f);

    private static bool Approx(Color a, Color b) =>
        Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < 0.02f;

    public void OffForT(float t)
    {
        StopAllCoroutines();
        StartCoroutine(Wait(t));
    }

    private IEnumerator Wait(float t)
    {
        col.enabled = false;
        yield return new WaitForSeconds(t);
        col.enabled = true;
    }

    void OnTriggerEnter2D(Collider2D col)
    {
        if(UIManager.i.telemode == UIManager.TeleMode.Base)
        {
            if (anim.GetBool("Morph") == false)
            {
                anim.SetBool("Morph", true);
                FadeIn();
            }
        }
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (PortalScript.i.inDungeon || SetM.quit)
        {
            return;
        }
        if (anim.GetBool("Morph") == true)
        {
            anim.SetBool("Morph", false);
            FadeOut();
        }
    }

    public void FadeIn()
    {
        if (sr.color != BrightEraCol())
        {
            StopAllCoroutines();
            StartCoroutine(Fade(true));
            col.enabled = true;
        }
    }

    public void FadeOut(bool safe = false)
    {
        if(sr.color != DimEraCol())
        {
            if (!safe)
            {
                StopAllCoroutines();
                col.enabled = true;
            }
            StartCoroutine(Fade(false));
        }
    }

    private IEnumerator Fade(bool fadeIn)
    {
        if (fadeIn)
        {
            Color target = BrightEraCol();
            while (!Approx(sr.color, target))
            {
                sr.color = Color.Lerp(sr.color, target, 1.75f * Time.deltaTime);
                yield return null;
            }
            sr.color = target;
        }
        else
        {
            yield return new WaitForSeconds(1.5f);
            Color target = DimEraCol();
            while (!Approx(sr.color, target))
            {
                sr.color = Color.Lerp(sr.color, target, 3f * Time.deltaTime);
                yield return null;
            }
            sr.color = target;
        }
    }

}
