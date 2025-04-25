using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class BuildingUI : MonoBehaviour
{
    public void OnDisable()
    {
        BM.i.goToDaddy.Invoke(new InputAction.CallbackContext());
    }
}
