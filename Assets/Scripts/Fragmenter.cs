using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Fragmenter : MonoBehaviour, IOnDeath, IOnCollide
{
    public int n;
    public GameObject fragment;
    public float fragmentDiameter = 0.5f;

    public void OnCollide(Collision2D collision)
    {
        Fragment();
    }

    public void OnDeath()
    {
        Fragment();
    }

    public void Fragment()
    {
        GS.Parent p = GS.ProjParent(transform);
        int N = Mathf.RoundToInt(n*Random.Range(0.4f,1.6f));
        for (int i = 0; i < N; i++)
        {
            var a = Instantiate(fragment, transform.position + (Vector3) Random.insideUnitCircle * fragmentDiameter, Quaternion.Euler(0f, 0f, Random.Range(0, 360)), GS.FindParent(p));
            a.GetComponent<ProjectileScript>().SetValues(a.transform.up, tag);
        }
    }
}
