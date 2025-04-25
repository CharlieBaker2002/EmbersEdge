using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GroupTile : MonoBehaviour, IClickable
{
    [HideInInspector]
    public LifeScript ls = null;
    public TextMeshProUGUI nam;
    public TextMeshProUGUI siz;
    public Image background;
    public Image img;
    private float hp;
    public Gradient gradient;

    public void Init(string namP, Sprite spr, int size, LifeScript lsP)
    {
        if(namP.Contains("(Clone)"))
        {
            namP = namP.Remove(namP.IndexOf("(Clone)"));
        }
        nam.text = namP;
        ls = lsP;
        img.sprite = spr;
        siz.text = size.ToString();
    }

    private void Update()
    {
        if (ls != null)
        {
            if(hp!= ls.hp)
            {
                hp = ls.hp;
                background.color = gradient.Evaluate(ls.hp / ls.maxHp);
            }
        }
    }

    public void OnClick()
    {
        if (!PortalScript.i.inDungeon)
        {
            AllyAI ai = ls.GetComponent<AllyAI>();
            CharacterScript.CS.groupCurrent -= ai.groupCost;
            CharacterScript.CS.group.Remove(ai);
            CharacterScript.CS.DeleteTile(ls);
            ai.exploreRadius *= 2;
            if (AllyAI.rallyMode)
            {
                ai.mode = AllyAI.Mode.rally;
            }
        }
        else
        {
            //Error message
            Debug.Log("in dungeon, cant change group");
        }
    }
}
