using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System;

public class BaseTile : MonoBehaviour, IClickable
{
    public int[] cost = new int[4] { 0, 0, 0, 0 };
    public Image background;
    public Image img;
    public Color init;
    public TextMeshProUGUI txt;
    public TextMeshProUGUI w;
    public TextMeshProUGUI g;
    public TextMeshProUGUI b;
    public TextMeshProUGUI r;
    private TextMeshProUGUI[] texts;
    public bool destroyOnUse;
    public GameObject daddy;
    public TextMeshProUGUI n;

    public Action action;
    public Action instantAction;
    public Func<bool> optParam;
    public Func<bool> showParam;

    public bool scienceRequisit = false;

    private void Awake()
    {
        init = background.color;
        img.preserveAspect = true;
    }

    public void SetTextN(int num)
    {
        if (num > 0)
        {
            n.text = num.ToString();
            n.color = GS.ColFromEra() * 1.25f;
        }
    }
    public void Init(GameObject dad, int[] costP, string nam, Sprite imgP, bool destroyOnUseP, Action actP, Action instantActionP = null, Func<bool> optParamP = null, Func<bool> showParamP = null)
    {
        daddy = dad;
        txt.text = nam;
        img.sprite = imgP;
        destroyOnUse = destroyOnUseP;
        action = actP;
        instantAction = instantActionP;
        optParam = optParamP;
        showParam = showParamP;

        UpdateCost(costP);
        init = GS.ColourFromCost(cost);
        
        if (scienceRequisit)
        {
            init = new Color(0.25f,0.25f,0.25f,0.5f);
        }
        background.color = init;
    }

    void UpdateCost(int[] costP)
    {
        cost = costP;
        TextMeshProUGUI[] texts = new TextMeshProUGUI[] { w, g, b, r };
        for (int i = 0; i < 4; i++)
        {
            texts[i].text = Mathf.Abs(cost[i]).ToString();
            texts[i].gameObject.SetActive(cost[i] != 0f);
        }
    }

    private void OnEnable()
    {
        UpdateCost(cost);
    }

    public void Colour(Color col)
    {
        StopAllCoroutines();
        if (enabled && gameObject.activeInHierarchy)
        {
            StartCoroutine(IColour(col));
        }
    }
    public IEnumerator IColour(Color col)
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

    public void OnClick()
    {
        if (scienceRequisit)
        {
            CM.Message(txt.text + " Science Not Yet Researched");
            return;
        }
        if (optParam != null)
        {
            if (optParam.Invoke() == false)
            {
                Colour(Color.red);
                return;
            }
        }
        if (ResourceManager.instance.NewTask(daddy, cost, action))
        {
            instantAction?.Invoke();
            if (destroyOnUse)
            {
                Destroy(gameObject);
            }
            else
            {
                Colour(Color.green);
            }
        }
        else
        {
            Colour(Color.red);
        }
    }
}
