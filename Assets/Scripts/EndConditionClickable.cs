using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class EndConditionClickable : MonoBehaviour, IClickable
{
    public string function = "";

    public void OnClick()
    {
        function.ToLower();
        if(function == "mainmenu")
        {
            IM.i.Reset();
            Time.timeScale = 1;
            SceneManager.LoadScene(0);
        }
        else if(function == "continue")
        {
            PortalScript.i.WinUI.SetActive(false);
            SpawnManager.instance.CancelTS(PortalScript.i.tsID);
        }
        else if(function == "exit")
        {
            Application.Quit();
        }
        else if(function == "resume")
        {
            UIManager.i.escapeDel.Invoke(new InputAction.CallbackContext());
        }
    }
}
