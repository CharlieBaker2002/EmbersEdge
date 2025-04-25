using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ResourceShrine : Shrine
{
    public Vector2Int range;
    public int orbT;
    private bool acco = true;

    public override void Trigger(Transform t)
    {
        if (acco)
        {
            acco = false;
            base.Trigger(t);
            int[] buffer = new int[] { 0, 0, 0, 0 };
            buffer[orbT] = (1+GS.era) * Random.Range(range.x, range.y);
            GS.CallSpawnOrbs(transform.position, buffer);
        }
    }
}
