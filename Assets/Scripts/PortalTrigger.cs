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

    // The portal's body keeps its natural sprite colours at all times — its look only changes with
    // era (the animator's Blend/OnNewEra sprite-sheet swaps). These used to tint sr toward a
    // bright/dim era colour on every approach, teleport and death-return, which drifted the centre
    // building's colour permanently; now they only guarantee the tint is reset.
    public void FadeIn()
    {
        StopAllCoroutines();
        sr.color = Color.white;
        col.enabled = true;
    }

    public void FadeOut(bool safe = false)
    {
        sr.color = Color.white;
        if (!safe)
        {
            StopAllCoroutines();
            col.enabled = true;
        }
    }

}
