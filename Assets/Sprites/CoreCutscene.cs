using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class CoreCutscene : MonoBehaviour
{
    [SerializeField] Volume v;
    [SerializeField] private SpriteRenderer s;
    [SerializeField] private Camera cam;
    LensDistortion ld;
    [SerializeField] private EmbersEdge lr;
    [SerializeField] private Animator anim;
    [SerializeField] private Color tin;
    private IEnumerator Start()
    {
        v.sharedProfile.TryGet(out Bloom bl);
        bl.tint.Override(tin);
        bl.intensity.Override(3f);
        bl.threshold.Override(0.8f);
        
        v.sharedProfile.TryGet(out ld);
        ld.scale.Override(1f);
        ld.intensity.Override(1f);
        yield return null;
        LeanTween.reset();
        
        yield return new WaitForSeconds(1f);
        ld.scale.Override(0.6f);
        ld.intensity.Override(1f);
        lr.gameObject.SetActive(false);
        anim.enabled = true;
        s.enabled = true;
        
        LeanTween.value(gameObject, 0.6f, 1.5f, 2f).setEaseOutCirc().setOnUpdate(x=>ld.scale.Override(x));
        LeanTween.value(gameObject, 1f, -0.95f, 3.25f).setEaseOutQuart().setOnUpdate(x =>
        {
            ld.intensity.Override(x);
        });
    }

    public void Revert()
    {
        LeanTween.cancel(gameObject);
        LeanTween.value(gameObject, -0.95f, 1f, 1f).setEaseOutQuart().setOnUpdate(x =>
        {
            ld.intensity.Override(x);
        });
        LeanTween.value(gameObject, 1.5f, 0.45f, 1f).setEaseOutQuad().setOnUpdate(x => ld.scale.Override(x));
        LeanTween.value(cam.gameObject, 3.5f, 10f, 1f).setOnUpdate(x => cam.orthographicSize = x).setOnComplete(x=>
        {
            StartCoroutine(Activate());
            LeanTween.value(gameObject, 10f, 20f, 1.25f).setEaseInQuad().setOnUpdate(z => cam.orthographicSize = z);
        });
        LeanTween.LeanSRCol(s, Color.clear, 2.5f).setEaseInBack();
    }
    
    public void Stop()
    {
        anim.enabled = false;
    }

    IEnumerator Activate()
    {
        lr.gameObject.SetActive(true);
        yield return new WaitForSeconds(1f);
        LeanTween.cancel(gameObject);
        LeanTween.value(cam.gameObject, 20f, 6f, 10f).setOnUpdate(x => cam.orthographicSize = x);
        yield return StartCoroutine(lr.MakeFX());
        StartCoroutine(lr.Acco(1f));
        LeanTween.LeanSRCol(s, Color.clear, 5f);
        yield return new WaitForSeconds(9f);
        Debug.Log("DONE!");
        Debug.Break();
    }
}
