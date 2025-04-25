using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ColourOnHover : MonoBehaviour, IHoverable
{
    [SerializeField] private Image img;
    [SerializeField] private SpriteRenderer sr;
    [SerializeField] private Color[] cols;
    private bool on = false;
    [SerializeField] private bool ignoreTimeScale = true;
    
    public void OnHover()
    {
        if (sr != null && !on)
        {
            on = true;
            LeanTween.LeanSRCol(sr, cols[1], 0.5f).setIgnoreTimeScale(ignoreTimeScale);
        }
        if (img != null && !on)
        {
            on = true;
            LeanTween.LeanImgCol(img, cols[1], 0.5f).setIgnoreTimeScale(ignoreTimeScale);
        }
    }

    public void OnDeHover()
    {
        if (sr != null)
        {
            on = false;
            LeanTween.cancel(sr.gameObject);
            LeanTween.LeanSRCol(sr,cols[0], 1f).setIgnoreTimeScale(ignoreTimeScale);
        }

        if (img != null)
        {
            on = false;
            LeanTween.cancel(img.gameObject);
            LeanTween.LeanImgCol(img,cols[0], 1f).setIgnoreTimeScale(ignoreTimeScale);
        }
    }
}
