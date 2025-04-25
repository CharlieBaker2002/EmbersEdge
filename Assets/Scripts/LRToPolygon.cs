using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LRToPolygon : MonoBehaviour
{
    public PolygonCollider2D col;
    public LineRenderer lr;
    public float refreshT = 0.01666666667f;
    private float t = 0f;
    private List<Vector2> vs = new List<Vector2>();

    public void Update()
    {
        t -= Time.fixedDeltaTime;
        if (t >= 0f)
        {
            return;
        }
        t += refreshT;

        vs.Clear();
        Vector2 dir = Vector2.zero;

        for (int i = 0; i < lr.positionCount - 1; i++)
        {
            dir = lr.GetPosition(i + 1) - lr.GetPosition(i);
            dir = Mathf.Min(1, lr.widthCurve.Evaluate((float)i / (float)(lr.positionCount - 1))) * 0.5f * dir.normalized;
            dir = new Vector2(dir.y, -dir.x);
            vs.Add((Vector2)lr.GetPosition(i) + dir);
        }
        vs.Add((Vector2)lr.GetPosition(lr.positionCount-1) + dir);
        for (int i = lr.positionCount-1; i > 1; i--)
        {
            dir = lr.GetPosition(i - 1) - lr.GetPosition(i);
            dir = Mathf.Min(1, lr.widthCurve.Evaluate((float)i / (float)(lr.positionCount - 1))) * 0.5f * dir.normalized;
            dir = new Vector2(dir.y, -dir.x); //no need to invert cos going in opposite direction methinks
            vs.Add((Vector2)lr.GetPosition(i) + dir);
        }
        vs.Add((Vector2)lr.GetPosition(0) + dir);
      
        for (int i = 0; i < vs.Count; i++)
        {
            vs[i] = vs[i] - (Vector2)transform.position;
        }
        for (int i = 0; i < vs.Count; i++)
        {
            vs[i] = vs[i].Rotated(-transform.rotation.eulerAngles.z);
        }
        for (int i = 0; i < vs.Count; i++)
        {
            vs[i] = vs[i] / transform.lossyScale;
        }
        col.SetPath(0, vs);
    }
}
