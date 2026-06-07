using System.Collections;
using UnityEngine;

/// <summary>
/// "Edge of the world" waterfall along the map boundary, done as screen distortion (like the
/// shockwave): a ribbon hangs off the outer side of the edge and the EdgeWaterfall2D shader pushes
/// the sampled scene along the outward normal by a LOOPING profile that keeps returning to 0, so the
/// world appears to pour off the edge in repeating cascades. Wraps around the whole rim, follows the
/// live boundary (rebuilds on change, parents to the collider for reshaping + Shrink scaling).
///
/// Drop on an empty GameObject (e.g. a child of MapManager). Needs the renderer's "Camera Sorting
/// Layer Texture" enabled (same as Shockwave2D) and must sit ABOVE its Foremost Sorting Layer.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class MapBoundaryWaterfall : MonoBehaviour
{
    [Header("Ribbon (world units)")]
    [Tooltip("How far the waterfall hangs below/outside the edge.")]
    public float width = 1.6f;
    [Tooltip("How far the lip starts inside the edge.")]
    public float innerInset = 0.15f;

    [Header("Dynamic sizing")]
    [Tooltip("Let the ribbon width swell and shrink a little with noise.")]
    public bool dynamicSizing = false;
    [Tooltip("How much the width varies (fraction of width).")]
    [Range(0f, 1f)] public float widthNoiseAmount = 0.25f;
    [Tooltip("Spatial frequency of the wobble around the rim.")]
    public float widthNoiseScale = 2.5f;
    [Tooltip("How fast the wobble animates.")]
    public float widthNoiseSpeed = 0.5f;

    [Header("Distortion")]
    [Tooltip("Peak screen-space push, as a fraction of screen height.")]
    public float strength = 0.05f;
    [Tooltip("Falling strands per world unit around the rim.")]
    public float strandsPerUnit = 3f;
    [Range(0f, 1f)] public float strandWidth = 0.45f;
    [Tooltip("How many cascade loops stack down the fall.")]
    public float stripes = 3f;
    public float fallSpeed = 0.6f;
    [Range(0f, 1f)] public float speedVariance = 0.6f;
    public float chroma = 0.3f;
    [Tooltip("Pile-up toward the outer edge: 0 ≈ even, higher = faster at the lip and more repeated copies at the end.")]
    public float magnification = 2.5f;

    [Header("Wiring")]
    public Material waterfallMaterial;   // auto-created from EdgeWaterfall2D if empty
    [Tooltip("Must be ABOVE the renderer's Foremost Sorting Layer to read the scene copy.")]
    public string sortingLayer = "Power Ups";
    public int sortingOrder = 4;

    MeshFilter mf;
    MeshRenderer mr;
    Material mat;
    Mesh mesh;

    Vector3[] verts;
    Vector3[] norms;
    Vector2[] uvs;
    int[] tris;
    int lastN = -1;
    float lastSig = float.NaN;

    float perimeter;

    static readonly int ID_Strength = Shader.PropertyToID("_Strength");
    static readonly int ID_Aspect   = Shader.PropertyToID("_Aspect");
    static readonly int ID_Strands  = Shader.PropertyToID("_StrandCount");
    static readonly int ID_Strand   = Shader.PropertyToID("_StrandWidth");
    static readonly int ID_Stripes  = Shader.PropertyToID("_Stripes");
    static readonly int ID_Speed    = Shader.PropertyToID("_Speed");
    static readonly int ID_SpeedV   = Shader.PropertyToID("_SpeedVar");
    static readonly int ID_Chroma   = Shader.PropertyToID("_Chroma");
    static readonly int ID_Mag      = Shader.PropertyToID("_Mag");

    IEnumerator Start()
    {
        mf = GetComponent<MeshFilter>();
        mr = GetComponent<MeshRenderer>();

        if (waterfallMaterial == null)
        {
            Shader sh = Shader.Find("Hidden/EdgeWaterfall2D");
            if (sh == null) { Debug.LogWarning("[MapBoundaryWaterfall] shader missing."); enabled = false; yield break; }
            waterfallMaterial = new Material(sh);
        }
        mat = waterfallMaterial;
        mr.sharedMaterial = mat;
        mr.sortingLayerName = sortingLayer;
        mr.sortingOrder = sortingOrder;

        mesh = new Mesh { name = "MapBoundaryWaterfall" };
        mesh.MarkDynamic();
        mf.sharedMesh = mesh;

        while (MapManager.i == null || MapManager.i.poly == null || MapManager.i.poly.points.Length < 3)
            yield return null;

        transform.SetParent(MapManager.i.poly.transform, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one;

        Rebuild();
        MapManager.OnUpdateMap += Rebuild;
    }

    void OnDestroy()
    {
        MapManager.OnUpdateMap -= Rebuild;
    }

    void Update()
    {
        if (mat == null) return;

        // Animation/loop runs off _Time in the shader; feed live params.
        mat.SetFloat(ID_Strength, strength);
        mat.SetFloat(ID_Aspect, (float)Screen.width / Mathf.Max(1, Screen.height));
        // Integer strand count over the whole rim -> seamless, uniform strand size.
        mat.SetFloat(ID_Strands, Mathf.Max(1f, Mathf.Round(perimeter * strandsPerUnit)));
        mat.SetFloat(ID_Strand, strandWidth);
        mat.SetFloat(ID_Stripes, stripes);
        mat.SetFloat(ID_Speed, fallSpeed);
        mat.SetFloat(ID_SpeedV, speedVariance);
        mat.SetFloat(ID_Chroma, chroma);
        mat.SetFloat(ID_Mag, magnification);

        // Dynamic sizing animates the width, so rebuild every frame; otherwise only on map changes.
        if (MapManager.i != null && MapManager.i.poly != null
            && (dynamicSizing || Changed(MapManager.i.poly.points)))
            Rebuild();
    }

    bool Changed(Vector2[] pts)
    {
        int n = pts.Length;
        if (n < 3) return false;
        float sig = n
                  + pts[0].x * 1.1f + pts[0].y * 1.3f
                  + pts[n / 3].x * 1.7f + pts[n / 3].y * 1.9f
                  + pts[2 * n / 3].x * 2.3f + pts[2 * n / 3].y * 2.9f;
        return n != lastN || sig != lastSig;
    }

    public void Rebuild()
    {
        if (MapManager.i == null || MapManager.i.poly == null) return;
        Vector2[] pts = MapManager.i.poly.points;
        int n = pts.Length;
        if (n < 3) return;

        // (n+1) ring rows: duplicate row 0 at the end with u = total length, so the seam doesn't
        // interpolate uv.x back to 0.
        int rows = n + 1;
        if (rows != lastN + 1 || verts == null)
        {
            verts = new Vector3[rows * 2];
            norms = new Vector3[rows * 2];
            uvs   = new Vector2[rows * 2];
            tris  = new int[n * 6];
        }

        Vector2 c = Vector2.zero;
        for (int k = 0; k < n; k++) c += pts[k];
        c /= n;

        // Total rim length first, so uv.x can be normalised 0..1 (seamless strand wrap).
        float total = 0f;
        for (int k = 0; k < n; k++) total += Vector2.Distance(pts[k], pts[(k + 1) % n]);
        perimeter = total;
        float invTotal = total > 1e-5f ? 1f / total : 0f;
        float noiseT = dynamicSizing ? Time.time * widthNoiseSpeed : 0f;

        float cum = 0f;
        for (int r = 0; r < rows; r++)
        {
            int i = r % n;
            Vector2 p = pts[i];
            Vector2 prev = pts[(i - 1 + n) % n];
            Vector2 next = pts[(i + 1) % n];
            Vector2 edge = next - prev;
            Vector2 nrm = new Vector2(edge.y, -edge.x);
            float mag = nrm.magnitude;
            nrm = mag > 1e-5f ? nrm / mag : (p - c).normalized;
            if (Vector2.Dot(nrm, p - c) < 0f) nrm = -nrm;   // outward

            float uu = cum * invTotal;                 // 0..1 around the rim (1.0 at the duplicated seam row)

            // Dynamic width: noise sampled around a circle (seamless at the rim wrap) sliding over time.
            float wW = width;
            if (dynamicSizing)
            {
                float ang = uu * 6.2831853f;
                float nz = Mathf.PerlinNoise(Mathf.Cos(ang) * widthNoiseScale + noiseT,
                                             Mathf.Sin(ang) * widthNoiseScale);
                wW = width * Mathf.Max(0.05f, 1f + widthNoiseAmount * (nz - 0.5f) * 2f);
            }

            verts[2 * r]     = p - nrm * innerInset;   // lip (uv.y = 0)
            verts[2 * r + 1] = p + nrm * wW;           // bottom (uv.y = 1)
            norms[2 * r] = nrm; norms[2 * r + 1] = nrm;
            uvs[2 * r]     = new Vector2(uu, 0f);
            uvs[2 * r + 1] = new Vector2(uu, 1f);

            if (r < rows - 1)
                cum += Vector2.Distance(p, pts[(i + 1) % n]);
        }

        int ti = 0;
        for (int r = 0; r < n; r++)
        {
            int a = 2 * r, b = 2 * r + 1, c2 = 2 * (r + 1), d = 2 * (r + 1) + 1;
            tris[ti++] = a; tris[ti++] = c2; tris[ti++] = b;
            tris[ti++] = b; tris[ti++] = c2; tris[ti++] = d;
        }

        mesh.Clear();
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
