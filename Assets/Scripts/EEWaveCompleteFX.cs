using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EEWaveCompleteFX : MonoBehaviour
{
    [SerializeField] LineRenderer lr;
    float t = 0.05f;
    Vector3[] vs = new Vector3[100];
    private Gradient grad;
    [SerializeField] Material[] mats;
    [SerializeField] bool setColour = true;

    // Start is called before the first frame update
    void Start()
    {
        if (setColour)
        {
            lr.material = mats[GS.era];
        }
      
        LeanTween.value(gameObject, 0.05f, 1f, 5f).setOnUpdate(x => t = x).setEaseOutExpo();
    }

    // Update is called once per frame
    void Update()
    {
        if(t >= 1f) { Destroy(gameObject);  return; }

        lr.GetPositions(vs);
        for(int i = 0; i < vs.Length; i++)
        {
            vs[i] = 15f * t * vs[i].normalized;
        }
        lr.SetPositions(vs);

        if (!setColour) return;
        grad = new Gradient();
        grad.SetKeys(new GradientColorKey[] { new(GS.ColFromEra(), 0) },new GradientAlphaKey[] { new(1 - t*t, 0) });
        lr.colorGradient = grad;

    }
}
