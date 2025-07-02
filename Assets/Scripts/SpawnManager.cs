using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Serialization;

public class SpawnManager : MonoBehaviour
{
    public static SpawnManager instance;
    [Space(10)]
    public Transform orbParent;
    public List<MarauderSO> marauders;
    public int incrementer;
    public GameObject[] orbs;
    public ObjectPool<GameObject>[] orbPools = new ObjectPool<GameObject>[4];
    public GameObject[] chests;
    public Transform Allies;
    public Transform AllyBuildings;
    public Transform AllyProjectiles;
    public Transform Enemies;
    public Transform EnemyProjectiles;
    public Transform FX;
    public Transform EE;
    public Transform Loot;
    public Transform Misc;
    public Transform Followers;
    public TextMeshProUGUI timeText;
    public System.Action OnNewDay;
    private float sinceLastBigAttack = 0f;
    private float activityLevel;
    public System.Action onWaveComplete;
    public bool waveCompleted = false; 
    bool helpedWithWave = false; //determines whether next day should be called even from dungeon
    float maxTimer;
    public static bool eeactive = false;

    public Material[] eraMats;

    public enum DayState { Day, PreAttack, Attack }
    public DayState dayState = DayState.Day;

    private List<TS> timeScales = new();

    public float timer = 120f;

    public static int day = 0;
    public static int daySinceNewEra = 0;
    public Slider activitySlider;
    public Slider eraCompletionSlider;

    public GameObject castFX;

    public GameObject[] vulnerables;

    public List<EmbersEdge> EEs = new List<EmbersEdge>();
    public List<GameObject> alives = new List<GameObject>();
    public Transform EESpawnPoint;
    public MarauderSO[] E2SOs;
    public MarauderSO[] E3SOs;

    public Color e1;
    public Color e2;
    public Color e3;

    public EEIcon EEIcon;


    private void Awake()
    {
        onWaveComplete = delegate
        {
            //UIManager.i.SetTelePhone(UIManager.TeleMode.Base, 1f);
            if (!GS.CS().InDungeon()) { PortalScript.i.YesPortal(); } 
        };
        maxTimer = timer;
        instance = this;
        EmbersEdge.warmUpTime = 20f;
        day = 0;
        daySinceNewEra = 0;
        OnNewDay += delegate {day++; daySinceNewEra++; Debug.Log("Day: " + day); UIManager.i.UpdateDayText(day);};
        Random.InitState((int)System.DateTime.Now.Ticks);
        orbPools[0] = new ObjectPool<GameObject>(() =>
        {
            OrbScript.tot++;
            return Instantiate(orbs[0], transform.position, transform.rotation, orbParent);
        }, orb =>
        {
            orb.SetActive(true);
            OrbScript.tot++;
        }, orb =>
        {
            orb.SetActive(false);
            transform.parent = orbParent;
            OrbScript.tot--;
        }, orb =>
        {
            Debug.Log("attempted destruction of orb");
            Destroy(orb);
            OrbScript.tot--;
        });

        orbPools[1] = new ObjectPool<GameObject>(() =>
        {
            OrbScript.tot++;
            return Instantiate(orbs[1], transform.position, transform.rotation, orbParent);
        }, orb =>
        {
            orb.SetActive(true);
            OrbScript.tot++;
        }, orb =>
        {
            orb.SetActive(false);
            transform.parent = orbParent;
            OrbScript.tot--;
        }, orb =>
        {
            Debug.Log("attempted destruction of orb");
            Destroy(orb);
            OrbScript.tot--;
        });

        orbPools[2] = new ObjectPool<GameObject>(() =>
        {
            OrbScript.tot++;
            return Instantiate(orbs[2], transform.position, transform.rotation, orbParent);
        }, orb =>
        {
            orb.SetActive(true);
            OrbScript.tot++;
        }, orb =>
        {
            orb.SetActive(false);
            transform.parent = orbParent;
            OrbScript.tot--;
        }, orb =>
        {
            Debug.Log("attempted destruction of orb");
            Destroy(orb);
            OrbScript.tot--;
        });

        orbPools[3] = new ObjectPool<GameObject>(() =>
        {
            OrbScript.tot++;
            return Instantiate(orbs[3], transform.position, transform.rotation, orbParent);
        }, orb =>
        {
            orb.SetActive(true);
            OrbScript.tot++;
        }, orb =>
        {
            orb.SetActive(false);
            transform.parent = orbParent;
            OrbScript.tot--;
        }, orb =>
        {
            Debug.Log("attempted destruction of orb");
            Destroy(orb);
            OrbScript.tot--;
        });
    }
    
