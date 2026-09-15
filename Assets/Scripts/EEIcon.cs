using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Random = UnityEngine.Random;

public class EEIcon : MonoBehaviour
{
    private static readonly int Color1 = Shader.PropertyToID("thecolor");
    [SerializeField] private SpriteRenderer sr;
    private Material m;
    private Color c;
    [SerializeField] Color startCol;
    [SerializeField] private GameObject fx;

    public static List<EEIcon> icons = new List<EEIcon>();

    //NOTE I'VE SWAPPED STARTCOL AND C AROUND FOR CONVEINCE SAKE.
    void Start()
    {
        SetColour();
        icons.Add(this);
    }

    public void SetColour()
    {
        if(m!=null) Destroy(m);
        m = Instantiate(GS.Glow(GlowLevel.Dim));
        // this copy is faded to an explicit grey later, so it paints the full era colour itself
        if (m.HasProperty(EraGlow.EraLevelId)) m.SetFloat(EraGlow.EraLevelId, 0f);
        c = EraGlow.Colour(GlowLevel.Dim);
        m.SetColor(Color1, c);
        sr.material = m;
    }

    public IEnumerator SetDone()
    {
        icons.Remove(this);
        for (float t = 0f; t < 1f; t += 3f*Time.deltaTime)
        {
            m.SetColor(Color1, Color.Lerp(c,startCol, t * t));
            yield return null;
        }
        m.SetColor(Color1, startCol);
        transform.LeanScale(Vector3.zero, 0.25f).setEaseInBack().setOnComplete(() => Instantiate(fx, transform.position,
            Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)), GS.FindParent(GS.Parent.fx))).delay = 0.25f;
    }

    private void OnDestroy()
    {
        icons.Remove(this);
    }
}
