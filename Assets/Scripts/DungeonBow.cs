using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DungeonBow : MonoBehaviour
{
    public GameObject arrow;

    public void FireArrow()
    {
        var p = Instantiate(arrow, transform.position, transform.rotation, GS.FindParent(GS.Parent.enemies)).GetComponent<ProjectileScript>();
        p.SetValues(-transform.right,"Enemies");
    }
}
