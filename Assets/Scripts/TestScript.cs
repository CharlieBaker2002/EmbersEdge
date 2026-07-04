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
            // FindEnemies hands back collider transforms — the Unit can live on the rigidbody/parent,
            // so resolve upward and skip anything without one (else GS.Stat NREs on a null unit).
            var u = t.GetComponentInParent<Unit>();
            if (u != null) GS.Stat(u, "static", 1f);
        }
    }
}




 