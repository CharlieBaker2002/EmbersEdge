using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PortalTrigger : MonoBehaviour
{
    public static PortalTrigger i;
    public Collider2D col;
    public SpriteRenderer sr;
    public Animator anim;
    private Color fadeCol;


    private void Awake()
    {
        i = this;
        fadeCol = new Color(0.6f, 0.6f, 0.6f);
    }

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
        if (sr.color != Color.white)
        {
            StopAllCoroutines();
            StartCoroutine(Fade(true));
            col.enabled = true;
        }
    }

    public void FadeOut(bool safe = false)
    {
        if(sr.color != fadeCol)
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
            while (sr.color.r <= 0.985f)
            {
                sr.color = Color.Lerp(sr.color, Color.white, 1.75f * Time.deltaTime);
                yield return null;
            }
            sr.color = Color.white;
        }
        else
        {
            yield return new WaitForSeconds(1.5f);
            while (sr.color.r >= 0.61f)
            {
                sr.color = Color.Lerp(sr.color, fadeCol, 3f * Time.deltaTime);
                yield return null;
            }
            sr.color = fadeCol;
        }
    }

}
