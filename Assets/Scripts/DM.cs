using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
public class DM : MonoBehaviour
{
    public static DM i;
    public GameObject[] r1;
    public GameObject[] r2;
    public GameObject[] r3;
    public Transform dungeonParent;
    GameObject[][] rss;
    private GameObject[] rsP; //room prefabs
    public GameObject[] bossRooms;
    public GameObject[] connectorRooms;
    public Room[] initR;
    public Room activeRoom;
    public int[] dungeonValues;
    
    public List<Room> rs; //rooms
    public static bool finishDungeon;
    [SerializeField] BossDoor[] bossdoors;

    private void Awake()
    {
        i = this;
        rss = new GameObject[][] { r1, r2, r3 };
    }

    private IEnumerator Start()
    {
        yield return new WaitForSeconds(1f);
        if (RefreshManager.i.STARTDUNGEON == 2)
        {
            SpawnManager.day = GS.daysforeraComplete[0];
            yield return new WaitForSeconds(1f);
            GS.IncrementEra();
            MakeDungeon(1);
            EmbersEdge.mainCore.lr.material = EmbersEdge.mainCore.mats[GS.era];
        }
        else if(RefreshManager.i.STARTDUNGEON == 3)
        {
            SpawnManager.day = GS.daysforeraComplete[0] + GS.daysforeraComplete[1];
            yield return new WaitForSeconds(1f);
            GS.IncrementEra();
            yield return new WaitForSeconds(3f);
            GS.IncrementEra();
            MakeDungeon(2);
            EmbersEdge.mainCore.lr.material = EmbersEdge.mainCore.mats[GS.era];
        }
        else
        {
            MakeDungeon(0);
        }
        GS.OnNewEra += ctx => MakeDungeon(ctx);
    }

    public void MakeDungeon(int era)
    {
        activeRoom = initR[era];
        StartCoroutine(WaitForDungeon());

        IEnumerator WaitForDungeon()
        {
            if(GS.Era1() != RefreshManager.i.STARTDUNGEON)
            {
                yield return new WaitForSeconds(2f);
            }
            CameraScript.i.CameraSequence(activeRoom.transform);
            StopAllCoroutines();
            StartCoroutine(MakeDungeonI(era));
            yield return null;
        }
    }

    private IEnumerator MakeDungeonI(int era)
    {
        for (int i = 0; i < rs.Count; i++)
        {
            if (rs[i] == initR[GS.era])
            {
                continue;
            }
            yield return null;
            Destroy(rs[i].transform.parent.gameObject);
        }
        rs.Clear();
        rs.Insert(0,initR[GS.era]);
        activeRoom = initR[GS.era];
        rsP = rss[era];
        int overlapCount = 0;
        int n = dungeonValues[era];
        float maxN = n;
        while (n > 0)
        {
            Door d = AvDoor();
            rs = rs.OrderBy(x => Random.value).ToList();
            if (d == null)
            {
                MakeDungeon(era);
                yield break;
            }
            Room r = Instantiate(GS.RE(rsP), new Vector3(d.transform.position.x,d.transform.position.y,0f), Quaternion.Euler(0, 0, -d.transform.rotation.eulerAngles.z), dungeonParent).GetComponentInChildren<IRoom>().GetRoom();
            if (r.BoundsCheck(rs) == false)
            {
                Destroy(r.transform.parent.gameObject);
                overlapCount++;
                if(overlapCount == 100)
                {
                    MakeDungeon(era);
                    yield break;
                }
                yield return null;
                continue;
            }
            r.EEVal = (float)(maxN - n) / maxN; //0-1
            r.dp *= 0.5f + r.EEVal; //0.5 - 1.5
            d.room2 = r;    
            r.doors.Add(d);
            rs.Add(r);
            r.InitDoors();
            n -= r.makeDungeonPoints;
            yield return null;
        }


        for (int i = 0; i < 100; i++)
        {
            if (i == 99)
            {
                if (GS.era != 0)
                {
                    CM.Convo(CM.CT.NotMeantToSee);
                }
                MakeDungeon(era);
                yield break;
            }

            Door dcon = AvDoor();
            if (dcon == null)
            {
                MakeDungeon(era); // no need to nest and shuffle list because size = 1 x 1. If no av door, remamke.
                yield break;
            }
            Room rcon = Instantiate(connectorRooms[era], dcon.transform.position, Quaternion.Euler(0, 0, -dcon.transform.rotation.eulerAngles.z), dungeonParent).GetComponentInChildren<IRoom>().GetRoom();
            if (rcon.BoundsCheck(rs) == false)
            {
                rs = rs.OrderBy(x => Random.value).ToList();
                Destroy(rcon.transform.parent.gameObject);
                yield return null;
                continue;
            }

            rs.Add(rcon);
            rcon.doors.Add(dcon);
            dcon.room2 = rcon;
            rs = rs.OrderBy(x => Random.value).ToList();
            break;
        }

        for (int i = 0; i < 100; i++)
        {
            Door dBoss = AvDoor();
            if(dBoss == null)
            {
                MakeDungeon(era);
                yield break;
            }
            Room rBoss = Instantiate(bossRooms[era], dBoss.transform.position, Quaternion.Euler(0, 0, -dBoss.transform.rotation.eulerAngles.z), dungeonParent).GetComponentInChildren<IRoom>().GetRoom();
            rBoss.bossRoom = true;
            if (rBoss.BoundsCheck(rs) == false)
            {
                rs = rs.OrderBy(x => Random.value).ToList();
                Destroy(rBoss.transform.parent.gameObject);
                yield return null;
                continue;
            }

            Vector3 v = dBoss.transform.position;
            Quaternion q = dBoss.transform.rotation;
            Room r = dBoss.room1;
            
            r.doors.Remove(dBoss);
            Destroy(dBoss.gameObject);

            dBoss = Instantiate(bossdoors[GS.era], v, q, r.transform);
            dBoss.transform.localScale = new Vector3(1, 1, 1);

            dBoss.room1 = r;
            dBoss.room2 = rBoss;
            r.doors.Add(dBoss);
            rBoss.doors.Add(dBoss);

            rs.Add(rBoss);
            rBoss.InitDoors();
            rBoss.EEVal = 3;

            rBoss.icon.transform.rotation = Quaternion.identity;
            break;
        }
        yield return new WaitForSeconds(0.25f);
        yield return null;
        CloseOffDoors();
        yield return new WaitForSeconds(0.25f);
        yield return null;
        yield return StartCoroutine(SortShrines());
        MapManager.i.SnapShotDungeon();
    }

