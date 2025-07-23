using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using System.Linq;
using Random = UnityEngine.Random;

public class EmbersEdge : MonoBehaviour
{
    public static float warmUpTime = 20f;
    public static int currentCores = 1;

    public bool startAcco = false;
    public float refresh = 0.05f;

    public int N = 50;

    [SerializeField]
    private float fluidness = 0.1f;

    private float reduceFluidityOverTime;
    private float increaseHastinessOverTime;
    private float wavestart;
    
    public float hastiness = 0.13f;

    bool sinOnOneSide = true;

    Vector2[] posVels;
    [HideInInspector]
    public Vector3[] positions;
    bool busy = false;

    [Header("")]
    public LineRenderer lr;
    public Transform targetEnd;
    public Transform attachPoint;

    float size = 0.00825f;
    public Material[] mats;
    public GameObject ps;

    public MarauderSO[] SOs;

    private System.Action changeNOnDay;

    public bool InDungeon = true;

    private float spawnCoef;

    public static EmbersEdge mainCore;
    
    private float radStep;

    public bool finishedSpawning = true;
    public Vector2 shiftPos = Vector2.zero;
    [SerializeField] Camera cam;
    public RenderTexture textur = null;
    private int renderCount;

    public static bool fullCheck = true;
    public static Action EEExplodeEvent = () => { };
    public GameObject eeWaveCompleteFX;

    public float bias;
    
    public static bool CheckFinished()
    {
        foreach(EmbersEdge EE in SpawnManager.instance.EEs)
        {
            if (!EE.finishedSpawning)
            {
                return false;
            }
        }
        return true;
    }

    private void Awake()
    {
        posVels = new Vector2[N];
        positions = new Vector3[N];
        positions[0] = attachPoint.transform.position;
        Vector3 dir = (Vector2)attachPoint.transform.up * size;
        for (int i = 1; i < N; i++)
        {
            positions[i] = positions[i - 1] + dir + size * fluidness * (Vector3)Random.insideUnitCircle;
            dir = GS.Rotated(dir, Random.Range(0, fluidness * 15f), true);
            posVels[i] = Vector2.zero;
        }
        lr.positionCount = N;
        lr.SetPositions(positions);
        renderCount = currentCores;

        if (name == "MainCore")
        {
            size = 0.015f;
            spawnCoef = 1 + SetM.difficulty; //2-4;
            mainCore = this;
            GS.OnNewEra += _ =>
            {
                if (_ == 1)
                {
                    SOs = SpawnManager.instance.E2SOs;
                }
                else if (_ == 2)
                {
                    SOs = SpawnManager.instance.E3SOs;
                }
            };
        }
        else
        {
            var rt = Resources.Load<RenderTexture>("EERT");
            textur = new RenderTexture(rt);
            cam.targetTexture = textur;
        }

        if (startAcco)
        {
            StartCoroutine(Acco());
        }
    }

    /// <param name="scale">0 to 1</param>
    public void Activate(float scale, MarauderSO[] s)
    {
        size = Mathf.LerpUnclamped(0.006f, 0.01f, scale);
        ChangeN(Mathf.RoundToInt(25 + scale * 20));
        SOs = (MarauderSO[])s.Clone();
        lr.material = mats[GS.era];
        spawnCoef = 0.25f + 0.5f * scale + 0.5f * (SetM.difficulty - 1); //0.25 - 0.75f; (previously 1-2);
    }

    private void Start()
    {
        changeNOnDay = () => ChangeN(N + SpawnManager.day);
    }

    private void OnEnable()
    {
        StartCoroutine(IStart());
    }

    private void Update()
    {
        renderCount++;
        if (mainCore == this)
        {
            if (UIManager.i.telemode != UIManager.TeleMode.Core) return;
            if (renderCount >= 10)
            {
                Vector3 v = positions.Aggregate(Vector3.zero,(a, b) => a + b) / N;
                cam.transform.position = new Vector3(v.x,v.y,-2f);
                renderCount = 0;
            }
            return;
        }
        if (renderCount >= 10)
        {
            
            Vector3 v = positions.Aggregate(Vector3.zero,(a, b) => a + b) / N;
            cam.transform.position = new Vector3(v.x,v.y,-2f);
            renderCount = 0;
        }
        // if (this != mainCore && !InDungeon)
        // {
        //     cam.transform.position = new Vector3(positions[0].x, positions[0].y,-2f);
        //     renderCount += 1;
        //     if (renderCount % 6 == 0)
        //     {
        //         //cam.Render();
        //         renderCount -= 6;
        //     }
        // }
       
    }

