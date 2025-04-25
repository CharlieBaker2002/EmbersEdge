using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GroupButton : MonoBehaviour, IClickable
{
    public void OnClick()
    {
        if (!CharacterScript.CS.groupUIParent.activeInHierarchy)
        {
            UIManager.CloseAllUIs();
        }
        CharacterScript.CS.AltGroupUI();
    }
}
