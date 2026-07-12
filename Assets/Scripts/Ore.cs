using System.Collections;
using UnityEngine;

public class Ore : MonoBehaviour
{
    private float orbs;
    public int orbType;
    private float chipCoef;
    private float initialOrbs;  // captured at Setup so drone mining can pace by fraction-of-node
    private float droneYield;   // fractional orbs a mining drone has earned but not yet released
    /// <summary>Soft claim so a squad of miners spreads over marked tiles instead of clustering.</summary>
    [HideInInspector] public Drone miner;

    public bool Depleted => orbs <= 0f;

    public void Setup(int typ, float _orbs, float coef)
    {
        orbs = _orbs;
        initialOrbs = _orbs;
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

    /// <summary>Drone mining: eats the WHOLE node over <paramref name="secondsToEat"/> seconds of
    /// contact (fast, deliberate deconstruction) but credits only HALF the orb value — a drone
    /// chews twice the ore a building would for the same yield. Returns whole orbs released this
    /// tick (with the same ChipFX beat the buildings get). Pays out in orbs only — no debris chips.</summary>
    public int ChipDrone(float dt, float secondsToEat)
    {
        if (orbs <= 0f) return 0;
        float units = Mathf.Min(initialOrbs * dt / Mathf.Max(0.1f, secondsToEat), orbs);
        orbs -= units;
        droneYield += units * 0.5f;
        int give = Mathf.FloorToInt(droneYield);
        droneYield -= give;
        if (orbs <= 0f) StartCoroutine(Des());
        if (give > 0)
            Instantiate(Resources.Load("ChipFX"), transform.position, Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)), transform);
        return give;
    }

    private IEnumerator Des()
    {
        yield return null;
        TilemapResource.m[orbType].SetTile(TilemapResource.m[orbType].WorldToCell(transform.position), null);
    }

}
