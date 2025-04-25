using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Pool;

public class StatusManager : MonoBehaviour
{
    [SerializeField] private Sprite[] statusSprites;
    [SerializeField] private Sprite[] borderSprites;
    [SerializeField] private Transform statusParent;

    public static List<Unit> staticUnits = new List<Unit>();
    static Dictionary<int, List<UnitSample>> staticSamples = new Dictionary<int, List<UnitSample>>(); //This is necessary for the way status onDisable is used to deinitialise the static unit (rather than adding an IonDeath or clunky lifescript hack)

    public static bool lightning = false;
    private int count;
    private int countDemat;

    private static int timeID;

    public static StatusManager i;

    private static List<Unit> lightningTargets = new List<Unit>();
    private static List<Unit> nodedUnits = new List<Unit>();
    
    private static int totalLightningTargets;
    private const float lightningLinkDistance = 6f;
        
    [SerializeField]  LineRenderer lrPrefab;
    private List<List<LineRenderer>> lrs;
    
    [SerializeField] private Status statusPrefab;
    public ObjectPool<Status> statusPool;

    [SerializeField] private Follower demFX;
    [SerializeField] private GameObject dematerial;

    [SerializeField] private Follower charmFX;

    private static List<(LifeScript,GameObject)> dematerialsing = new List<(LifeScript,GameObject)>();
    private static List<(int, GameObject)> charmed;

    [SerializeField] private Follower leechFX;
    private static List<(Unit, GameObject, LineRenderer)> leeched;
    [SerializeField] private LineRenderer leechLR;
    
    private void Awake()
    {
        lightning = false;
        lightningTargets = new List<Unit>();
        nodedUnits = new List<Unit>();
        i = this;
        Status.statusSprites = statusSprites;
        Status.borderSprites = borderSprites;
        dematerialsing = new List<(LifeScript,GameObject)>();
        leeched = new List<(Unit, GameObject,LineRenderer)>();
        charmed = new List<(int, GameObject)>();
        staticUnits = new List<Unit>();
        statusPool = new ObjectPool<Status>(() => Instantiate(statusPrefab,statusParent), status =>
        {
            status.ind = -1; 
            status.gameObject.SetActive(true);
            status.enabled = true;
        }, status => {status.gameObject.SetActive(false);}, status => {Destroy(status.gameObject);},true,999);
    }

    public void StartLeech(Unit u)
    {
        u.leechTime = Time.time;
        var f = Instantiate(leechFX, u.transform.position, Quaternion.Euler(90f,0f,0f), u.transform);
        f.t = u.stati.First(x => x.ind == 14).transform;
        var lr = Instantiate(leechLR, Vector3.zero, Quaternion.identity,GS.FindParent(GS.Parent.misc));
        lr.SetPosition(0,GS.CS().position);
        lr.SetPosition(1,u.transform.position);
        leeched.Add((u,f.gameObject,lr));
    }

    public static void RemoveLeech(Unit u) //no stress if called twice due to time out and distance, but should be disabled in that case
    {
        for (int i = 0; i < leeched.Count; i++)
        {
            if (leeched[i].Item1 != u) continue;
            Destroy(leeched[i].Item2);
            Destroy(leeched[i].Item3.gameObject);
            leeched.RemoveAt(i);
            break;
        }
    }
    
