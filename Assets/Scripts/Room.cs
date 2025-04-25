using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Random = UnityEngine.Random;


public class Room : MonoBehaviour, IRoom
{
    public int makeDungeonPoints = 0; //for spawning room, 0 for corridor.
    public float dp; //dungeon points for spawning enemies
    private float initWaveDp;
    public int waves = 1; // set to zero to ignore spawnEnemies and skip straight to on defeat
    private int spind = 0; // spawnpoint index;
    public float spawnTCoef = 0.5f;
    public bool defeated = false;
    public float hasCharacter = -1f;
    public float darkness = 1f;
    public Transform safeSpawn;

    public List<MarauderSO> enemySOs;
    public List<GameObject> alives;
    public List<Door> doors;
    public Transform[] sp; //spawnpoints
    [HideInInspector]
    public Collider2D col;
    private SpriteRenderer sr;
    private Coroutine spawnenemies;

    [HideInInspector]
    public EmbersEdge EE;
    [HideInInspector]
    public float EEVal;
    
    [HideInInspector]
    public bool bossRoom = false;
    public Transform icon;

    public void GetIcon()
    {
        if(icon == null || icon.gameObject.activeInHierarchy)
        {
            return;
        }
        icon.transform.parent = transform.parent.parent;
        icon.gameObject.SetActive(true);
    }

    private void Awake()
    {
        transform.position = new Vector3(transform.position.x, transform.position.y, -1);
        doors = doors.OrderBy(x => Random.value).ToList();
        col = GetComponent<CompositeCollider2D>();
        int newWaves = Mathf.Max(Random.Range(waves - 1, waves + 1),1);
        if(waves > 0)
        {
            initWaveDp = dp/GS.Sigma(newWaves);
        }
        if(newWaves < waves)
        {
            dp *= 1 - 1f / GS.Sigma(waves);
            dp = Mathf.RoundToInt(dp);
        }
        waves = newWaves;
        sr = GetComponent<SpriteRenderer>();
        if (icon != null)
        {
            icon.rotation = Quaternion.identity;
            icon.GetComponent<SpriteRenderer>().color = GS.ColFromEra();
        }
    }

    private void OnEnable()
    {
        StartCoroutine(FadeIn());
    }

