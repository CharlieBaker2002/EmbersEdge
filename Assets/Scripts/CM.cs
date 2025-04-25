using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TMPro;

public class CM : MonoBehaviour
{
    //Conversation Manager
    [SerializeField] private TextMeshProUGUI txtPrefab;
    
    private static CM i;
    public enum CT { UneventfulRandom, UnluckySpawn, LuckyRandom, Bonus, NotMeantToSee}; //ConvoType

    private List<RectTransform> ts = new List<RectTransform>();
    private void Awake()
    {
        i = this;
    }

    public static void Convo(CT c)
    {
        
    }

    public static void Message(string s, bool negative = true)
    {
        if(s.Length > 14)
        {
            if (s[0] == 'M')
            {
                if (s[1..4] == "ain")
                {
                    return;
                }
            }
            else if (s[1] == 'M')
            {
                if (s[2..5] == "ain")
                {
                    return;
                }
            }
        }
        i.StartCoroutine(i.Msg(s,negative));
    }

    private IEnumerator Msg(string s, bool negative)
    {
        foreach (RectTransform t in ts)
        {
            t.anchoredPosition -= new Vector2(0f, 100f);
        }
        var txt = Instantiate(txtPrefab, txtPrefab.transform.position, Quaternion.identity, UIManager.i.canvas);
        ts.Add(txt.rectTransform);
        txt.text = s;
        if (!negative)
        {
            txt.color = Color.green;
        }
        txt.gameObject.SetActive(true);
        yield return new WaitForSecondsRealtime(0.25f);
        for (float t = 0f; t < 4f; t += Time.unscaledDeltaTime)
        {
            yield return new WaitForSecondsRealtime(0f);
         
            if (t > 2f)
            {
                if (negative)
                {
                    txt.color = Color.Lerp(txt.color, new Color(1f, 0f, 0f, 0f), Time.unscaledDeltaTime * 2.5f * (t-2f));
                }
                else
                {
                    txt.color = Color.Lerp(txt.color, new Color(0f, 1f, 0f, 0f), Time.unscaledDeltaTime * 2.5f * (t-2f));
                }
                
            }

            txt.rectTransform.anchoredPosition -= new Vector2(0f, 10f * Time.unscaledDeltaTime);
        }
        ts.Remove(txt.rectTransform);
        Destroy(txt);
    }
    
}