    private void Update()
    {
        for(int i = 0; i < leeched.Count; i++)
        {
            var x = leeched[i];
            float dist = Vector2.Distance(GS.CS().transform.position,x.Item1.transform.position);
            if(dist > 5f)
            {
                var stat = x.Item1.stati.First(z=> z.ind == 14);
                stat.Dissapear();
                x.Item1.StatusComplete(14);
                i--;
                continue;
            }

            float prevDist = dist;
            
            dist = 6 - Mathf.Clamp(dist, 2f, 4f); //4 - 2; closer is higher
            x.Item3.startColor = new Color(dist*0.25f, dist *0.25f, dist*0.25f, (5f-prevDist) * 0.2f);
            float tim = (Time.time - x.Item1.leechTime) * 0.25f;
            x.Item3.endColor = new Color(tim, tim, tim, tim);
            
            x.Item3.SetPosition(0,GS.CS().position);
            x.Item3.SetPosition(1,x.Item1.transform.position);

            float val = Time.deltaTime * dist * tim * GS.Era1(); //Do more dmg over time, and more dmg the closer you are
            x.Item1.ls.Change(-val,1);
            CharacterScript.CS.ls.Change(val,1);
        }
       
    }

    
    public static void StartLightning(Unit u)
    {
        if (lightning)
        {
            return;
        }

        u.staticEffectActivated = true;
        u.stati.First(x => x.ind == 15).sliderValue = 1;
        u.AS.prepared = false;
        Transform z = u.stati.First(x => x.ind == 15).transform;
        Instantiate(Resources.Load<GameObject>("StaticFX"), u.transform.position,Quaternion.Euler(90f,0f,0f), u.transform).GetComponent<Follower>().t = z;
        u.ls.ChangeOverTime(-0.4f * u.ls.maxHp,5f,2);

        lightning = true;
        //rememeber to apply staticEffectActivated bool to each unit;
        //Determine Order of Units to be struck

        foreach (Unit t in staticUnits) //1ST GET CLOSEISH UNITS - NOT ACROSS THE DAMN MAP
        {
            if(Vector2.Distance(t.transform.position,u.transform.position) < 50f)
            {
                lightningTargets.Add(t);
            }
        }

        #region 1,2,3 Length Cases
        if (lightningTargets.Count == 1) //min 1 cos root node included in staticUnits
        {
            GS.Stat(u,"static",1,1);
        }
        
        //timeID = SpawnManager.instance.NewTS(0.5f,6f);
        
        if(lightningTargets.Count == 2)
        {
            Unit notRoot = lightningTargets[0] == u ? lightningTargets[1] : lightningTargets[0];
            Node root = new Node(u, notRoot);
            Node leaf = new Node(notRoot);
            i.StartCoroutine(i.LightningI(new List<List<Node>>{new List<Node>{root}, new List<Node>{leaf}}));
            return;
        }
        
        if(lightningTargets.Count == 3)
        {
            Node root;
            Node leafOne;
            Node leafTwo;
            if (u == lightningTargets[0])
            {
                root = new Node(u, lightningTargets[1], lightningTargets[2]);
                leafOne = new Node(lightningTargets[1]);
                leafTwo = new Node(lightningTargets[2]);
            }
            else if (u == lightningTargets[1])
            {
                root = new Node(u, lightningTargets[0], lightningTargets[2]);
                leafOne = new Node(lightningTargets[0]);
                leafTwo = new Node(lightningTargets[2]);
            }
            else
            {
                root = new Node(u, lightningTargets[0], lightningTargets[1]);
                leafOne = new Node(lightningTargets[0]);
                leafTwo = new Node(lightningTargets[1]);
            }
            i.StartCoroutine(i.LightningI(new List<List<Node>>{new List<Node>{root}, new List<Node>{leafOne, leafTwo}}));
            return;
        }
        #endregion

        totalLightningTargets = lightningTargets.Count;

        //SO, IN ORDER: 
        //1. GET TWO CLOSEST UNITS (DISTANCE LIMITS BASED ON HOW MANY UNITS ARE LEFT, TO PREVENT BACKSTEPPING)
        //2. IF THEY ARE MORE THAN 120 DEGREES APART, SPLIT, TWO CHILD NODES. OTHERWISE CONTINUE.
        //3. FOR EACH NODE IN EACH LEVEL OF THE TREE DO 1&2 AGAIN
        //4. SORT OUT THE REMAINING UNITS BY FINDING THE CLOSEST UNIT TO THEM, UPDATING THE CORRESPONDING NODE TO INCLUDE
        //THE NEW CONNECTION, AND ADD A NEW LEAF NODE IN THE ORDER BELOW THE PARENT NODE. EXTEND THE TREE IF NECESSARY.
        //5. CALL THE LIGHTNINGI COROUTINE TO DO THE LIGHTNING EFFECT WITH THE GIVEN TREE
        
        List<List<Node>> tree = new List<List<Node>>();
        tree.Add(new List<Node>{new Node(u)});
        nodedUnits.Add(u);
        lightningTargets.Remove(u);
        int order = 0;
        
        while (true) //Build tree which can only fork in two directions
        {
            tree.Add(new List<Node>());
            for (int i = 0; i < tree[order].Count; i++) 
            {
                (Unit a,Unit b) = GetClosestTwoUnits(tree[order][i].u);
                if(a == null && b == null) continue;
                tree[order][i] = new Node(tree[order][i].u, a, b);
                tree[order + 1].Add(new Node(a));
                if(b == null) continue;
                tree[order + 1].Add(new Node(b));
            }
            if (tree[order + 1].Count == 0) //empty list at the end of the tree on purpose for final cleanup leaf nodes
            {
                break;
            }
            order++;
        }

        while (lightningTargets.Count > 0)
        {
            Unit unlinked = lightningTargets[0];
            if (unlinked == null)
            {
                lightningTargets.RemoveAt(0);
                continue;
            }
            Unit x = null;
            float distance = 999f;
            float buf;
           
            foreach(Unit c in nodedUnits)
            {
                if(c == unlinked) continue;
                buf = Vector2.Distance(c.transform.position, unlinked.transform.position);
                if (buf < distance)
                {
                    distance = buf;
                    x = c;
                }
            }
            //x determined, the closest noded unit to the unlinked. Now find the node in the tree that contains x and replace it with an extra connected node
            bool breakFlag = false;
            for (order = 0; order < tree.Count; order++)
            {
                if (breakFlag) break;
                for (int n = 0; n < tree[order].Count; n++)
                {
                    if (tree[order][n].u == x)
                    {
                        tree[order][n] = new Node(tree[order][n].u, tree[order][n].connectingUnits, unlinked);
                        tree[order+1].Add(new Node(unlinked)); //And add a new node to the next layer with the previously unlinked unit
                        nodedUnits.Add(unlinked);
                        lightningTargets.Remove(unlinked);
                        if (order + 1 == tree.Count - 1) //If the tree is extended, add a new layer
                        {
                            tree.Add(new List<Node>());
                        }
                        breakFlag = true;
                        break;
                    }
                }
            }
        }
        
        i.StartCoroutine(i.LightningI(tree));
    }