    private void FixedUpdate()
    {
        if (reduceFluidityOverTime > 0f)
        {
            hastiness = Mathf.Lerp(hastiness, increaseHastinessOverTime,
                Time.fixedDeltaTime * 0.05f * fluidness * fluidness * Mathf.Sqrt(5f + Time.time - wavestart));
            fluidness = Mathf.Lerp(fluidness, reduceFluidityOverTime,
                Time.fixedDeltaTime * 0.05f * fluidness * fluidness * Mathf.Sqrt(5f + Time.time - wavestart));
        }
    }

    private IEnumerator IStart()
    {
        while (true)
        {
            Vector3[] initPositions = new Vector3[N];
            for (int i = 0; i < N; i++)
            {
                initPositions[i] = positions[i];
            }
            for (int repetitions = 0; repetitions < 6; repetitions++)
            {
                positions[^1] = targetEnd.position;
                for (int i = positions.Length - 2; i > 0; i--)
                {
                    positions[i] = positions[i + 1] + (positions[i] - positions[i + 1]).normalized * size;
                }
                positions[0] = attachPoint.position;
                for (int i = 1; i < positions.Length; i++)
                {
                    positions[i] = positions[i - 1] + (positions[i] - positions[i - 1]).normalized * size;
                }
                if (Vector2.Distance(positions[^1], targetEnd.position) < 0.05f)
                {
                    break;
                }
            }
            for (int i = 1; i < N; i++)
            {
                positions[i] = Vector2.Lerp(initPositions[i], positions[i], hastiness * Time.fixedDeltaTime * (N - 0.5f * i) / N);
                Vector2 tangent = (positions[i] - positions[i - 1]).normalized;
                if (tangent == Vector2.zero)
                {
                    continue;
                }
                tangent = new Vector2(tangent.y, -tangent.x) * size; //wibble wobble (1 - Mathf.Log(-0.1f * i)) 
                if (!sinOnOneSide)
                {
                    positions[i] += (Vector3)(fluidness * Mathf.Sin(hastiness * Time.time + i * 10 * Mathf.Deg2Rad * (1f + fluidness)) * tangent);
                }
                else
                {
                    positions[i] += (Vector3)(Mathf.Max(0f, fluidness * Mathf.Sin(hastiness * Time.time + i * 10 * Mathf.Deg2Rad * (1f + fluidness))) * tangent);
                }
            }
            lr.SetPositions(positions);
            while (refresh == -1f)
            {
                yield return null;
            }
            yield return new WaitForSeconds(refresh);
            if (Random.Range(0, 100) == 0)
            {
                targetEnd.localPosition = Random.insideUnitCircle;
            }
        }
    }

    public IEnumerator Randomise() //make shit go lil cray cray, hurt?
    {
        if (busy == true)
        {
            yield break;
        }
        busy = true;
        for (float t = 0f; t < 1.5f; t++)
        {
            Vector2 rand = Vector2.zero;
            for (int i = 1; i < positions.Length; i++)
            {
                rand += size * fluidness * Random.insideUnitCircle;
                positions[i] = (Vector2)positions[i] + rand;
            }
            yield return new WaitForFixedUpdate();
        }
        busy = false;
    }

    public IEnumerator Relax() // bring it back home slowly
    {
        if (busy == true)
        {
            yield break;
        }
        busy = true;
        int max = Mathf.Max(2, Mathf.CeilToInt(Random.Range(0, 5) * (2 - hastiness)));
        float prevHaste = hastiness;
        float prevFluid = fluidness;
        fluidness *= 2;
        hastiness /= 2;
        for (int i = 0; i < max; i++)
        {
            targetEnd.position = attachPoint.position + (Vector3)Random.insideUnitCircle.normalized * (size * N) / 5;
            yield return new WaitForSeconds(Random.Range(1f, 2f));
        }
        hastiness = prevHaste;
        fluidness = prevFluid;
        yield return new WaitForSeconds(1f);
        busy = false;
    }

