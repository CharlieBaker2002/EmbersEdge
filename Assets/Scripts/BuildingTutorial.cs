using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;
public class BuildingTutorial : MonoBehaviour
{
    public TutorialText txt;
    public GameObject[] buildings;
    private int ind = 0;
    public GameObject[] enemies;
    public static bool defeated = false;
    public LifeScript ls;
    private readonly int skipKey = 0;
    private TurretScript t;
    //build a turret, build a cell

    private void Awake()
    {
        if(skipKey != 0)
        {
            txt.strs = new string[] { };
        }
    }

    //    //yield return StartCoroutine(NextStrs(new string[] { "" }));
    //}

    IEnumerator NextStrs(string[] strs, bool skip = false)
    {
        if (!skip)
        {
            if(txt.strs.Length > 0)
            {
                while (txt.txt.text != txt.strs[^1])
                {
                    yield return null;
                }
                yield return new WaitForSeconds(0.5f);
            }
            txt.strs = strs;
        }
        else
        {
            txt.StopAllCoroutines();
            txt.strs = strs;
            txt.StartCoroutine(txt.Go());
        }
        
    }

    private IEnumerator HadBuilding(string s, bool ignoreclone = false)
    {
        while(GS.FindParent(GS.Parent.buildings).Find(s + (!ignoreclone? ("(Clone)") : "")) == null)
        {
            yield return null;
        }
    }

    private void NextB()
    {
        bool a = BM.i.UI.activeInHierarchy;
        UIManager.CloseAllUIs();
        //BM.i.buildingPrefabs.Add(buildings[ind]);
        ind++;
        if (a)
        {
            BM.i.AltUI();
        }
    }
}
