using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CloseHintButton : MonoBehaviour, IClickable
{
    [SerializeField] private Hint h;
    public void OnClick()
    {
        h.Shut();
    }
}
