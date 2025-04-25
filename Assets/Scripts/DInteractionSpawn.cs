using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DInteractionSpawn : MonoBehaviour
{
    public GameObject[] pSpawns;
    public int p = 100;
    public int value = 2;
    public bool rotate = false;
    public bool onStart = false;

    private void Start()
    {
        if (onStart) { Make(); Destroy(this); }
    }

    public int Make()
    {
        if (Random.Range(0, 101) <= p)
        {
            if (!rotate)
            {
                var g = Instantiate(GS.RE(pSpawns), transform.position, Quaternion.identity, transform);
                g.transform.localScale = new Vector3(1 / 1.75f, 1 / 1.75f, 1);
            }
            else
            {
                if(transform.rotation.eulerAngles.z == 180f || transform.rotation.eulerAngles.z == -180f)
                {
                    transform.rotation = Quaternion.identity;
                }
                var g = Instantiate(GS.RE(pSpawns), transform.position, transform.rotation, transform);
                g.transform.localScale = new Vector3(transform.localScale.x / 1.75f, transform.localScale.y / 1.75f, 1);
            }
            Destroy(this);
            return value;
        }
        else
        {
            return 0;
        }
        
    }
}
