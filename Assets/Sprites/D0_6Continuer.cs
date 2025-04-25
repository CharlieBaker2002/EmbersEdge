using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class D0_6Continuer : MonoBehaviour
{
    [SerializeField] private Transform[] primaryTs;
    [SerializeField] private Transform[] extraTs;
    [SerializeField] private ProjectileScript ps;
    [SerializeField] private Rotator r;
    [SerializeField] private SpriteRenderer sr;
    [SerializeField] private LA l;
    
    public void Do(float thru, float maxTime, float omega, Sprite s, bool cancelled)
    {
        r.omega = omega;
        sr.sprite = s;
        if (cancelled)
        {
            LeanTween.value(gameObject,x=> r.omega =x, omega, 0f, 1f).setEaseInBack();
            LeanTween.scale(gameObject, Vector2.zero, 1f).setEaseInBack().setOnComplete(_ => Destroy(gameObject));
        }
        else
        {
            l.On();
      
            LeanTween.value(gameObject,x=> r.omega =x, omega, 0f, 1f).setEaseInBack();
            LeanTween.scale(gameObject, Vector3.one * (transform.localScale.magnitude * 1.5f), 1f).setEaseOutBack().setOnComplete(_ =>
            {
                l.FadeOut();
                r.omega = 0f;
                LeanTween.scale(gameObject, Vector2.zero, 1f).setEaseInBack().setOnComplete(_ => Destroy(gameObject));
                for (int i = 0; i < primaryTs.Length; i++)
                {
                    GS.NewP(ps, primaryTs[i], tag, 0f, 10f, 0f);
                }
                StartCoroutine(Boom(thru,maxTime));
            });
        }
    }

    public IEnumerator Boom(float thru, float maxTime)
    {
        int n = Mathf.FloorToInt(thru * extraTs.Length);
        for (int i = 0; i < n; i++)
        {
            GS.NewP(ps, extraTs[i], tag, 1f, 0f, 5f);
            yield return new WaitForFixedUpdate();
        }
    }
}
