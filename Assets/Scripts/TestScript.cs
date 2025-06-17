using System;
using System.Linq;
using UnityEngine;

public class TestScript : MonoBehaviour
{
    private void Start()
    {
        IM.i.pi.Player.Reload.performed += _ => LightningBastards();
    }

    void LightningBastards()
    {
        foreach(Transform t in GS.FindEnemies(tag, transform.position, 10f, false))
        {
            GS.Stat(t.GetComponent<Unit>(), "static", 1f);
        }
    }
}




 