    void SetPreAttack()
    {
        if (!RefreshManager.i.CASUALNOTREALTIME) //IF REGULAR MODE, WE DON'T SET PRE-ATTACK WHEN DYING TO EE... BECAUSE IT JUST HAPPENS ANYWAY FOR SOME REASON?
        {
            if(!DM.i.activeRoom.defeated && DM.i.activeRoom.EE!=null && PortalScript.i.inDungeon) return; 
        }
        eeactive = true;
        activityLevel = Mathf.Lerp(0.45f, 1f, RandomManager.Rand(1, new Vector2(sinceLastBigAttack, 1), sinceLastBigAttack));
        MapManager.SetSpin(activityLevel);
        if(activityLevel < 0.6f)
        {
            timeText.text = "Ember's Edge Weakly Active";
            timeText.color = new Color(0.675f, 0.5f, 0.3f);
        }
        else if (activityLevel < 0.75f)
        {
            timeText.text = "Ember's Edge Active";
            timeText.color = new Color(0.775f, 0.4f, 0.2f);
        }
        else if (activityLevel < 0.9f)
        {
            timeText.text = "Ember's Edge Very Active";
            timeText.color = new Color(0.875f, 0.15f, 0.1f);
        }   
        else
        {
            timeText.text = "Ember's Edge Extremely Active";
            timeText.color = Color.red;
        }
        if (activityLevel > 0.75f)
        {
            sinceLastBigAttack = 0f;
        }
        else
        {
            sinceLastBigAttack += 0.01f * GS.Era1();
        }

        foreach (EmberCannon ec in EmberCannon.ecs)
        {
            ec.Activate();
        }
        foreach (EmbersEdge EE in EEs)
        {
            EE.StartCoroutine(EE.Acco(activityLevel + EE.bias));
        }
        dayState = DayState.PreAttack;
        timer = EmbersEdge.warmUpTime;
        UpdateActivitySlider(activityLevel);
    }
    
    

    public void AccelerateWave(bool dead)
    {
        if (dead)
        {
            EmbersEdge.warmUpTime = 5f;
            this.QA(() => EmbersEdge.warmUpTime = 20f, 15);
        }
        if(dayState == DayState.Day)
        {
            PortalScript.i.Cancel();
            PortalScript.i.NoPortal();
            if (RefreshManager.i.CASUALNOTREALTIME)
            {
                SetPreAttack();
                return;
            }
            timer = Mathf.Min(timer,dead ? 0.01f : Mathf.Lerp(timer,10f,0.6f));
        }
    }

    private void SetToBase()
    {
        if (GS.CS().InDungeon())
        {
            PortalScript.i.swapTeleIcon = true;
        }
        else
        {
            PortalScript.i.swapTeleIcon = false;
        }
        UpdateActivitySlider(0f);
    }

    private void Start()
    {
        UIManager.i.SetTelePhone(UIManager.TeleMode.Core, 1f);
        PortalScript.i.YesPortal();
        dayState = DayState.Attack;
        timer = -10f;
        Time.timeScale = RefreshManager.i.STANDARDTIME;
        GS.OnNewEra += (ctx) => { UIManager.i.UpdateDayText(day); timer = 20f; maxTimer = 10f; dayState = DayState.Day; waveCompleted = false; };
    }

