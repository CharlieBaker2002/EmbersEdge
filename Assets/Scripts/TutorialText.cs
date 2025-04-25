using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class TutorialText : MonoBehaviour
{
    public TextMeshProUGUI txt;
    public string[] strs;
    public string[] altText = new string[] { };
    private string[] strDisplay;
    public Room r;
    public bool init = false;
    [HideInInspector]
    public float speed = 1f;
    public bool buildingTutorial = false;

    public void Start()
    {
        if (!buildingTutorial)
        {
            txt.color = new Color(1, 1, 1, 0);
            IM.OnControllerChange += _ => strDisplay = _ ? (altText.Length == 0 ? strs : altText) : strs;
            IM.OnControllerChange += _ => { StopAllCoroutines(); StartCoroutine(Go()); };
        }
        strDisplay = strs;
        StartCoroutine(Go());
    }

    public IEnumerator Go()
    {
        yield return new WaitForSeconds(0.3f);
        Color cl = new Color(1, 1, 1, 0);
        txt.color = cl;
        if (r != null)
        {
            while (r.hasCharacter <= 0 && !init)
            {
                yield return null;
            }
        }
        while (true)
        {
            foreach(string str in strDisplay)
            {
                txt.text = str;
                yield return new WaitForSeconds(0.25f);
                while (txt.color.a < 0.96f)
                {
                    txt.color = Color.Lerp(txt.color, Color.white, Time.deltaTime * 2 * speed);
                    yield return null;
                }
                txt.color = Color.white;
                yield return new WaitForSeconds(Mathf.Max(1f, Mathf.Min(5,2 * txt.text.Length  / (25 * speed))));
                while (txt.color.a > 0.04f)
                {
                    txt.color = Color.Lerp(txt.color, cl, Time.deltaTime * 3 * speed);
                    yield return null;
                }
                txt.color = cl;
            }
            yield return null;
        }
    }
}
