using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ResourceShrine : Shrine
{
    public Vector2Int range;
    private bool acco = true;

    public override void Trigger(Transform t)
    {
        if (acco)
        {
            acco = false;
            base.Trigger(t);
        }
    }
}
