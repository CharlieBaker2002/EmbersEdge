using System.Collections;
using UnityEngine;

public class Ore : MonoBehaviour
{
    private float orbs;
    public int orbType;
    private float chipCoef;

    public void Setup(int typ, float _orbs, float coef)
    {
        orbs = _orbs;
        orbType = typ;
        chipCoef = coef;
    }

    public int Chip()
    {
        int prev = Mathf.FloorToInt(orbs);
        orbs -= chipCoef;
        if (orbs <= 0f)
        {
            StartCoroutine(Des());
            //Destroy(gameObject,1f);
        }
        if (prev - Mathf.FloorToInt(orbs) > 0)
        {
            Instantiate(Resources.Load("ChipFX"), transform.position, Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)), transform);
        }
        return prev - Mathf.FloorToInt(orbs);
    }

    private IEnumerator Des()
    {
        yield return null;
        TilemapResource.m[orbType].SetTile(TilemapResource.m[orbType].WorldToCell(transform.position), null);
    }

}