    public bool BoundsCheck(List<Room> rs)
    {
        Awake();
        foreach(Room R in rs)
        {
            if (R.col.bounds.Intersects(col.bounds))
            {
                return false;
            }
        }
        return true;
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.name == "Character")
        {
            hasCharacter = Time.realtimeSinceStartup;
        }
    }

    public virtual void OnEnter() //called from doors
    {
        DM.i.activeRoom = this;
       
        if (!defeated)
        {
            if (!bossRoom && EE != null)
            {
                CameraScript.QuickLeanDistort(CameraScript.dungDistort + 0.05f,0.84f);
            }
            CharacterScript.speedy = false;
            SortIntersects(false);
            CloseDoors();
            if(EE != null)
            {
                PortalScript.i.NoPortal();
            }
            spawnenemies = StartCoroutine(SpawnEnemies());
        }
        else
        {
            OpenDoors(CharacterScript.CS.keys);
        }
        PortalScript.i.UpdateCamera();
        AllyAI.SkrDaHomies(safeSpawn.position);
        foreach (Door d in doors)
        {
            if (d.room1 != this)
            {
                d.room1.GetIcon();
            }
            if (d.room2 != this)
            {
                d.room2.GetIcon();
            }
        }
        MapManager.i.SnapShotDungeon();
    }

    public virtual void OnDefeat()
    {
        CameraScript.i.StopShake();
        CharacterScript.speedy = true;
        if (!defeated && makeDungeonPoints != 0)
        {
            //Loot
            // float[] lucks = new float[Mathf.Max(makeDungeonPoints + Random.Range(0, 2), 2)]; //chance to add 1
            // float affinity = 1 + (initWaveDp * GS.Sigma(waves) / DM.GetStandardDPFromEra());
            // affinity = 0.31f + Mathf.Log(affinity); //such that standard number of dps gives multiplier of 1, 2 x gives 1.41 and 0.5 x gives 0.71
            //
            // if (lucks.Length == 2)
            // {
            //     lucks[0] = 0.25f * affinity;
            //     lucks[1] = 0.25f * affinity; //if 2 have equal luck
            // }
            // else //otherwise scale luck over range
            // {
            //     float range = 2 * affinity;
            //     for (int i = 0; i < lucks.Length; i++)
            //     {
            //         lucks[i] = Mathf.Lerp(0f, 0.25f * range, (float)i / (lucks.Length - 1));
            //     }
            // }
            if (!MechaSuit.lastlife)
            {
                SpawnChest();
            }
            else
            {
                AfterLootSelect();
            }
        }
        else
        {
            AfterLootSelect();
        }
        if(icon!= null)
        {
            Destroy(icon.gameObject);
            MapManager.i.SnapShotDungeon();
        }
        defeated = true;
    }

    public void AfterLootSelect()
    {
        OpenDoors(CharacterScript.CS.keys);
        if (EE != null)
        {
            EE.transform.parent = GS.FindParent(GS.Parent.ee);
            MapManager.BeginPlace(EE,MechaSuit.lastlife);
        }
        else
        {
            PortalScript.i.YesPortal();
        }

        if (MechaSuit.lastlife)
        {
            IM.i.pi.Player.LockMap.Disable();
            PortalScript.i.Portal(EE != null);
        }
    }

    public virtual IEnumerator SpawnEnemies()
    {
        yield return new WaitForSeconds(0.75f);
        if (EE != null)
        {
            if (bossRoom)
            {
                CameraScript.QuickLeanDistort(CameraScript.dungDistort + 0.05f, 0.85f);
                Destroy(EE);
            }
            EE.StartCoroutine(EE.Acco());
        }
        int waveN = 0;
        while(waveN < waves)
        {
            waveN++;
            dp = waveN * initWaveDp;
            while(dp > 0)
            {
                bool any = false;
                enemySOs = enemySOs.OrderBy(x => Random.value).ToList();
                foreach (MarauderSO so in enemySOs)
                {
                    if(dp - so.price >= 0)
                    {
                        any = true;
                        if (Random.Range(0, so.rarity) == 0)
                        {
                            dp -= so.price;
                            SpawnEnemy(so.prefab, (Vector2)sp[spind].position);
                            spind++;
                            if (spind >= sp.Length)
                            {
                                spind = 0;
                            }
                            if(spawnTCoef > 0f)
                            {
                                yield return new WaitForSeconds(Mathf.Pow(so.price, 0.6f) * Random.Range(0.66f * spawnTCoef, 1.5f * spawnTCoef));
                            }
                        }
                    }
                }
                if(any == false)
                {
                    break;
                }
            }
            while (alives.Count > 0)
            {
                yield return null;
                InformPositions();
                for (int i = 0; i < alives.Count; i++)
                {
                    if (alives[i] == null)
                    {
                        alives.RemoveAt(i);
                    }
                }
            }
            for(int i = 0; i < 5; i++)
            {
                GS.GatherResources(transform);
                yield return new WaitForSeconds(0.3f);
            }
        }
        OnDefeat();
    }

    void InformPositions()
    {
        //Group enemies of the same kind together
    }

    private void CloseDoors()
    {
        foreach(Door d in doors)
        {
            d.CloseDoor();
        }
    }

    private void SpawnEnemy(GameObject prefab, Vector2 position)
    {
        if (EE != null)
        {
            alives.Add(EE.SpawnEnemy(prefab, position)); //effect from EE
        }
        else
        {
            alives.Add(Instantiate(prefab, position, Quaternion.identity, GS.FindParent(GS.Parent.enemies))); //effect show direction to closest EE.
        }
        foreach (LifeScript l in alives[^1].GetComponentsInChildren<LifeScript>())
        {
            l.orbSpawnPlace = transform;
        }
        foreach (ILA ila in alives[^1].GetComponentsInChildren<ILA>())
        {
            ila.UpdateCoef(darkness);
        }
        if (alives[^1].TryGetComponent<IRoomUnit>(out var ru))
        {
            ru.RecieveRoom(col, transform.position);
        }
    }

    private void OpenDoors(List<string> keys)
    {
        SortIntersects(false);
        foreach(Door d in doors)
        {
            d.OpenDoor(keys);
        }
    }

    public void InitDoors()
    {
        foreach(Door d in doors)
        {
            if(d.room1 == this)
            {
                d.transform.rotation *= Quaternion.Euler(0f, 0f, -2*transform.rotation.eulerAngles.z);
            }
        }
    }

    public bool SortIntersects(bool makeEE)
    {
        //foreach(Collider2D col in PIs)
        //{
        //    col.isTrigger = false;
        //    Destroy(col.GetComponent<PossibleIntersect>());
        //}
        while (doors.Contains(null))
        {
            doors.Remove(null);
        }
        if (makeEE && EE == null && makeDungeonPoints != 0 && !name.Contains("Connector"))
        {
            if (!bossRoom)
            {
                EE = Instantiate(Resources.Load<GameObject>("EmbersEdge").GetComponent<EmbersEdge>(), transform.position, Quaternion.identity, transform);
            }
            int n = Mathf.CeilToInt(0.01f + EEVal * 2.99f);
            var msos = new MarauderSO[n];
            for (int i = 0; i < n; i++)
            {
                msos[i] = enemySOs[Random.Range(0, enemySOs.Count)];
            }
            if (!bossRoom)
            {
                EE.Activate(EEVal, msos);
            }

            if (icon != null)
            {
                if(icon.name.Contains("EEMapCon") || bossRoom)
                {
                    return true;
                }
                Destroy(icon.gameObject);
            }
            icon = Instantiate(Resources.Load<GameObject>("EEMapCon"), transform.position, Quaternion.identity, transform).transform;
            icon.GetComponent<SpriteRenderer>().color = GS.ColFromEra();
            return true;
        }
        return false;
    }

    private IEnumerator FadeIn()
    {
        sr.color = new Color(1, 1, 1, 0);
        while (sr.color != Color.white)
        {
            sr.color = Color.Lerp(sr.color, Color.white, Mathf.Min(1,7f * Time.deltaTime));
            yield return null;
        }
    }

    
    public virtual void ResetRoom()
    {
        if (!defeated)
        {
            if (spawnenemies != null)
            {
                StopCoroutine(spawnenemies);
                StartCoroutine(ResetRoomI());
            }
            if (EE != null)
            {
                EE.StartCoroutine(EE.DeAcco());
            }
            else
            {
                if (icon.gameObject != null)
                {
                    Destroy(icon.gameObject);
                    MapManager.i.SnapShotDungeon();
                }
                defeated = true;
            }
        }
    }
    

    public IEnumerator ResetRoomI()
    {
        yield return null;
        yield return null;
        foreach (GameObject g in alives)
        {
            Destroy(g);
        }
        alives = new List<GameObject>();
        GS.DestroyResources(transform);
    }

    protected void SpawnChest()
    {
        Instantiate(SpawnManager.instance.chests[Random.Range(0,5)], GS.CS().position, Quaternion.identity, GS.FindParent(GS.Parent.misc));
    }

    public Room GetRoom()
    {
        return this;
    }
}