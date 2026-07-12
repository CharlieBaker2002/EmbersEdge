using System.Collections;
using UnityEngine;
using Random = UnityEngine.Random;

public class OrbScript : MonoBehaviour
{
    //NOW MANAGED BY ORBMANAGER!
    public static bool[] canAttract = new bool[] { true, true, true, true };
    public static int tot = 0;
    
    public int orbType = 0;
    public enum OrbState {wild, hoverstore, hover, collect, accelerate, decelerate, harvest, deposit, follow }
    public OrbState state = OrbState.wild;
    public float timeLeft = 75f;
    [HideInInspector]
    public float theta;
    public float hovTimer = -1f;
    public float rot = 1f;
    [HideInInspector] public float chaseT;   // seconds this orb has been actively closing on the player

    // --- deposit-flight state (orb gliding from the player into a pylon) ---
    [HideInInspector] public Vector3 depStart;   // local start pos (relative to the target magnet)
    [HideInInspector] public float depT;          // 0..1 flight progress
    [HideInInspector] public float depDur = 0.8f; // flight duration, scaled by distance
    // Two sine harmonics that offset the orb perpendicular to an otherwise-straight beam. Both are
    // zero at t=0 and t=1 (anchored at player and pylon) and each crosses the line, so the sideways
    // wander is balanced — the beam reads straight overall, just with random curves along the way.
    [HideInInspector] public float depWig1;       // primary wiggle amplitude (2-lobe harmonic)
    [HideInInspector] public float depWig2;       // secondary wiggle amplitude (3-lobe harmonic)

    void Start()
    {
        theta = Random.Range(0f, 2 * Mathf.PI);
        if(orbType == 0)
        {
            rot = Random.Range(0, 8) * 45f;
        }
        else if(orbType == 1)
        {
            rot = Random.Range(0, 4) * 90f;
        }
        else if(orbType == 2)
        {
            rot = Random.Range(2, 4) * 90f;
        }
        else if(orbType == 3)
        {
            Random.Range(0f, 360f);
        }
    }

    public void Hover(bool store)
    {
        if (store)
        {
            state = OrbState.hoverstore;
        }
        else
        {
            state = OrbState.hover;
        }
        hovTimer = 0f;
    }

    public void Harvest()
    {
        state = OrbState.harvest;
        hovTimer = 2f;
    }

    public void ReturnToPool()
    {
        if (!gameObject.activeInHierarchy) return;
        state = OrbState.wild;
        timeLeft = 75f;
        chaseT = 0f;
        Start();
        theta = Random.Range(0f, 360f);
        transform.parent = SpawnManager.instance.orbParent;
        SpawnManager.instance.orbPools[orbType].Release(gameObject);
    }

    private void OnEnable()
    {
        OrbManager.allOrbs.Add(this);
    }

    private void OnDisable()
    {
        OrbManager.allOrbs.Remove(this);
    }

    //                            }
    //                            else
    //                            {
    //                                if (col.enabled) col.enabled = false;
    //                            }
    //                        }

}