    public IEnumerator Acco(float activity = 0.45f) //grow and spawn enemies if not in dungeon
    {
        bias = 0f;
        hastiness = 0.145f - 0.1f * activity;
        sinOnOneSide = false;
        fluidness = activity * 0.9f;

        if (RefreshManager.i.INSTASPAWN)
        {
            refresh = 0;
        }
        else
        {
            for (float t = 0f; t < warmUpTime; t += Time.fixedDeltaTime)
            {
                refresh = Mathf.Lerp(refresh, 0,  0.25f * t  / warmUpTime * Time.fixedDeltaTime);
                yield return new WaitForFixedUpdate();
            }
        }
        
        //refresh = 0;dw
        if (InDungeon) //Base EEs Spawn Enemies On Each Day
        {
            yield break;
        }
        reduceFluidityOverTime = fluidness * 0.85f;
        increaseHastinessOverTime = hastiness * 2f;
        finishedSpawning = false;
        int valBuf = Mathf.CeilToInt(activity * spawnCoef * 1.5f * GS.Sigma(SpawnManager.daySinceNewEra) + Mathf.Lerp(0, 2f, (activity - 0.45f) / 0.55f) * spawnCoef);
        MarauderSO spawn;
        while (valBuf > 0)
        {
            if (SOs.Length == 0)
            {
                break;
            }
            bool brek = true;
            foreach (MarauderSO so in SOs)
            {
                if (so.price <= valBuf)
                {
                    brek = false;
                    break;
                }
            }
            if (brek == true)
            {
                break;
            }
            while (true)
            {
                spawn = SOs[Random.Range(0, SOs.Length)]; //THIS WAS THE BUG! Prev: SOs.Length - 1!
                if (valBuf < spawn.price)
                {
                    continue;
                }
                if (Random.Range(0, spawn.rarity) == 0)
                {
                    Vector3 dir = 200 * activity * size * Random.insideUnitCircle;
                    if((dir + transform.position).sqrMagnitude < transform.position.sqrMagnitude)
                    {
                        dir *= -1f;
                    }
                    dir += (Vector3)(Vector2)transform.position;
                    SpawnEnemy(spawn.prefab, dir,false);
                    valBuf -= spawn.price;
                    yield return new WaitForSeconds(Mathf.Pow(spawn.price, 0.6f) / activity);
                    break;
                }
            }
        }
        finishedSpawning = true;
        ChangeN(SpawnManager.day + N);
    }

    public IEnumerator DeAcco()
    {
        sinOnOneSide = true;
        fluidness = 0.1f;
        hastiness = 0.13f;
        for (float t = 0f; t < warmUpTime; t += Time.fixedDeltaTime)
        {
            refresh = Mathf.Lerp(refresh, 0.05f, 0.25f * t / warmUpTime * Time.fixedDeltaTime);
            yield return new WaitForFixedUpdate();
        }
        refresh = 0.05f;
    }

    public void Shift()
    {
        hastiness *= 2;
        float r = transform.position.magnitude;
        float theta = Mathf.Atan(transform.position.x / transform.position.y);
        theta += Mathf.Deg2Rad * Random.Range(-50f, 50f);
        StartCoroutine(MoveI(MapManager.i.ProximityData(new Vector3(r * Mathf.Sin(theta), r * Mathf.Cos(theta), 1), 3f).Item1)); //move to somewhere 3 units away from line
    }

    public IEnumerator MoveI(Vector3 pos)
    {
        shiftPos = pos;
        Vector3 mov = (pos - transform.position) * Time.fixedDeltaTime / 29f;
        for (float i = 29f; i > 0f; i -= Time.fixedDeltaTime)
        {
            transform.position += mov;
            yield return new WaitForFixedUpdate();
        }
        hastiness /= 2;
        shiftPos = Vector2.zero;
        yield return null;
    }

    public void ChangeN(int newN)
    {
        lr.positionCount = newN;
        System.Array.Resize(ref positions, newN);
        System.Array.Resize(ref posVels, newN);
        if (newN > N)
        {
            for (int i = N - 1; i < newN; i++)
            {
                positions[i] = positions[i - 1];
            }
            lr.SetPositions(positions);
        }
        N = newN;
    }

    public void Dissapear()
    {
        StopCoroutine(nameof(Acco));
        StopCoroutine(nameof(DeAcco));
        transform.parent = GS.FindParent(GS.Parent.ee);
        StartCoroutine(IDissapear());
    }

