using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BossFightShortcut : MonoBehaviour, IClickable
{
    public Room room;
    private bool hasActivated = false;

    public void OnClick()
    {
        CharacterScript.CS.transform.position = room.transform.parent.position + new Vector3(0, 1);
        PortalScript.i.inDungeon = true;
        if (!hasActivated)
        {
            room.OnEnter();
            hasActivated = true;
        }
    }
}
