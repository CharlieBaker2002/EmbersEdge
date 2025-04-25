using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[CreateAssetMenu(fileName = "ColorSO", menuName = "ScriptableObjects/ColourSO")]
public class ColourSO : ScriptableObject
{
    public Color StandardWhite;
    public Color StandardGreen;
    public Color StandardBlue;
    public Color StandardRed;
    public Color Level1;
    public Color Level2;
    public Color Level3;
    public Color[] cols;
    public Color[] levels;

    public List<SpriteRenderer> w;
    public List<SpriteRenderer> g;
    public List<SpriteRenderer> b;
    public List<SpriteRenderer> r;
    public List<SpriteRenderer> one;
    public List<SpriteRenderer> two;
    public List<SpriteRenderer> three;

    public List<Image> wU;
    public List<Image> gU;
    public List<Image> bU;
    public List<Image> rU;
    public List<Image> oneU;
    public List<Image> twoU;
    public List<Image> threeU;

    private void Awake()
    {
        UpdateCols();
    }

    public void UpdateCols()
    {
        cols = new Color[] { StandardWhite, StandardGreen, StandardBlue, StandardRed, Level1, Level2, Level3 };
        levels = new Color[] { Level1, Level2, Level3 };
    }

}
