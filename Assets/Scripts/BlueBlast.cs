using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BlueBlast : MonoBehaviour
{
    public Transform[] sps;
    public GameObject bullet;
    public LifeScript ls;
    public Transform ps;
    public int lvl = 1;

    public void Bosh()
    {
        for(int i = 1; i < lvl; i++)
        {
            foreach (Transform t in sps)
            {
                StartCoroutine(Shoot(t));
            }
        }
        ps.parent = GS.FindParent(GS.Parent.fx);
        ps.gameObject.SetActive(true);
    }

    private IEnumerator Shoot(Transform t)
    {
        yield return new WaitForSeconds(Random.Range(0f, 0.25f));
        GS.NewP(bullet, t, tag, 0.8f, 0, 5);
    }
}
