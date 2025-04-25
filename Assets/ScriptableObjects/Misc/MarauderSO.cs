using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "Data", menuName = "ScriptableObjects/MarauderSO", order = 1)]
public class MarauderSO : ScriptableObject
{
    public GameObject prefab;
    public int rarity;
    public int price;
    public bool isBuilding;
    public Vector2 xOffset;
    public Vector2 yOffset;

    public Vector2 NewPosition()
    {
        return new Vector2(Random.Range(xOffset.x, xOffset.y), Random.Range(yOffset.x, yOffset.y));
    }
}
