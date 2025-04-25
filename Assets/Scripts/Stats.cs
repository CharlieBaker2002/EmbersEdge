using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class Stats : MonoBehaviour
{
    TextMeshProUGUI txt;
    List<float> datas = new();
    public int n = 10;
    float t;

    private void Awake()
    {
        txt = GetComponent<TextMeshProUGUI>();
    }

    // Update is called once per frame
    void Update()
    {
        if(datas.Count < n)
        {
            datas.Add(Time.unscaledDeltaTime);
            txt.text = (1f / Time.deltaTime).ToString("F0") + " FPS ";
        }
        else
        {
            t = 0;
            foreach(float x in datas)
            {
                t += x;
            }
            t = t / n;
            txt.text = (1f / t).ToString("F0") + " FPS ";
            datas.RemoveAt(0);
            datas.Add(Time.unscaledDeltaTime);
        }
        
    }
}
