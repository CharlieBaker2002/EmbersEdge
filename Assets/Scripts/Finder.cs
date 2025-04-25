using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public class Finder : MonoBehaviour
{
    public System.Action<Transform> OnFound = _ => { };
    public float refresh = 6f;
    
    [Header("Calls OnFound even if not found a unit")]
    public bool ALWAYSCALL = true;
    [Header("Only searches when tracked dies")]
    public bool FAITHFUL;
    [Header("Calls OnLost when distance is exceeded")]
    public bool CHECKDISTANCE;
    [Header("Searches Immediately On OnLost")]
    public bool QUICKSWAP;
    [Header("CLOSEST ONLY")]
    public bool PROXIMAL;

    public bool turret = false;
    public static List<Finder> turrets = new List<Finder>();
    public static bool turretsOn = false;
    
    public int maxSearch = 10;
    private Collider2D[] cols;
    [SerializeField] bool allowBuildings = false;
    [SerializeField] bool preferBuildings = false;
    
    private float timer = 0.25f;
    public Transform T;
    public float radius = 5f;
    public Action OnLost = delegate { };
    
    private int distCheck;
    private float sqrDistance;
    bool had = false;
    
    private void Start()
    {
        if(!PROXIMAL) cols = new Collider2D[maxSearch];
        sqrDistance = radius * radius;
        if (turret)
        {
            turrets.Add(this);
            if (!turretsOn)
            {
                enabled = false;
            }
        }
    }

    private void OnDestroy()
    {
        if (turret)
        {
            turrets.Remove(this);
        }
    }

    public static void TurnOnTurrets()
    {
        foreach (Finder f in turrets)
        {
            f.enabled = true;
        }
        turretsOn = true;
    }

    public void UpdateRadius(float t)
    {
        radius = t;
        sqrDistance = radius * radius;
    }
    public static void TurnOffTurrets()
    {
        if(RefreshManager.i.IGNOREEFFICIENCY) return;
        foreach (Finder f in turrets)
        {
            f.enabled = false;
        }
        turretsOn = false;
    }

    private void Update()
    {
        timer -= Time.deltaTime;
        if (CHECKDISTANCE && T!=null) //CHECK DISTANCE
        {
            distCheck++;
            if (distCheck >= 12)
            {
                distCheck = 0;
                if ((T.position - transform.position).sqrMagnitude > sqrDistance)
                {
                    T = null;
                    if (QUICKSWAP)
                    {
                        timer = 0f;
                    }
                    OnLost.Invoke();
                    had = false;
                    return;
                }
            }
        }
        if (had && T == null) //CHECK DEADED
        {
            if (QUICKSWAP)
            {
                timer = 0f;
            }
            T = null;
            OnLost.Invoke();
            had = false;
            return;
        }
        
        if(timer > 0f) //TIMING BLOCK (QUICK SWAP PASSES THIS ON DIE OR DISTANCE)
        {
            return;
        }
        
        if(FAITHFUL && T != null) //FAITHFUL BLOCK
        {
            timer = 0f;
            return;
        }
        timer += refresh;
        
        T = PROXIMAL ? GS.FindNearestEnemy(tag, transform.position, radius, preferBuildings, allowBuildings) : GS.FindEnemy(transform,radius,GS.BoolsToSearch(true,allowBuildings,false),cols, T);
        had = T != null;
        if(ALWAYSCALL || T != null) //CALL ON FOUND
        {
            OnFound.Invoke(T);
        }
    }

    public Transform FindFresh()
    {
         T = PROXIMAL ? GS.FindNearestEnemy(tag, transform.position, radius, preferBuildings, allowBuildings) : GS.FindEnemy(transform,radius,GS.BoolsToSearch(true,allowBuildings,false),cols);
        had = T == null;
        return T;
    }
}
