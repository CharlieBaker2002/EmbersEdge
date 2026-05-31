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

        if (lr.positionCount < 2) return;
        Vector2 scale = transform.lossyScale;
        if (Mathf.Abs(scale.x) < 0.0001f || Mathf.Abs(scale.y) < 0.0001f) return;

        vs.Clear();
        Vector2 dir = Vector2.zero;

        for (int i = 0; i < lr.positionCount - 1; i++)
        {
            Vector2 seg = lr.GetPosition(i + 1) - lr.GetPosition(i);
            if (seg.sqrMagnitude > 0.000001f)
            {
                dir = Mathf.Min(1, lr.widthCurve.Evaluate((float)i / (float)(lr.positionCount - 1))) * 0.5f * seg.normalized;
                dir = new Vector2(dir.y, -dir.x);
            }
            vs.Add((Vector2)lr.GetPosition(i) + dir);
        }
        vs.Add((Vector2)lr.GetPosition(lr.positionCount-1) + dir);
        for (int i = lr.positionCount-1; i > 1; i--)
        {
            Vector2 seg = lr.GetPosition(i - 1) - lr.GetPosition(i);
            if (seg.sqrMagnitude > 0.000001f)
            {
                dir = Mathf.Min(1, lr.widthCurve.Evaluate((float)i / (float)(lr.positionCount - 1))) * 0.5f * seg.normalized;
                dir = new Vector2(dir.y, -dir.x);
            }
            vs.Add((Vector2)lr.GetPosition(i) + dir);
        }
        vs.Add((Vector2)lr.GetPosition(0) + dir);

        for (int i = 0; i < vs.Count; i++)
            vs[i] = (vs[i] - (Vector2)transform.position).Rotated(-transform.rotation.eulerAngles.z) / scale;

        // sanity check — don't give the tessellator NaN or inf
        for (int i = 0; i < vs.Count; i++)
            if (float.IsNaN(vs[i].x) || float.IsNaN(vs[i].y) || float.IsInfinity(vs[i].x) || float.IsInfinity(vs[i].y)) return;

        // Collapse runs of coincident vertices (including the wrap-around pair) — libtess2's
        // convex-merge step crashes on zero-length edges. See ~/Library/Logs/DiagnosticReports
        // Unity-2026-05-28-034212.ips: SIGSEGV at 0x24 inside tessMeshMergeConvexFaces.
        const float coincidentEps = 1e-6f;
        for (int i = vs.Count - 1; i > 0; i--)
            if ((vs[i] - vs[i - 1]).sqrMagnitude < coincidentEps) vs.RemoveAt(i);
        while (vs.Count > 1 && (vs[vs.Count - 1] - vs[0]).sqrMagnitude < coincidentEps)
            vs.RemoveAt(vs.Count - 1);

        if (vs.Count < 4) return;

        float a = 0f;
        for (int i = 0, j = vs.Count - 1; i < vs.Count; j = i++)
            a += (vs[j].x * vs[i].y) - (vs[i].x * vs[j].y);
        if (Mathf.Abs(0.5f * a) < 1e-6f) return;

        col.SetPath(0, vs);
    }
}
