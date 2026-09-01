using System.Collections;
using UnityEngine;

public class Ore : MonoBehaviour
{
    private float units;
    public int element;
    private float chipCoef;
    private float initialUnits;  // captured at Setup so drone mining can pace by fraction-of-node
    private float droneYield;   // fractional units a mining drone has earned but not yet released
    /// <summary>Soft claim so a squad of miners spreads over marked tiles instead of clustering.</summary>
    [HideInInspector] public Drone miner;

    public bool Depleted => units <= 0f;

    public void Setup(int typ, float _units, float coef)
    {
        units = _units;
        initialUnits = _units;
        element = typ;
        chipCoef = coef;
    }

    public int Chip()
    {
        int prev = Mathf.FloorToInt(units);
        units -= chipCoef;
        if (units <= 0f)
        {
            StartCoroutine(Des());
            //Destroy(gameObject,1f);
        }
        if (prev - Mathf.FloorToInt(units) > 0)
        {
            Instantiate(MineField.ChipFxPrefab(), transform.position, Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)), transform);
        }
        return prev - Mathf.FloorToInt(units);
    }

    /// <summary>Drone mining: eats the WHOLE node over <paramref name="secondsToEat"/> seconds of
    /// contact (fast, deliberate deconstruction) but credits only HALF the value — a drone
    /// chews twice the ore a building would for the same yield. Returns whole units released
    /// this tick (with the same ChipFX beat the buildings get).</summary>
    public int ChipDrone(float dt, float secondsToEat)
    {
        if (units <= 0f) return 0;
        float bite = Mathf.Min(initialUnits * dt / Mathf.Max(0.1f, secondsToEat), units);
        units -= bite;
        droneYield += bite * 0.5f;
        int give = Mathf.FloorToInt(droneYield);
        droneYield -= give;
        if (units <= 0f) StartCoroutine(Des());
        if (give > 0)
            Instantiate(MineField.ChipFxPrefab(), transform.position, Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)), transform);
        return give;
    }

    private IEnumerator Des()
    {
        yield return null;
        TilemapResource.m[element].SetTile(TilemapResource.m[element].WorldToCell(transform.position), null);
    }

}
