using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TutorialManager : MonoBehaviour
{
    public static bool tutorial = false;
    public static bool building = false;

    private void Awake()
    {
        tutorial = true;
        building = (name == "CombatTutorial") ? false : true;
    }

    private IEnumerator Start()
    {
        if (!building)
        {
            MapManager.i.SetMap(true);
            IM.i.pi.Player.Portal.Disable();
            yield return new WaitForSeconds(0.25f);
            IM.i.pi.Player.Build.Disable();
            MapManager.i.SetMap(true);
            IM.i.pi.Player.Map.Disable();
            PortalScript.i = GetComponent<PortalScript>();
            IM.i.pi.Player.Escape.Enable();
        }
    }

    public void OnDestroy()
    {
        tutorial = false;
        building = false;
    }
}