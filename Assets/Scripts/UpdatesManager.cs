using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class UpdatesManager : MonoBehaviour
{
    public static UpdatesManager i;
    [TextArea(0,50)]
    public string[] updateTexts;
    public string[] versions;
    public Sprite[] sprites; //grouped in 5s
    public Image[] imgs;
    public TextMeshProUGUI coreText;
    public TextMeshProUGUI versionText;
    private int index = 0;

    private void Awake()
    {
        i = this;
    }

    private void Start()
    {
        index = versions.Length - 1;
        SortUI();
    }

    private void SortUI()
    {
        coreText.text = updateTexts[index];
        versionText.text = versions[index];
        for(int j = 0; j < 5; j++)
        {
            int x = index * 5 + j;
            if(sprites[x] != null)
            {
                imgs[j].sprite = sprites[x];
                imgs[j].color = new Color(1, 1, 1, 1);
            }
            else
            {
                imgs[j].color = new Color(1, 1, 1, 0);
            }
            imgs[j].preserveAspect = true;
        }
    }

    public void ChangeIndex(bool positive)
    {
        if (positive)
        {
            index += 1;
        }
        else
        {
            index -= 1;
        }
        if(index >= versions.Length)
        {
            index = 0;
        }
        else if(index < 0)
        {
            index = versions.Length - 1;
        }
        SortUI();
    }
}
