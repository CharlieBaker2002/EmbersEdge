using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

public class EnemyTracker : MonoBehaviour
{
    private ObjectPool<Director> dirs;
    [SerializeField] Director dir;
    public static List<List<Transform>> enemies = new List<List<Transform>>();
    // Directors that are in‑use this frame (returned to the pool at the start of the next)
    private readonly List<Director> activeDirs = new();
    public List<Sprite> sprs1; //every sprite img for each enemy in the game
    public List<Sprite> sprs2;
    public List<Sprite> sprs3;
    List<List<Sprite>> sprs;
    public static float dist = 3f;
    private int iteration = 0;
    // private int dirCursor = 0;
    private int delta;
    [SerializeField] private Camera cam;
    List<Director>[] dirList = new List<Director>[6];
    
    private void Awake()
    {
        for(int i = 0; i < 6; i++)
        {
            dirList[i] = new List<Director>();
        }
        // Fallback to the overlay camera if none has been set in the Inspector
        if (cam == null)
            cam = CameraScript.i.cam;
        sprs = new List<List<Sprite>> { sprs1, sprs2, sprs3 };
        enemies = new List<List<Transform>>();
        for (int i = 0; i < 15; i++)
        {
            enemies.Add(new());
        }
        delta = Mathf.FloorToInt(sprs[GS.era].Count / 6f);
        dirs = new ObjectPool<Director>(() => Instantiate(dir, UIManager.i.directors),
            d =>
            {
                d.gameObject.SetActive(true);
                d.enabled = true;          // no delay – update runs immediately
                d.transform.localScale = Vector3.one;
            },
            d =>
            {
                d.gameObject.SetActive(false);
            },
            d => Destroy(d.gameObject),
            false, 20, 200);
        GS.OnNewEra += _ =>
        {
            Director.maxdistance *= 2f;
        };
    }
    
    public void Update() //batched to be in 6 intervals
    {
        dirList[iteration].ForEach(x=>
        {
            x.inUse = false;
            x.ts.Clear();
        });
        dirList[iteration].Clear();
        if (iteration == 5)
        {
            for(int i = iteration * delta; i < sprs[GS.era].Count; i++)
            {
               Batch(enemies[i], sprs[GS.era][i], i,iteration);
            }
        }
        else
        {
            for(int i = iteration * delta; i < iteration * delta + delta; i++)
            {
                Batch(enemies[i],sprs[GS.era][i], i,iteration);
            }
        }
        iteration++;
        if (iteration > 5)
        {
            iteration = 0;
        }
    }

    // Groups off‑screen enemies that share a similar angle and spawns one Director per group
    void Batch(List<Transform> en, Sprite spr, int i, int iteration)
    {
        if (en == null || en.Count == 0) return;
        List<Transform> offScreen = new List<Transform>();

        // Collect only enemies that are outside the viewport
        const float MARGIN = -0.05f;
        foreach (var t in en)
        {
            if (t == null) continue;
            Vector3 vp = cam.WorldToViewportPoint(t.position);
            if (vp.x < MARGIN || vp.x > 1f - MARGIN || vp.y < MARGIN || vp.y > 1f - MARGIN)
            {
                offScreen.Add(t);
            }
        }
        if (offScreen.Count == 0) return;

        Vector3 playerPos = CharacterScript.CS.transform.position;
        const float ANGLE_THRESHOLD = 15f; // degrees

        // Simple angular clustering
        List<List<Transform>> clusters = new List<List<Transform>>();
        foreach (var t in offScreen)
        {
            float ang = Mathf.Atan2(t.position.y - playerPos.y, t.position.x - playerPos.x) * Mathf.Rad2Deg;
            bool placed = false;

            foreach (var cluster in clusters)
            {
                float cAng = Mathf.Atan2(cluster[0].position.y - playerPos.y, cluster[0].position.x - playerPos.x) * Mathf.Rad2Deg;
                if (Mathf.Abs(Mathf.DeltaAngle(cAng, ang)) < ANGLE_THRESHOLD)
                {
                    cluster.Add(t);
                    placed = true;
                    break;
                }
            }

            if (!placed)
            {
                clusters.Add(new List<Transform> { t });
            }
        }

        // Spawn a Director for each cluster
        foreach (var cluster in clusters)
        {
            // Find an unused Director first
            Director d = null;
            foreach (var cand in activeDirs)
            {
                if (!cand.inUse)
                {
                    d = cand;
                    break;
                }
            }

            // If every Director is busy, pull a fresh one from the pool
            if (d == null)
            {
                d = dirs.Get();
                activeDirs.Add(d);
            }

            d.inUse = true;
            d.gameObject.SetActive(true);
            d.Set(cluster, spr);
            dirList[iteration].Add(d);
        }
    }


}