using UnityEngine;
using System;
using UnityEngine.UI;
using System.Collections;
using TMPro;
using System.Collections.Generic;

public class BuildingTile : MonoBehaviour, IClickable
{
    public int[] cost = new int[4] { 0, 0, 0, 0 };
    [HideInInspector]
    public GameObject buildingPrefab;
    public Image background;
    public Image img;
    public Color init;
    public TextMeshProUGUI txt;
    public TextMeshProUGUI w;
    public TextMeshProUGUI g;
    public TextMeshProUGUI b;
    public TextMeshProUGUI r;
    private TextMeshProUGUI[] texts;

    private void Awake()
    {
        init = background.color;
        img.preserveAspect = true;
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        background.color = init;
    }

    public void OnClick()
    {
        if (ResourceManager.instance.CanAfford(cost))
        {
            GS.CopyArray(ref BM.i.cost, cost);
            StartCoroutine(Colour(Color.green));
            if (BM.i.planting)
            {
                BM.i.Escape();
            }
            BM.i.BuildingFollowMouse(buildingPrefab,this);
            BM.i.RemoveDaddyDel();
        }
        else
        {
            StartCoroutine(Colour(Color.red));
        }
    }

    public IEnumerator Colour(Color col)
    {
        background.color = col;
        float timer = 0f;
        while (background.color != init)
        {
            background.color = Color.Lerp(background.color, init, Time.deltaTime * 5);
            timer += Time.deltaTime;
            yield return null;
            if (timer > 1f)
            {
                background.color = init;
                yield break;
            }
        }
    }

    public void UpdateCost()
    {
        texts = new TextMeshProUGUI[] { w, g, b, r };
        if (transform.parent.name != "WCanvas" || name == "wDefault")
        {
            for (int i = 0; i < 4; i++)
            {
                texts[i].text = Mathf.Abs(cost[i]).ToString();
                texts[i].gameObject.SetActive(cost[i] != 0f);
            }
        }
        else
        {
            for (int i = 0; i < 4; i++)
            {
                texts[i].gameObject.SetActive(false);
            }
        }
    }

    public void ChangeBackground()
    {
        List<Color> cols = new List<Color>();
        for(int i = 0; i< 4; i++)
        {
            if(cost[i] > 0)
            {
                cols.Add(ColorFromIndex(i));
            }
        }
        float[] col = new float[3] { 0,0,0};
        foreach(Color c in cols)
        {
            col[0] += c.r;
            col[1] += c.g;
            col[2] += c.b;
        }
        col[0] /= cols.Count;
        col[1] /= cols.Count;
        col[2] /= cols.Count;
        background.color = new Color(col[0], col[1], col[2], 0.4f);
        init = background.color;
    }

    private Color ColorFromIndex(int ind)
    {
        switch (ind)
        {
            case 0:
                return UIManager.i.colSO.StandardWhite;
            case 1:
                return UIManager.i.colSO.StandardGreen;
            case 2:
                return UIManager.i.colSO.StandardBlue;
            case 3:
                return UIManager.i.colSO.StandardRed;
            default:
                return Color.white;
        }
    }
}
