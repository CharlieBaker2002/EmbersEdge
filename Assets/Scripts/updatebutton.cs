using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class updatebutton : MonoBehaviour
{
    public bool positive = false;

    public void ChangeManagerValue()
    {
        UpdatesManager.i.ChangeIndex(positive);
    }
}