    private IEnumerator IDissapear() //Destroy / go to dungeon
    {
        bool wasInDungeon = InDungeon;
        if (wasInDungeon)
        {
            currentCores += 1;
        }
        int ogN = N;
        float ogSize = size;
        ps.transform.localScale = new Vector3(size * 100, size * 100, 1f);
        ChangeN(N + 5);
        fluidness = 2f;
        hastiness = 0.4f;
        size *= 2;
        sinOnOneSide = false;
        yield return new WaitForSeconds(0.75f);
        size = 0f;
        sinOnOneSide = true;
        fluidness = 0.01f;
        hastiness = 1.5f;
        yield return new WaitForSeconds(1.25f);
        hastiness = 0.13f;
        refresh = -1f;
        for (int i = N - 1; i > 1; i--)
        {
            ChangeN(i);
            if (i > 10 && !wasInDungeon)
            {
                Instantiate(ps, transform).SetActive(true);
            }
            yield return new WaitForSeconds(0.5f / N);
        }
        if (!wasInDungeon && name != "MainCore")
        {
            SpawnManager.instance.OnNewDay -= changeNOnDay;
            Destroy(gameObject,0.15f);
            SpawnManager.instance.EEs.Remove(this);
        }
        else //Dungeon EEs travel to base
        {
            if (name != "MainCore")
            {
                SpawnManager.instance.OnNewDay += changeNOnDay;
                transform.position = MapManager.i.ProximityData(transform.position, 3f).Item1;
            }
            else
            {
                lr.material = mats[GS.Era1()]; //this is done before era changes so era1
            }
            refresh = 0.05f;
            fluidness = 0.1f;
            size = ogSize;
            lr.positionCount = 0;
            ChangeN(ogN);
            positions[0] = attachPoint.transform.position;
            Vector3 dir = Random.insideUnitCircle.normalized * size;
            for (int i = 1; i < N; i++)
            {
                positions[i] = positions[i - 1] + dir + size * fluidness * (Vector3)Random.insideUnitCircle;
                dir = GS.Rotated(dir, Random.Range(0, fluidness * 15f), true);
                posVels[i] = Vector2.zero;
            }
            lr.SetPositions(positions);
            InDungeon = false; //signals that EE is ready to be moved about.
        }
    }

    public void SetupUI()
    {
        SpawnManager.instance.EEs.Add(this);
        RawImage ri = UIManager.i.FillNextEEIcon();
        cam.enabled = true;
        ri.texture = textur;
        ri.color = Color.white;
        BossDoor.AddRaw(textur);
    }

    //Growdwth Factors: #1 fluidness, #2 N, #3 Size, #4 Refresh, #5 SinOnOneSide.
    //Growing: fluidness going up, sinOnOneSide is off, refresh rate decreasing
    //Shrinking: Fluidness going down, sinOnOneSide is on, refresh rate increasing

    //Permagrowing: N is increasing
    //Size is always the same..

    public GameObject SpawnEnemy(GameObject enemy, Vector2 pos, bool inRoom = true)
    {
        if (inRoom)
        {
            return Instantiate(enemy, pos, Quaternion.identity, GS.FindParent(GS.Parent.enemies));
        }
        
        var g = Instantiate(enemy, pos, Quaternion.identity, GS.FindParent(GS.Parent.enemies));
        SpawnManager.instance.alives.Add(g);
        MapManager.i.OnTriggerExit2D(g.GetComponentInChildren<Collider2D>());
        if (SoulGenerator.gs.Count > 0)
        {
            foreach (LifeScript l in g.GetComponentsInChildren<LifeScript>())
            {
                var s = l.gameObject.AddComponent<SoulCollectOnDeath>();
                l.onDeaths.Add(s);
            }
        }
        g.transform.up = -g.transform.position;
        return g;
        
    }

    public IEnumerator MakeFX()
    {
        reduceFluidityOverTime = 0f;
        increaseHastinessOverTime = 0f;
        float ogSize = size;
        float ogFluid = fluidness;
        float oghastiness = hastiness;
        int ogN = N;
        ChangeN(N + 5);
        fluidness = 2f;
        hastiness = 0.4f;
        size *= 2;
        sinOnOneSide = false;
        yield return new WaitForSeconds(0.75f);
        Instantiate(eeWaveCompleteFX,transform.position,Quaternion.identity,transform);
        size = 0f;
        sinOnOneSide = true;
        fluidness = 0.01f;
        hastiness = 1.5f;
        EEExplodeEvent.Invoke();
        yield return new WaitForSeconds(1.25f);
        size = ogSize;
        fluidness = ogFluid;
        hastiness = oghastiness;
        ChangeN(ogN);
        yield return new WaitForSeconds(1f);
        StartCoroutine(DeAcco());
    }
}
