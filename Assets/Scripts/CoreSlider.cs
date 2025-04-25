using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEditor.Rendering;

public class CoreSlider : MonoBehaviour
{
    public RectTransform fill;
    public RectTransform bas;
    public TextMeshProUGUI txt;
    [HideInInspector]
    public float max = 0;
    [HideInInspector]
    public float current = 0;
    public float length = 5f;
    private float t = 1f;
    public Image endSlider;
    public RectTransform sliderBase;
    public Sprite[] endSliderSprites; //23 sprites incl base empty
    private int endSliderN = 0;
    public bool shield = false;
    public bool health = false;

    private float initLength;

    private void Awake()
    {
        initLength = length;
    }

    public void UpdateSlider(float x)
    {
        fill.sizeDelta = new Vector2(Mathf.Min(96 * length * x/max, bas.sizeDelta.x - 3), 96);
        if (shield)
        {
            txt.text =  x.ToString("0.0");
        }
        else if (health)
        {
            txt.text = x.ToString("0.0") + " / " + max.ToString("0.0");
        }
        else
        {
            txt.text = Mathf.Floor(x) + " / " + Mathf.Floor(max);
        }
        current = x;
    }

    public virtual void UpdateMax(float m)
    {
        if (max != 0)
        {
            length *= (m / max);
        }
        max = m;
        bas.sizeDelta = new Vector2(length * 96, 100);
        UpdateSlider(Mathf.Min(current,max));
    }

    public void InitialiseSlider(float m)
    {
        max = current = m;
        length = initLength;
        UpdateMax(m);
    }

    private void Update()
    {
        t -= Time.deltaTime;
        if(t <= 0f)
        {
            if (current >= max)
            {
                t = 0.1f;
                endSliderN += 1;
                if(endSliderN >= endSliderSprites.Length - 5)
                {
                    endSliderN = 10;
                }
            }
            else
            {
                t = 0.04f;
                if(endSliderN != 0)
                {
                    endSliderN += 1;
                    if (endSliderN == endSliderSprites.Length)
                    {
                        endSliderN = 0;
                    }
                }
            }
            endSlider.sprite = endSliderSprites[endSliderN];
        }
    }
}
