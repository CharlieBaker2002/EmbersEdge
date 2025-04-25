using System.Collections;
using UnityEngine;

public class EEIcon : MonoBehaviour
{
    private static readonly int Color1 = Shader.PropertyToID("thecolor");
    [SerializeField] private SpriteRenderer sr;
    private Material m;
    private Color c;
    [SerializeField] Color startCol;
    [SerializeField] private GameObject[] FXs;
    
    //NOTE I'VE SWAPPED STARTCOL AND C AROUND FOR CONVEINCE SAKE.
    void  Start()
    {
        m = Instantiate(GS.MatByEra(GS.era,false));
        c = m.GetColor(Color1);
        m.SetColor(Color1, c);
        sr.material = m;
    }

    public IEnumerator SetDone()
    {
        for (float t = 0f; t < 1f; t += Time.deltaTime)
        {
            m.SetColor(Color1, Color.Lerp(c,startCol, t * t));
            yield return null;
        }
        m.SetColor(Color1, startCol);
        transform.LeanScale(Vector3.zero, 1f).setEaseInBack().setOnComplete(() => Instantiate(FXs[GS.era], transform.position,
            Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)), GS.FindParent(GS.Parent.fx))).delay = 1f;
    }
    
}
