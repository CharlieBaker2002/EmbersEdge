using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GoBackButtonBuilding : MonoBehaviour, IClickable
{
    public void OnClick()
    {
        BM.i.BackItUpOffDaddy();
    }
}
