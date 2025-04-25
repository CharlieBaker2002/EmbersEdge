using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class StartScreen : MonoBehaviour
{
    [SerializeField] private string txt;
    [SerializeField] private TextMeshProUGUI tex;
    private IEnumerator Start()
    {
        yield return new WaitForSeconds(1.5f);
        StartCoroutine(NewLoot.ScrollText(tex, txt));
    }

    public void Quit()
    {
        Application.Quit();
    }

    public void StarButt()
    {
        SexyCutsceneManager.i.Cutscene(1);
    }
    
    public void Arena()
    {
        SexyCutsceneManager.i.Cutscene(2);
    }
}