    //Remove from the lightning targets afterwards
    static (Unit,Unit) GetClosestTwoUnits(Unit u)
    {
        Unit closest1 = null;
        float d1 = Mathf.Lerp(2f,lightningLinkDistance, (float)lightningTargets.Count/totalLightningTargets); //reduce jump size as more units are linked to prevent backtracking, and to allow cleanup at the end to do its thing
        Unit closest2 = null;
        float d2 = Mathf.Lerp(2f,lightningLinkDistance, (float)lightningTargets.Count/totalLightningTargets);
        float buf;
        foreach (Unit x in lightningTargets)
        {
            if (x == null)
            {
                continue;
            }
            buf = Vector2.Distance(x.transform.position, u.transform.position);
            if (!(buf < d2)) continue;
            if (buf < d1)
            {
                d2 = d1;
                closest2 = closest1;
                d1 = buf;
                closest1 = x;
            }
            else
            {
                d2 = buf;
                closest2 = x;
            }
        }
        
        if (closest1 == null && closest2 == null) //Nothing within link distance away
        {
            return (null, null);
        }
        if (closest1 != null && closest2 == null) //Only one unit within link distance
        {   
            lightningTargets.Remove(closest1);
            nodedUnits.Add(closest1);
            return (closest1, null);
        }
        if (closest1 == null && closest2 != null)
        {
            throw new Exception("Closest2 not null, closest1 is null?");
        }
        
        Vector2 dir1 = (Vector2) closest1!.transform.position - (Vector2) u.transform.position;
        Vector2 dir2 = (Vector2) closest2!.transform.position - (Vector2) u.transform.position;

        if (Vector2.Angle(dir1, dir2) < 120f)
        {
            lightningTargets.Remove(closest1);
            nodedUnits.Add(closest1);
            return (closest1, null);
        }
        
        lightningTargets.Remove(closest1);
        nodedUnits.Add(closest1);
        lightningTargets.Remove(closest2);
        nodedUnits.Add(closest2);
        return (closest1, closest2);
    }