    public void Update()
    {
        if (timeText.isActiveAndEnabled) // for sake of tutorial
        {
            float save = timer;
            timer -= Time.deltaTime;
            switch (dayState)
            {
                case DayState.Day:
                    if (RefreshManager.i.CASUALNOTREALTIME)
                    {
                        timer += Time.deltaTime;  //PSYCH!
                    }
                    activitySlider.value = timer / maxTimer;
                    if (timer <= 0f)
                    {
                        SetPreAttack();
                    }
                    else if(timer <= 30f && save > 30f && Random.Range(0, 3) == 0)
                    {
                        SetShiftEEs();
                    }
                    break;
                case DayState.PreAttack:
                    if (RefreshManager.i.INSTASPAWN)
                    {
                        timer = 0f;
                    }
                    if(timer <= 0f)
                    {
                        dayState = DayState.Attack;
                        timeText.text = "Ember's Edge Errupted";
                        CameraScript.QuickLeanDistort(CameraScript.dungDistort * (0.5f + activityLevel), 1f - 0.125f * activityLevel);
                        Finder.TurnOnTurrets();
                        timeText.color = Color.Lerp(timeText.color, Color.blue, 0.5f);
                        helpedWithWave = !PortalScript.i.inDungeon;
                    }
                    break;
                case DayState.Attack:
                    if(timer > -5f)
                    {
                        break;
                    }
                    for(int i = 0; i < alives.Count; i++)
                    {
                        if (alives[i] == null)
                        {
                            alives.RemoveAt(i);
                            i--;
                        }
                    }
                    if(timer <= -40f)
                    {
                        timer = save;
                    }
                    if(alives.Count == 0 && EmbersEdge.CheckFinished())
                    {
                        timer = save;
                        NextDayFR();
                    }
                    break;
            }
        }
        for(int i = 0; i < timeScales.Count; i++)
        {
            if(Time.unscaledTime > timeScales[i].expire)
            {
                CancelTS(timeScales[i].ID);
            }
        }
    }

    public void NextDayFR()
    {
        if (!waveCompleted)
        {
            waveCompleted = true;
            CameraScript.QuickLeanDistort(0f,1f);
            SetToBase();
            StartCoroutine(InvokeWakeComplete());
            if (!PortalScript.i.inDungeon || helpedWithWave)
            {
                SetNextDay();
                PortalScript.i.goingHomeNow = false;
            }
            timeText.text = "Ember's Edge Inactive";
            timeText.color = new Color(0.849f, 0.849f, 0.849f);
            foreach (EmbersEdge EE in EEs)
            {
                EE.StartCoroutine(EE.MakeFX());
            }
            MapManager.DeSpin();
        }
    }


    void SetShiftEEs()
    {
        timeText.text = "Ember's Edge Shifting";
        timeText.color = new Color(0.1f, 0.1f, 1);
        sinceLastBigAttack += 0.01f * GS.Era1();
        foreach(EmbersEdge e in EEs)
        {
            e.Shift();
        }
    }

    private IEnumerator InvokeWakeComplete()
    {
        yield return new WaitForSeconds(2f);
        onWaveComplete.Invoke();
    }

    public void SetNextDay()
    {
        this.QA(() => eeactive = false, 2f);
        StartCoroutine(SetNextDayI());
        IEnumerator SetNextDayI()
        {
            int d = day;
            yield return new WaitForSeconds(1.25f);
            if(d != day)
            {
                yield break;
            }
            waveCompleted = false;
            dayState = DayState.Day;
            timer = 100f + 1.5f * timer + Random.Range(60f, 90f) + 90f * activityLevel; // how fast you beat the prev wave, random, activity
            maxTimer = timer;
            helpedWithWave = false;
            Finder.TurnOffTurrets();
            OnNewDay.Invoke();
            if(RefreshManager.i.DAILYORBBOUNTY)
            {
                CallSpawnOrbs(Vector2.zero,ResourceManager.instance.initResources);
            }
            UpdateEraSlider();
            if (RefreshManager.i.SPAWNTESTMODE)
            {
                timer = 11f;
                if (Random.Range(0, 3) == 0)
                {
                    foreach (EmbersEdge e in EEs)
                    {
                        e.Shift();
                    }
                }
            }
        }
    }
    
    

