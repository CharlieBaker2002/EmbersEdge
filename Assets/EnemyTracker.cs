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
    public List<Sprite> sprs; //every sprite img for each enemy in the game
    public static float dist = 3f;
    private int iteration = 0;
    private int delta;
    
    private void Awake()
    {
        enemies = new List<List<Transform>>();
        delta = Mathf.FloorToInt(sprs.Count / 6f);
        dirs = new ObjectPool<Director>(() => Instantiate(dir),
            d =>
            {
                d.gameObject.SetActive(true);
                d.enabled = false;
                d.transform.localScale = Vector2.zero;
                d.gameObject.LeanScale(Vector2.one, 0.5f).setEaseInOutBack().setOnComplete(() => d.enabled = true);
            },
            d =>
            {
                d.gameObject.SetActive(false);
            },
            d => Destroy(d.gameObject),
            false, 20, 50);
    }
    
    public void Update() //batched to be in 6 intervals
    {
        if (iteration == 5)
        {
            for(int i = iteration * delta; i < enemies.Count; i++)
            {
               Batch(enemies[i], sprs[i], i);
            }
        }
        else
        {
            for(int i = iteration * delta; i < iteration * delta + delta; i++)
            {
                Batch(enemies[i],sprs[i], i);
            }
        }
        iteration++;
        if (iteration > 5)
        {
            iteration = 0;
        }
    }
    
    void Batch(List<Transform> en, Sprite spr, int i)
    {
        Vector2[] dirs = new Vector2[en.Count];
        for(int z = 0; z < en.Count; z++)
        {
            if ((en[z].transform.position - CharacterScript.CS.transform.position).sqrMagnitude < 20f)
            {
                dirs[z] = en[z].position;
            }
        }
    }
}