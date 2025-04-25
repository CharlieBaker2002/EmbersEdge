using System.Collections.Generic;
using UnityEngine;
using System;

public class OrbManifester : Building, IOnDeath
{
    public float timePeriod = 10f;
    public int n = 1;
    public int orbType;
    int[] buffer = new int[4] { 0, 0, 0, 0 };
    private Animator anim;
    public List<Ore> ores = new();
    private Action act;

    private void Awake()
    {
        anim = GetComponent<Animator>();
    }

    public override void Start()
    {
        base.Start();
        buffer[orbType] = n;
        ResetOre();
        act = () => { Spawn(); ResetOre(); };
        SpawnManager.instance.OnNewDay += act;
    }

    private void ResetOre()
    {
        ores = new();
        GameObject g;
        Vector3Int start = TilemapResource.m[orbType].WorldToCell(transform.position);
        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                g = TilemapResource.m[orbType].GetInstantiatedObject(start + new Vector3Int(x, y));
                if (g != null)
                {
                    ores.Add(g.GetComponent<Ore>());
                }
            }
        }
    }


    public void Spawn() //call from animation
    {
        anim.ResetTrigger("Trigger");
        SpawnManager.instance.CallSpawnOrbs(transform.position, buffer);
        buffer = new int[4];
        buffer[orbType] += n;
        foreach(Ore o in ores)
        {
            if (o == null) { continue; }
            buffer[orbType] += o.Chip();
        }
        GS.CallSpawnOrbs(transform.position, buffer);
    }

    public override void OnDeath()
    {
        base.OnDeath();
        SpawnManager.instance.OnNewDay -= act;
    }

}