    public void CallSpawnOrbs(Vector2 pos, int[] orbs, Transform p = null)
    {
        for (int i = 0; i < orbs.Length; i++)
        {
            if (orbs[i] > 0)
            {
                StartCoroutine(SpawnOrbs(pos, i.ToString(), orbs[i], p, false));
            }
        }
    }

    public void CallSpawnOrbs(Vector2 pos, float[] orbs, Transform p = null, bool fillHarvest = false)
    {
        for (int i = 0; i < orbs.Length; i++)
        {
            if (orbs[i] > 0)
            {
                int given = Mathf.FloorToInt(orbs[i]) + Mathf.FloorToInt(ResourceManager.debt[i]);
                float left = orbs[i] + ResourceManager.debt[i] - given;
                ResourceManager.debt[i] = left;
                StartCoroutine(SpawnOrbs(pos, i.ToString(), given, p, fillHarvest));
            }
        }
    }

    public IEnumerator SpawnOrbs(Vector2 pos, string orbType, int orbNum, Transform p, bool fillHarvest)
    {
        int index = -1;
        switch (orbType)
        {
            case "general":
                index = 0;
                break;
            case "druid":
                index = 1;
                break;
            case "engineer":
                index = 2;
                break;
            case "cult":
                index = 3;
                break;
            case "0":
                index = 0;
                break;
            case "1":
                index = 1;
                break;
            case "2":
                index = 2;
                break;
            case "3":
                index = 3;
                break;
        }
        for (int i = 0; i < orbNum; i++)
        {
            if (fillHarvest)
            {
                p = SoulHarvester.GetSpaceAll(index);
                if (p != null)
                {
                    var orb = orbPools[index].Get();
                    orb.transform.position = pos;
                    orb.transform.parent = p;
                    orb.GetComponent<OrbScript>().Harvest();
                    yield return null;
                }
                else
                {
                    break;
                }
            }
            else
            {
                var orb = orbPools[index].Get();
                orb.transform.position = pos;
                orb.transform.parent = p == null ? orbParent : p;
                yield return null;
            }
        }
    }

  
 
    public Transform FindParent(GS.Parent p)
    {
        return p switch
        {
            GS.Parent.allies => Allies,
            GS.Parent.enemies => Enemies,
            GS.Parent.enemyprojectiles => EnemyProjectiles,
            GS.Parent.allyprojectiles => AllyProjectiles,
            GS.Parent.fx => FX,
            GS.Parent.ee => EE,
            GS.Parent.buildings => AllyBuildings,
            GS.Parent.loot => Loot,
            GS.Parent.misc => Misc,
            GS.Parent.followers => Followers,
            _ => null
        };
    }

    public int NewTS(float ts, float duration)
    {
        bool cont = false;
        int ID = 0;
        while (!cont)
        {
            cont = true;
            ID = Random.Range(0, 100);
            foreach (TS t in timeScales)
            {
                if(t.ID == ID)
                {
                    cont = false;
                }
            }
        }
        timeScales.Add(new TS(ts, duration, ID));
        SetTimeScale();
        return ID;
    }

    private void SetTimeScale()
    {
        float min = 10;
        foreach (TS t in timeScales)
        {
            min = (t.ts < min) ? t.ts : min;
        }
        if (min == 10)
        {
            min = RefreshManager.i.STANDARDTIME;
        }
        Time.timeScale = min;
    }

