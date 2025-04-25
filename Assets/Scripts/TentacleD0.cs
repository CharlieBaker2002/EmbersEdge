using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TentacleD0 : Tentacle
{
    private Transform enemy = null;

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

    private void Update()
    {
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
