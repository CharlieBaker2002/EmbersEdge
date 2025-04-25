using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class TilemapResource : MonoBehaviour
{
    public static Tilemap[] m = new Tilemap[6];
    [SerializeField] Tilemap map;
    [SerializeField] RuleTile t;
    [SerializeField] int mapNumber;
    [SerializeField] Sprite[] allSprites;
    float valueCoef;
    [SerializeField] Vector2 distances; 
    [SerializeField] Vector2 batchSizes;
    [SerializeField] int batchN;
    [SerializeField] float diagonality = 0.8f;

    private void Awake()
    {
        if(mapNumber <= 3)
        {
            int[] buffer = new int[4];
            buffer[mapNumber] = 1;
            valueCoef = 1f / GS.CostValue(buffer);
        }
        m[mapNumber] = map;
    }

    private IEnumerator Start()
    {
        Sprite s;
        Vector3Int pos;
        float dec;
        float ang;
        Vector3Int r;
        Vector2 r2;
        for (int n = batchN; n > 0; n--)
        {
            dec = 1f - (float)n / (float)batchN;
            ang = Random.Range(80f * dec + mapNumber * 15, 360f - 80f * dec - mapNumber*15) * Mathf.Deg2Rad;
            pos = new Vector3Int(Mathf.RoundToInt(Mathf.Lerp(distances.x,distances.y,dec) * Mathf.Sin(ang)), Mathf.RoundToInt(Mathf.Lerp(distances.x, distances.y, dec) * Mathf.Cos(ang)));
            for(int i = 0; i < Mathf.RoundToInt(Mathf.Lerp(batchSizes.x,batchSizes.y,dec)); i++)
            {
                r2 = Random.insideUnitCircle.normalized * diagonality;
                r = new Vector3Int(Mathf.RoundToInt(r2.x), Mathf.RoundToInt(r2.y));
                if((pos + r).sqrMagnitude < Mathf.Pow(Mathf.Lerp(distances.x, distances.y, dec), 2))
                {
                    i--;
                    continue;
                }
                if (map.HasTile(pos + r))
                {
                    if (Random.Range(0, 4) != 0)
                    {
                        i--;
                    }
                    continue;
                }
                pos += r;
                map.SetTile(pos, t);
                s = map.GetSprite(pos);
                SetupOre(s, pos);
            }
            yield return null;
        }
        yield return null;
        if(mapNumber <= 3 && mapNumber != 0)
        {
            yield return new WaitForSeconds(mapNumber);
            for(int i = 0; i < mapNumber; i++)
            {
                Tilemap o = m[i];
                Vector3Int v;
                for (int x = -19; x <= 19; x++)
                {
                    for (int y = -19; y <= 19; y++)
                    {
                        v = new Vector3Int(x, y, 0);
                        if (map.HasTile(v) && o.HasTile(v))
                        {
                            o.SetTile(v, null);
                        }
                    }
                }
            }
        } 
    }

    private void SetupOre(Sprite s, Vector3Int pos)
    {
        switch(System.Array.IndexOf(allSprites,s))
        {
            case 0:
                SetOre(map.GetInstantiatedObject(pos), 300);
                break;
            case 1:
                SetOre(map.GetInstantiatedObject(pos), 240);
                break;
            case 2:
                SetOre(map.GetInstantiatedObject(pos), 200);
                break;
            case 3:
                SetOre(map.GetInstantiatedObject(pos), 220);
                break;
            case 4:
                SetOre(map.GetInstantiatedObject(pos), 130);
                break;
            case 5:
                SetOre(map.GetInstantiatedObject(pos), 210);
                break;
            case 6:
                SetOre(map.GetInstantiatedObject(pos), 200);
                break;
            case 7:
                SetOre(map.GetInstantiatedObject(pos), 250);
                break;
            case 8:
                SetOre(map.GetInstantiatedObject(pos), 260);
                break;
            case 9:
                SetOre(map.GetInstantiatedObject(pos), 250);
                break;
            case 10:
                SetOre(map.GetInstantiatedObject(pos), 200);
                break;
            case 11:
                SetOre(map.GetInstantiatedObject(pos), 130);
                break;
        }
    }

    private void SetOre(GameObject g, float orbs)
    {
        g.GetComponent<Ore>().Setup(mapNumber, orbs * valueCoef * 0.5f, 10 / valueCoef);
    }
}
