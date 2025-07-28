using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class TilemapCorruption : MonoBehaviour
{
    public static TilemapCorruption i;
    public Tilemap map;
    public Tilemap extras;
    [SerializeField] Tile[] tiles1;
    [SerializeField] Tile[] tiles2;
    [SerializeField] Tile[] tiles3;
    [SerializeField] LootBox lootbox;
    Tile[][] totTiles;

    [SerializeField] AnimatedTile[] aTiles;
    [SerializeField] private GameObject[] aTileFX;
    private Dictionary<Vector3Int, GameObject> fx;
    
    [SerializeField] GameObject[] en;

    [SerializeField] GameObject connectBasePrefab;

    [HideInInspector] public GameObject connectBase;

    [SerializeField] GameObject[] poof;

    [SerializeField] private Sprite[] floorsprs;
    [SerializeField] private SpriteRenderer floorsr;


    private void Awake()
    {
        totTiles = new Tile[][] { tiles1, tiles2, tiles3 };
        GS.OnNewEra += ctx => StartCoroutine(SetBackground());
        fx = new Dictionary<Vector3Int, GameObject>();
        i = this;
    }

    private IEnumerator Start()
    {
        yield return StartCoroutine(SetBackground());
        GS.OnNewEra += _ => { if (connectBase != null) { Destroy(connectBase); }};
    }

    private IEnumerator SetBackground()
    {
        floorsr.sprite = floorsprs[GS.era];
        map.ClearAllTiles();
        extras.ClearAllTiles();
        foreach (var keyValuePair in fx)
        {
           Destroy(keyValuePair.Value);
        }
        fx.Clear();
        yield return null;
        Vector3Int v;
        Tile[] tiles = totTiles[GS.era];
        for(int x = -40; x < 40; x++)
        {
            yield return null;
            for (int y = -40; y < 40; y++)
            {
                v = new Vector3Int(x, y,0);
                switch (v.magnitude)
                {
                    case <= 16:
                        map.SetTile(v, tiles[0]);
                        break;
                    case <= 18:
                        if (Random.Range(0, 2) == 0)
                        {
                            map.SetTile(v, tiles[0]);
                        }
                        else
                        {
                            map.SetTile(v, tiles[Random.Range(1, 5)]);
                        }
                        break;
                    case <= 20:
                        map.SetTile(v, tiles[Random.Range(1, 5)]);
                        break;
                    case <= 22:
                        map.SetTile(v, tiles[Random.Range(1, 9)]);
                        break;
                    case <= 24:
                        map.SetTile(v, tiles[Random.Range(5, 9)]);
                        break;
                    case <= 26:
                        map.SetTile(v, tiles[Random.Range(5, 13)]);
                        break;
                    case <= 28:
                        map.SetTile(v, tiles[Random.Range(9, 13)]);
                        break;
                    case <= 30:
                        map.SetTile(v, tiles[Random.Range(9, 17)]);
                        break;
                    case <= 32:
                        map.SetTile(v, tiles[Random.Range(13, 17)]);
                        break;
                    case <= 34:
                        map.SetTile(v, tiles[Random.Range(13, 21)]);
                        break;
                    default:
                        map.SetTile(v, tiles[Random.Range(17, 21)]);
                        break;
                }
            }
        }
        SetExtras();
    }

    private void SetExtras()
    {
        Color col = GS.ColFromEra();
        while(col.maxColorComponent < 1f)
        {
            col = new Color(col.r * 1.04f, col.g * 1.04f, col.b * 1.04f);
        }
        extras.color = col;
        int[] ns = GS.era switch
        {
            0 => new int[] { 3, 2, 1, 1, 1 }, //Small Threat, Random, Big Threat, Signal, Bonus
            1 => new int[] { 5, 3, 2, 2, 1 },
            _ => new int[] { 7, 4, 3, 3, 1 },
        };
        Vector3Int v;
        Vector2 v2;
        for (int i = 0; i < ns.Length; i++)
        {
            for(int n = 0; n < ns[i]; n++)
            {
                for(int trY = 0; trY < 10; trY++) //makes sure the spawned extras are not within the maps boundaries
                {
                    v2 = GS.RandCircleV2(9.51f + GS.era * 1.5f + i, 10.49f + GS.era * 3 + i);
                    v = new Vector3Int(Mathf.RoundToInt(v2.x), Mathf.RoundToInt(v2.y));
                    if (MapManager.InsideBounds(extras.CellToWorld(v),true))
                    {
                        continue;
                    }
                    extras.SetTile(v, aTiles[i]);
                    if (!fx.ContainsKey(v))
                    {
                        fx.Add(v,Instantiate(aTileFX[i], extras.CellToWorld(v) + new Vector3(0.5f,0.5f,0f), aTileFX[i].transform.rotation, GS.FindParent(GS.Parent.fx)));
                    }
                    break;
                }
            }
        }
    }

    public void Event(Vector3Int position, TileBase t)
    {
        extras.SetTile(position, null);
        if (fx.ContainsKey(position))
        {
            Destroy(fx[position]);
            fx.Remove(position);
        }
        Vector2 p = extras.CellToWorld(position);
        p = p.normalized * (p.magnitude - 0.75f);

        switch (t.name)
        {
            case "Signal":
                Signal(p);
                Instantiate(poof[0], p, Quaternion.identity, null);
                break;
            case "Random":
                Rand(p);
                Instantiate(poof[1], p, Quaternion.identity, null);
                break;
            case "SmallThreat":
                StartCoroutine(Threat(true, p));
                Instantiate(poof[2], p, Quaternion.identity, null);
                break;
            case "BigThreat":
                StartCoroutine(Threat(false, p));
                Instantiate(poof[3], p, Quaternion.identity, null);
                break;
            case "Bonus":
                Bonus(p);
                Instantiate(poof[4], p, Quaternion.identity, null);
                break;
        }
    }

    private IEnumerator Threat(bool small, Vector2 p)
    {
        yield return new WaitForSeconds(5f);
        Finder.TurnOnTurrets();
        switch (GS.era)
        {
            case 0:
                if (small)
                {
                    for (int i = 0; i < 5; i++)
                    {
                        SpawnManager.instance.alives.Add(Instantiate(en[0], p + Random.insideUnitCircle, GS.RandRot(), GS.FindParent(GS.Parent.enemies)));
                        yield return new WaitForSeconds(0.25f);
                    }
                    for (int i = 0; i < 3; i++)
                    {
                        SpawnManager.instance.alives.Add(Instantiate(en[1], p + Random.insideUnitCircle, GS.RandRot(), GS.FindParent(GS.Parent.enemies)));
                        yield return new WaitForSeconds(0.25f);
                    }
                }
                else
                {
                    SpawnManager.instance.alives.Add(Instantiate(en[2], p + Random.insideUnitCircle, GS.RandRot(), GS.FindParent(GS.Parent.enemies)));
                    yield return new WaitForSeconds(1.5f);
                    for (int i = 0; i < 1; i++)
                    {
                        SpawnManager.instance.alives.Add(Instantiate(en[3], p + Random.insideUnitCircle, GS.RandRot(), GS.FindParent(GS.Parent.enemies)));
                    }
                    yield return new WaitForSeconds(2.5f);
                    for (int i = 0; i < 2; i++)
                    {
                        SpawnManager.instance.alives.Add(Instantiate(en[2], p + Random.insideUnitCircle, GS.RandRot(), GS.FindParent(GS.Parent.enemies)));
                        yield return new WaitForSeconds(1f);
                    }
                }
                break;

            case 1:
                if (small)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        SpawnManager.instance.alives.Add(Instantiate(en[4], p + Random.insideUnitCircle, GS.RandRot(), GS.FindParent(GS.Parent.enemies)));
                        yield return new WaitForSeconds(0.1f);
                    }
                    yield return new WaitForSeconds(2f);
                    for (int i = 0; i < 2; i++)
                    {
                        SpawnManager.instance.alives.Add(Instantiate(en[5], p + Random.insideUnitCircle, GS.RandRot(), GS.FindParent(GS.Parent.enemies)));
                    }
                }
                else
                {
                    for (int i = 0; i < 1; i++)
                    {
                        SpawnManager.instance.alives.Add(Instantiate(en[7], p + Random.insideUnitCircle, GS.RandRot(), GS.FindParent(GS.Parent.enemies)));
                    }
                    yield return new WaitForSeconds(2f);
                    for (int i = 0; i < 1; i++)
                    {
                        SpawnManager.instance.alives.Add(Instantiate(en[6], p + Random.insideUnitCircle, GS.RandRot(), GS.FindParent(GS.Parent.enemies)));
                    }
                    yield return new WaitForSeconds(2.5f);
                    for (int i = 0; i < 1; i++)
                    {
                        SpawnManager.instance.alives.Add(Instantiate(en[8], p + Random.insideUnitCircle, GS.RandRot(), GS.FindParent(GS.Parent.enemies)));
                    }
                }
                break;

            default:
                if (small)
                {
                    for (int i = 0; i < 1; i++)
                    {
                        SpawnManager.instance.alives.Add(Instantiate(en[9], p, GS.RandRot(), GS.FindParent(GS.Parent.enemies)));
                        yield return new WaitForSeconds(2f);
                    }
                    for (int i = 0; i < 1; i++)
                    {
                        SpawnManager.instance.alives.Add(Instantiate(en[10], p, GS.RandRot(), GS.FindParent(GS.Parent.enemies)));
                        yield return new WaitForSeconds(2f);
                    }
                }
                else
                {
                    for (int i = 0; i < 1; i++)
                    {
                        SpawnManager.instance.alives.Add(Instantiate(en[12], p, GS.RandRot(), GS.FindParent(GS.Parent.enemies)));
                        yield return new WaitForSeconds(2f);
                    }
                    for (int i = 0; i < 1; i++)
                    {
                        SpawnManager.instance.alives.Add(Instantiate(en[11], p, GS.RandRot(), GS.FindParent(GS.Parent.enemies)));
                        yield return new WaitForSeconds(2f);
                    }
                }
                break;
        }
      
    }

    private void Rand(Vector2 p)
    {
        int r = Random.Range(0, 4);
        if( r == 0)
        {
            int[] orbs = GS.era switch
            {
                0 => new int[] { 25, 5, 2, 1 },
                1 => new int[] { 40, 15, 3, 1 },
                _ => new int[] { 55, 20, 20, 5 },
            };
            GS.CallSpawnOrbs(p, orbs);
        }
        else if( r == 1)
        {
            StartCoroutine(Threat(true, p));
            CM.Convo(CM.CT.UnluckySpawn);
        }
        else if(r == 2)
        {
            Bonus(p,true);
            CM.Convo(CM.CT.UnluckySpawn);
        }
        else
        {
            CM.Convo(CM.CT.UneventfulRandom);
        }
    }

    private void Bonus(Vector2 p, bool reducedP = false)
    {
        CM.Convo(CM.CT.Bonus);
        Instantiate(lootbox, p, Quaternion.identity, GS.FindParent(GS.Parent.misc));
    }

    private void Signal(Vector2 p)
    {
        connectBase = Instantiate(connectBasePrefab, p, Quaternion.identity, GS.FindParent(GS.Parent.misc));
    }

}
