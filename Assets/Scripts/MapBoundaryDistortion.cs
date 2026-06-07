using System.Collections;
using UnityEngine;

/// <summary>
/// A constant screen-distortion band shaped like the map's outline (the boundary spline).
/// Builds a thin ribbon mesh straddling MapManager's polygon and renders it with the
/// Shockwave2D_Boundary shader. Rebuilds as the map is reshaped/repositioned (placing an EE,
/// extractor expansion) and follows the map's scale (Shrink) by parenting to the collider.
///
/// Drop this component on an empty GameObject — it wires its own MeshFilter/MeshRenderer/material.
/// Needs the renderer's "Camera Sorting Layer Texture" enabled (same requirement as Shockwave2D).
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class MapBoundaryDistortion : MonoBehaviour
{
    [Header("Band")]
    [Tooltip("Width of the distortion band straddling the map edge (world units).")]
    public float width = 1.5f;
    [Tooltip("Peak screen push, as a fraction of screen height.")]
    public float strength = 0.025f;

    [Header("Life (the shape is static — these keep it moving)")]
    [Tooltip("How many shimmer waves wrap around the rim.")]
    public float shimmerWaves = 24f;
    [Tooltip("How fast the shimmer travels / breathes.")]
    public float shimmerSpeed = 2f;
    [Tooltip("Chromatic split; its R/G/B offsets are phase-shifted 2π/3 apart for a living colour edge.")]
    public float chroma = 0.45f;

    [Header("Wiring")]
    [Tooltip("Optional — auto-created from the Shockwave2D_Boundary shader if empty.")]
    public Material boundaryMaterial;
    [Tooltip("Must be a sorting layer ABOVE the renderer's Foremost Sorting Layer.")]
    public string sortingLayer = "Power Ups";
    public int sortingOrder = 1;

    MeshFilter mf;
    MeshRenderer mr;
    Material mat;
    Mesh mesh;

    // Reused buffers (resized only when the point count changes) to keep rebuilds GC-light.
    Vector3[] verts;
    Vector3[] norms;
    Vector2[] uvs;
    int[] tris;
    int lastN = -1;
    float lastSig = float.NaN;

    static readonly int ID_Aspect   = Shader.PropertyToID("_Aspect");
    static readonly int ID_Strength = Shader.PropertyToID("_Strength");
    static readonly int ID_Waves    = Shader.PropertyToID("_Waves");
    static readonly int ID_Speed    = Shader.PropertyToID("_Speed");
    static readonly int ID_Chroma   = Shader.PropertyToID("_Chroma");

    IEnumerator Start()
    {
        mf = GetComponent<MeshFilter>();
        mr = GetComponent<MeshRenderer>();

        if (boundaryMaterial == null)
        {
            Shader sh = Shader.Find("Hidden/Shockwave2D_Boundary");
            if (sh == null) { Debug.LogWarning("[MapBoundaryDistortion] shader missing."); enabled = false; yield break; }
            boundaryMaterial = new Material(sh);
        }
        mat = boundaryMaterial;
        mr.sharedMaterial = mat;
        mr.sortingLayerName = sortingLayer;
        mr.sortingOrder = sortingOrder;

        mesh = new Mesh { name = "MapBoundaryDistortion" };
        mesh.MarkDynamic();
        mf.sharedMesh = mesh;

        // Wait for MapManager to build its polygon.
        while (MapManager.i == null || MapManager.i.poly == null || MapManager.i.poly.points.Length < 3)
            yield return null;

        // Parent to the collider so we inherit map expansion + Shrink scaling for free.
        transform.SetParent(MapManager.i.poly.transform, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one;

        Rebuild();
        MapManager.OnUpdateMap += Rebuild;   // committed map changes (placement, expansion)
    }

    void OnDestroy()
    {
        MapManager.OnUpdateMap -= Rebuild;
    }

    void Update()
    {
        if (mat == null) return;

        // Animation (shimmer/breath) runs off _Time in the shader; we just feed live params.
        mat.SetFloat(ID_Aspect, (float)Screen.width / Mathf.Max(1, Screen.height));
        mat.SetFloat(ID_Strength, strength);
        mat.SetFloat(ID_Waves, shimmerWaves);
        mat.SetFloat(ID_Speed, shimmerSpeed);
        mat.SetFloat(ID_Chroma, chroma);

        // The boundary moves continuously while placing an EE / during expansion — follow it.
        if (MapManager.i != null && MapManager.i.poly != null && Changed(MapManager.i.poly.points))
            Rebuild();
    }

    // Cheap change check: point count + a few sampled coordinates.
    bool Changed(Vector2[] pts)
    {
        int n = pts.Length;
        if (n < 3) return false;
        float sig = n
                  + pts[0].x * 1.1f + pts[0].y * 1.3f
                  + pts[n / 3].x * 1.7f + pts[n / 3].y * 1.9f
                  + pts[2 * n / 3].x * 2.3f + pts[2 * n / 3].y * 2.9f;
        if (n != lastN || sig != lastSig) { return true; }
        return false;
    }

    public void Rebuild()
    {
        if (MapManager.i == null || MapManager.i.poly == null) return;
        Vector2[] pts = MapManager.i.poly.points;
        int n = pts.Length;
        if (n < 3) return;

        // (n+1) rows: duplicate row 0 at the end with u = 1, so the seam quad runs uv.x 0.99->1.0
        // instead of folding back to 0.0 (which crammed the whole shimmer into one reversed quad,
        // the "weird" distortion at wherever the spline starts). Matches MapBoundaryWaterfall.
        if (n != lastN || verts == null)
        {
            verts = new Vector3[(n + 1) * 2];
            norms = new Vector3[(n + 1) * 2];
            uvs   = new Vector2[(n + 1) * 2];
            tris  = new int[n * 6];
        }

        // Centroid for outward-normal orientation.
        Vector2 c = Vector2.zero;
        for (int i = 0; i < n; i++) c += pts[i];
        c /= n;

        float hw = width * 0.5f;
        int rows = n + 1;
        for (int r = 0; r < rows; r++)
        {
            int i = r % n;                          // row n duplicates point 0 (closes the seam)
            Vector2 prev = pts[(i - 1 + n) % n];
            Vector2 next = pts[(i + 1) % n];
            Vector2 edge = next - prev;
            Vector2 nrm = new Vector2(edge.y, -edge.x);
            float mag = nrm.magnitude;
            nrm = mag > 1e-5f ? nrm / mag : (pts[i] - c).normalized;
            if (Vector2.Dot(nrm, pts[i] - c) < 0f) nrm = -nrm;   // outward

            verts[2 * r]     = pts[i] + nrm * hw;   // outer
            verts[2 * r + 1] = pts[i] - nrm * hw;   // inner
            norms[2 * r]     = nrm;
            norms[2 * r + 1] = nrm;
            float u = (float)r / n;                 // 0..1 around the rim; 1.0 at the duplicated seam row
            uvs[2 * r]       = new Vector2(u, 1f);  // band outer edge
            uvs[2 * r + 1]   = new Vector2(u, 0f);  // band inner edge
        }

        int ti = 0;
        for (int i = 0; i < n; i++)
        {
            int j = i + 1;                          // row j always exists (rows = n+1); no wrap
            int oi = 2 * i, ii = 2 * i + 1, oj = 2 * j, ij = 2 * j + 1;
            tris[ti++] = oi; tris[ti++] = oj; tris[ti++] = ii;
            tris[ti++] = ii; tris[ti++] = oj; tris[ti++] = ij;
        }

        if (n != lastN) mesh.Clear();
        mesh.vertices  = verts;
        mesh.normals   = norms;
        mesh.uv        = uvs;
        mesh.triangles = tris;
        mesh.RecalculateBounds();

        lastN = n;
        lastSig = n
                + pts[0].x * 1.1f + pts[0].y * 1.3f
                + pts[n / 3].x * 1.7f + pts[n / 3].y * 1.9f
                + pts[2 * n / 3].x * 2.3f + pts[2 * n / 3].y * 2.9f;
    }
}
