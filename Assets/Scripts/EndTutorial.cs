using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

public class EndTutorial : MonoBehaviour
{
    public Room r;
    public GameObject endUI;
    public TextMeshProUGUI[] txts;

    private IEnumerator Start()
    {
        while(r.defeated == false)
        {
            yield return null;
        }
        UIManager.i.pauseUI.SetActive(false);
        IM.i.pi.Player.Escape.Disable();
        endUI.SetActive(true);
        foreach(TextMeshProUGUI t in txts)
        {
            StartCoroutine(LoadText(t));
            yield return new WaitForSeconds(2f);
        }
        yield return new WaitForSeconds(3f);
        SceneManager.LoadScene(0);
    }

    private IEnumerator LoadText(TextMeshProUGUI t)
    {
        while(t.color.a < 1)
        {
            t.color = Color.Lerp(t.color, Color.white, Time.deltaTime * 1.5f);
            yield return null;
        }
    }
}