    private Door AvDoor()
    {
        foreach(Room r in rs)
        {
            r.doors = r.doors.OrderBy(x => Random.value).ToList();
            foreach (Door d in r.doors)
            {
                if(d.room2 == null)
                {
                    return d;
                }
            }
        }
        return null;
    }

    private void CloseOffDoors()
    {
        bool continu = false;
        while (continu == false)
        {
            continu = true;
            List<Room> rBuffer = new List<Room>();
            foreach (Room r in rs)
            {
                if (r.makeDungeonPoints == 0)
                {
                    bool dontDestroy = false;
                    foreach (Door d in r.doors)
                    {
                        if (d.room2 != null && d.room2 != r)
                        {
                            dontDestroy = true;
                        }
                    }
                    if (dontDestroy == false)
                    {
                        continu = false;
                        rBuffer.Add(r);
                    }
                }
            }
            foreach (Room r in rBuffer)
            {
                rs.Remove(r);
            }
            while (rBuffer.Count > 0)
            {
                RemoveRoomRef(rBuffer[0]);
                Destroy(rBuffer[0].transform.parent.gameObject);
                rBuffer.RemoveAt(0);
            }
        }
        for (int i = 0; i < rs.Count; i++)
        {
            Room r = rs[i];
            bool cont = false;
            while (cont == false)
            {
                cont = true;
                for (int j = 0; j < r.doors.Count; j++)
                {
                    if(r.doors[j].room2 == null)
                    {
                        r.doors[j].CloseDoor();
                        if(r.doors[j].sr != null)
                        {
                            r.doors[j].sr.sprite = r.doors[j].WallSprite();
                        }
                        Destroy(r.doors[j]);
                        r.doors.RemoveAt(j);
                        cont = false;
                        break;
                    }
                }
            }
        }
        int[] EEEraCount = new int[] { 3, 7, 11 };
        int EEs = EEEraCount[GS.era];
        rs.Shuffle();
        foreach(Room r in rs)
        {
            if (r.name.Contains("Boss") || r.transform.parent.name.Contains("Boss"))
            {
                r.SortIntersects(true);
                continue;
            }
            if(EEs > 0)
            {
                if (r.SortIntersects(true))
                {
                    EEs -= 1;
                }
            }
            else
            {
                r.SortIntersects(false);
            }
        }
    }

    private void RemoveRoomRef(Room R)
    {
        foreach(Room r in rs)
        {
            foreach(Door d in r.doors)
            {
                if(d.room2 == R)
                {
                    d.room2 = null;
                }
            }
        }
    }

    private IEnumerator SortShrines()
    {
        int minValue = Mathf.RoundToInt(10 * Mathf.Pow(GS.era + 1, 1.3f));
        foreach(Room r in rs)
        {
            if (r.GetComponentInChildren<DInteractionSpawn>()!=null)
            {
                yield return null;
                minValue -= r.GetComponentInChildren<DInteractionSpawn>().Make();
            }
        }
        while (minValue > 0)
        {
            yield return null;
            bool br = true;
            foreach (Room r in rs)
            {
                if (r.GetComponentInChildren<DInteractionSpawn>() != null)
                {
                    minValue -= r.GetComponentInChildren<DInteractionSpawn>().Make();
                    br = false;
                    break;
                }
            }
            if (br)
            {
                break;
            }
        }
        CameraScript.i.EndInitSequence();
        yield return new WaitForSeconds(2.5f);
        while (!finishDungeon)
        {
            yield return null;
        }
        if (!RefreshManager.i.REVEALALLROOMS)
        {
            foreach (Room r in rs)
            {
                if (r != initR[GS.era])
                {
                    r.transform.parent.gameObject.SetActive(false);
                }
            }
        }
    }

    public void RevealRooms()
    {
        foreach (Room r in rs)
        {
             r.transform.parent.gameObject.SetActive(true);
        }
    }

    public static int GetStandardDPFromEra()
    {
        switch (GS.era)
        {
            case 0:
                return 20;
            case 1:
                return 45;
            case 2:
                return 50;
            default:
                throw new System.Exception("Era 4??");
        }
    }
}