    //TO DO: REMOVE THE INIT UNIT FROM LIGHTNING THING AND MAKE SURE TO DRAW THE CONNECTION TO THE FIRST UNIT
    IEnumerator LightningI(List<List<Node>> tree)
    {
        lrs = new List<List<LineRenderer>>();
        LineRenderer extra = Instantiate(lrPrefab, Vector3.zero, Quaternion.identity);
        extra.SetPosition(0, tree[0][0].u.transform.position);
        
        for (int order = 0; order < tree.Count; order++)
        {
            lrs.Add(new List<LineRenderer>());
            for (int n = 0; n < tree[order].Count; n++)
            {
                foreach (Unit u in tree[order][n].connectingUnits)
                {
                    lrs[order].Add(Instantiate(lrPrefab, Vector3.zero, Quaternion.identity));
                    lrs[order][^1].SetPosition(0, tree[order][n].u.transform.position);
                }
            }
            float t = 0f;
            while (t < 0.1f)
            {
                t += Time.deltaTime;
                yield return null;
                int adjust = 0;
                for(int i = 0; i < tree[order].Count; i++)
                {
                    if (tree[order][i].connectingUnits.Count == 0) //IF LEAF, DOESN'T ADD TO LR LIST DOES IT MATE
                    {
                        adjust -= 1;
                        continue;
                    }
                    for (int j = 0; j < tree[order][i].connectingUnits.Count; j++)
                    {
                        try
                        {
                            if(tree[order][i].connectingUnits[j] == null || tree[order][i].u == null) continue;
                            lrs[order][i + j + adjust].positionCount++;
                            lrs[order][i + j + adjust].SetPosition(lrs[order][i+j+adjust].positionCount - 1, Vector2.Lerp(tree[order][i].u.transform.position, tree[order][i].connectingUnits[j].transform.position, t * 10f));
                        }
                        catch (Exception)
                        {
                            Debug.Log("Error:");
                            Debug.Log("i" + i);
                            Debug.Log("j" + i);
                            Debug.Log("order" + order);
                            Debug.Log("adjust" + adjust);
                            Debug.Log("number of lrs in order:" + lrs[order].Count);
                            Debug.Log("dad",tree[order][i].u.gameObject);
                            Debug.Log("child",tree[order][i].connectingUnits[j].gameObject);
                            Debug.Log("");
                            for (int a = 0; a < tree.Count; a++)
                            {
                                Debug.Log("order: " + a);
                                for (int b = 0; b < tree[a].Count; b++)
                                {
                                    Debug.Log("unit: ", tree[a][b].u.gameObject);
                                    foreach (var t1 in tree[a][b].connectingUnits)
                                    {
                                        Debug.Log("connects to: ", t1.gameObject);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            for(int i = 0; i < tree[order].Count; i++)
            {
                for (int j = 0; j < tree[order][i].connectingUnits.Count; j++)
                {
                    GS.Stat(tree[order][i].connectingUnits[j], "static", 1,1);
                }
            }
        }

        float tim = 0f;
        float width;
        LineRenderer buf;
        
        while (tim < 4f)
        {
            tim += Time.deltaTime;
            width = 0.1f * (1 - tim/4f);

            if (tree[0][0].u != null)
            {
                extra.positionCount++;
                extra.SetPosition(extra.positionCount-1,tree[0][0].u.transform.position);
                extra.startWidth = extra.endWidth = width;
            }
            
            for (int order = 0; order < tree.Count; order++)
            {
                int adjust = 0;
                for (int i = 0; i < tree[order].Count; i++)
                {
                    if(tree[order][i].connectingUnits.Count == 0)
                    {
                        adjust -= 1;
                        continue;
                    }
                    for (int j = 0; j < tree[order][i].connectingUnits.Count; j++)
                    {
                        buf = lrs[order][i + j + adjust];
                        buf.startWidth = buf.endWidth = width;
                        if(tree[order][i].connectingUnits[j] == null) continue;
                        var positionCount = buf.positionCount;
                        positionCount++;
                        buf.positionCount = positionCount;
                        buf.SetPosition(positionCount - 1,
                            tree[order][i].connectingUnits[j].transform.position);
                    }
                }
            }
            yield return null;
        }

        for(int x = 0; x < lrs.Count; x++)
        {
            for (int y = 0; y < lrs[x].Count; y++)
            {
                Destroy(lrs[x][y].gameObject);
            }
        }
        lrs.Clear();
        nodedUnits.Clear();
        Destroy(extra.gameObject);
        lightning = false;
       // SpawnManager.instance.CancelTS(timeID);
    }

    public static void StartDematerialise(Unit u)
    {
        Follower f = Instantiate(i.demFX, u.transform.position, Quaternion.Euler(90f,0f,0f), u.transform);
        f.t = u.stati.First(x => x.ind == 13).transform;
        dematerialsing.Add((u.ls,f.gameObject));
    }

    public static void StopDematerialise(Unit u)
    {
        foreach((LifeScript,GameObject) x in dematerialsing)
        {
            if (x.Item1 != u.ls) continue;
            Destroy(x.Item2);
            dematerialsing.Remove(x);
            break;
        }
    }

    private void FixedUpdate()
    {
        countDemat++;
        if (countDemat > 30)
        {
            countDemat -= 30;
            foreach ((LifeScript,GameObject) x in dematerialsing)
            {
                if (x.Item1 != null)
                {
                    x.Item1.Change(-Time.fixedDeltaTime * 30f,0,true,false);
                    Instantiate(dematerial, x.Item1.transform.position, quaternion.identity, GS.FindParent(GS.Parent.misc));
                }
            }
        }
        count++;
        if (count > 4) count -= 5; //12FPS @ 60FPS, (for 5 seconds)
        if (count != 0)
        {
            return;
        }
       
        foreach (Unit u in staticUnits)
        {
            var buf = u.GetInstanceID();
            if (u.staticEffectActivated)
            {
                if (staticSamples[buf].Count > 1)
                {
                    staticSamples[buf].RemoveAt(0);
                }
            }
            else
            {
                if (staticSamples[buf].Count > 60)
                {
                    staticSamples[buf].RemoveAt(60);
                }
                staticSamples[buf].Insert(0, new UnitSample(u));
            }
        }
    }
    
    private void LateUpdate()
    {
        if (!lightning) return;
        if (staticUnits.Count == 0) return;
        foreach (Unit u in staticUnits)
        {
            if (u.staticEffectActivated)
            {
                var buf = u.GetInstanceID();
                var transform1 = u.transform;
                transform1.position = staticSamples[buf][0].pos;
                transform1.rotation = staticSamples[buf][0].rot;
                u.sr.sprite = staticSamples[buf][0].s;
            }
        }
    }
    
    public static void AddStaticUnit(Unit u)
    {
        staticUnits.Add(u);
        staticSamples.Add(u.GetInstanceID(), new List<UnitSample>());
        staticSamples[u.GetInstanceID()].Add(new UnitSample(u));
    }

    public static void RemoveStaticUnit(Unit u)
    {
        staticUnits.Remove(u);
        staticSamples.Remove(u.GetInstanceID());
    }

    public void ConvertFX(Unit u)
    {
        Follower f = Instantiate(charmFX, u.transform.position, Quaternion.Euler(90f,45f,0f), u.transform);
        f.t = u.stati.First(x => x.ind == 16).transform;
        charmed.Add((u.GetInstanceID(), f.gameObject));
    }

    public void DeConvertFX(Unit u)
    {
        int inst = u.GetInstanceID();
        for(int i = 0; i < charmed.Count; i++)
        {
            if (charmed[i].Item1 == inst)
            {
                Destroy(charmed[i].Item2);
                charmed.RemoveAt(i);
                break;
            }
        }
    }
    
    //int -> Sprite, position, rotation. When accessing, use first element and then delete. 
    
    struct UnitSample
    {
        public Sprite s;
        public Vector3 pos;
        public Quaternion rot;

        public UnitSample(Unit u)
        {
            s = u.sr.sprite;
            var transform1 = u.transform;
            pos = transform1.position;
            rot = transform1.rotation;
        }
    }

    //Tree is a List<List<Nodes>> where each List<Node> represets a layer. Layer 0 is root node
    struct Node
    {
        public Unit u;
        public List<Unit> connectingUnits;

        //Base init to be overwritten if there are connections, stays as leaf node otherwise
        public Node(Unit u_)
        {
            u = u_;
            connectingUnits = new List<Unit>();
        }
        
        //Overwrite leaf node, write connections.
        public Node(Unit u_, Unit one, Unit two = null)
        {
            u = u_;
            connectingUnits = two != null ? new List<Unit>{one, two} : new List<Unit> { one };
        }
        
        //Replace node in cleanup for linking distant units
        public Node(Unit main, List<Unit> prev, Unit add)
        {
            u = main;
            connectingUnits = new List<Unit>();
            foreach (Unit u in prev)
            {
                connectingUnits.Add(u);
            }
            connectingUnits.Add(add);
        }
    }
}
