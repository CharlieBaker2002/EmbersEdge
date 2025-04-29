using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

public class TentacleD0 : Tentacle
{
    private Transform enemy = null;
    [SerializeField] private Room r;
    private bool added = false;

    private void OnEnable()
    {
        busy = false;
        StartCoroutine(Relax());
    }

    private void OnDisable()
    {
        fluidness = 1;
        hastiness = 0.12f;
    }
    
    private void OnDestroy()
    {
        EnemyTracker.enemies[7].Remove(transform);
    }

    private void Update()
    {
        if (!added && DM.i.activeRoom == r)
        {
            EnemyTracker.enemies[7].Add(transform);
            added = true;
        }
        if (busy)
        {
            return;
        }
        if(enemy == null)
        {
            if(Random.Range(0,6) != 0)
            {
                return;
            }
            enemy = GS.FindNearestEnemy(tag, attachPoint.transform.position + enforcedRotationSegs * segLength * attachPoint.transform.up, 8f, false, false);
            return;
        }
        targetEnd.position = enemy.position;
        if(Vector2.Distance(attachPoint.transform.position + enforcedRotationSegs * attachPoint.transform.up * segLength,enemy.position) > 8f)
        {
            enemy = null;
            StartCoroutine(Relax());
        }
    }
}
