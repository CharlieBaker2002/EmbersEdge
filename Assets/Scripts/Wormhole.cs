using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Wormhole : MonoBehaviour
{
    private bool activated = false;
    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (!activated)
        {
            if (collision.gameObject.name == "Character")
            {
                activated = true;
                GetComponent<Animator>().SetBool("Trigger", true);
                BlueprintManager.LootSafe();
                enabled = false;
                DM.i.RevealRooms();
                CameraScript.i.StartTemporaryZoom(7.5f, 5f, 0.04f, 0.02f);
            }
        }
    }
}