    public void CancelTS(int ID)
    {
        for(int i = 0; i < timeScales.Count; i++)
        {
            if(timeScales[i].ID == ID)
            {
                timeScales.RemoveAt(i);
                continue;
            }
        }
        SetTimeScale();
    }

    public GameObject NewP(GameObject proj, Transform t, string ta, float innaccuracy = 0f, float strength = 0f)
    {
        innaccuracy /= 2;
        Vector2 perp = 0.5f * Random.Range(-innaccuracy, innaccuracy) * Vector2.Perpendicular(t.up);
        Vector2 dire = (Vector2)t.up + perp;
        var p = Instantiate(proj, t.position, Quaternion.identity, GS.ProjectileParent(ta));
        p.GetComponent<ProjectileScript>().SetValues(dire, ta,strength,t);
        return p;
    }
    
    /// <summary>
    /// I'm efficient
    /// </summary>
    public static ProjectileScript NewP(ProjectileScript ps, Transform t, string ta, float innaccuracy = 0f, float strength = 0f)
    {
        innaccuracy /= 2;
        var up = t.up;
        Vector2 perp = 0.5f * Random.Range(-innaccuracy, innaccuracy) * Vector2.Perpendicular(up);
        Vector2 dire = (Vector2)up + perp;
        var p = Instantiate(ps, t.position, Quaternion.identity, GS.ProjectileParent(ta));
        p.SetValues(dire, ta,strength,t);
        return p;
    }
    
    public GameObject NewP(GameObject proj, Transform t, string ta, Vector2 dir, float innaccuracy = 0f, float strength = 0f)
    {
        if(dir == Vector2.zero && innaccuracy!=0)
        {
            var p0 = Instantiate(proj, t.position, Quaternion.identity, GS.ProjectileParent(ta));
            p0.GetComponent<ProjectileScript>().SetValues(Random.insideUnitCircle.normalized, ta, strength, t);
            return p0;
        }
        innaccuracy /= 2;
        Vector2 perp = 0.5f* Random.Range(-innaccuracy, innaccuracy) * Vector2.Perpendicular(dir).normalized;
        Vector2 dire = dir.normalized + perp;
        var p = Instantiate(proj, t.position, Quaternion.identity, GS.ProjectileParent(ta));
        p.GetComponent<ProjectileScript>().SetValues(dire, ta, strength,t);
        return p;
    }

    public void MakeVulnerable(int vulnerableType, Transform t, Vector2 pos)
    {
         Instantiate(vulnerables[vulnerableType], pos, Quaternion.identity, t);
    }

    private void UpdateActivitySlider(float val)
    {
        StartCoroutine(UpdateActivitySliderI(1 - ((val - 0.45f) / 0.65f)));

        IEnumerator UpdateActivitySliderI(float val)
        {
            for(float i = 0f; i < 2f; i += Time.deltaTime)
            {
                yield return null;
                activitySlider.value = Mathf.Lerp(activitySlider.value, val, Time.deltaTime * 4f * i);
            }
        }
    }

    private void UpdateEraSlider()
    {
        float val = 1f - GS.EraCompletion();
        val = Mathf.Lerp(val, 1f, 0.5f * val);
        StartCoroutine(UpdateEraSliderI(val));

        IEnumerator UpdateEraSliderI(float val)
        {
            for (float i = 0f; i < 4f; i += Time.deltaTime)
            {
                yield return null;
                eraCompletionSlider.value = Mathf.Lerp(eraCompletionSlider.value, val, Time.deltaTime * 1f * i);
            }
        }
    }

    public struct TS
    {
        public TS(float tsP, float durationP, int IDP)
        {
            ts = tsP * RefreshManager.i.STANDARDTIME;
            expire = Time.unscaledTime + durationP;
            ID = IDP;
        }

        public float ts { get; }
        public float expire { get; }
        public int ID { get; }
    }
}