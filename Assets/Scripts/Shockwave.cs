using System.Collections;
using UnityEngine;

/// <summary>
/// A one-shot screen-distortion shockwave for the URP 2D Renderer.
/// Drop the "Shockwave" prefab (Resources) at a world position, or call <see cref="Spawn"/>.
/// The ring rolls outward, its crest thinning as it travels, and the whole thing decays smoothly.
/// Requires the renderer's "Camera Sorting Layer Texture" to be enabled (see Shockwave2D.shader).
/// </summary>
public class Shockwave : MonoBehaviour
{
    [Header("Shape (world units)")]
    [Tooltip("How far the ring travels from the centre.")]
    public float maxRadius = 6f;
    [Tooltip("Crest thickness when the blast is born.")]
    public float startWidth = 1.2f;
    [Tooltip("Crest thickness once fully expanded — keep below startWidth so it sharpens with distance.")]
    public float endWidth = 0.25f;

    [Header("Feel")]
    [Tooltip("Peak UV displacement, as a fraction of screen height.")]
    public float strength = 0.045f;
    [Tooltip("Chromatic-aberration amount along the push.")]
    public float chroma = 0.4f;
    [Tooltip("Seconds for the full sweep.")]
    public float lifetime = 0.55f;

    [Header("Wiring")]
    [Tooltip("The Shockwave material (Shockwave2D shader). Auto-found from Resources/shader if left empty.")]
    public Material shockMaterial;
    [Tooltip("Must be a sorting layer ABOVE the renderer's Foremost Sorting Layer so it can read the copied scene.")]
    public string sortingLayer = "Power Ups";
    public int sortingOrder = 0;

    [HideInInspector] public bool externalDrive = false;   // when true, Drive() is called by an owner each frame

    SpriteRenderer sr;
    Material mat;
    Camera cam;

    static Sprite s_quad;
    static readonly int ID_Strength  = Shader.PropertyToID("_Strength");
    static readonly int ID_Radius    = Shader.PropertyToID("_Radius");
    static readonly int ID_RingWidth = Shader.PropertyToID("_RingWidth");
    static readonly int ID_Center    = Shader.PropertyToID("_Center");
    static readonly int ID_Aspect    = Shader.PropertyToID("_Aspect");
    static readonly int ID_Chroma    = Shader.PropertyToID("_Chroma");

    /// <summary>Instantiate the Shockwave prefab at a world position with optional overrides.</summary>
    public static Shockwave Spawn(Vector3 worldPos, float radius = -1f, float strength = -1f, float lifetime = -1f, float chroma = -1f)
    {
        GameObject prefab = Resources.Load<GameObject>("Shockwave");
        if (prefab == null)
        {
            Debug.LogWarning("[Shockwave] Resources/Shockwave prefab not found.");
            return null;
        }
        GameObject go = Instantiate(prefab, (Vector2)worldPos, Quaternion.identity);
        Shockwave sw = go.GetComponent<Shockwave>();
        if (sw != null)
        {
            if (radius   > 0f) sw.maxRadius = radius;
            if (strength > 0f) sw.strength  = strength;
            if (lifetime > 0f) sw.lifetime  = lifetime;
            if (chroma > 0f) sw.chroma  = chroma;
        }
        return sw;
    }
    
    public static void EEWave(Vector2 position, float rad = 15f)
    {
        Instantiate(EmbersEdge.mainCore.eeWaveCompleteFX,position,Quaternion.identity,GS.FindParent(GS.Parent.fx)).GetComponent<EEWaveCompleteFX>().ringRadius = rad;
    }

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        if (sr == null) sr = gameObject.AddComponent<SpriteRenderer>();

        // A 1x1 white sprite at 1 px-per-unit = a 1-world-unit quad we stretch to cover the ring.
        if (s_quad == null)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            t.SetPixel(0, 0, Color.white);
            t.Apply();
            s_quad = Sprite.Create(t, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        }
        sr.sprite = s_quad;

        if (shockMaterial == null)
        {
            Shader sh = Shader.Find("Hidden/Shockwave2D");
            if (sh != null) shockMaterial = new Material(sh);
        }
        sr.sharedMaterial = shockMaterial;
        mat = sr.material;                 // per-instance copy we can animate freely
        sr.sortingLayerName = sortingLayer;
        sr.sortingOrder = sortingOrder;
    }

    void Start()
    {
        // Fire-and-forget by default (dropped prefab or Spawn()); owners that drive it
        // manually set externalDrive (via DriveBegin) before this runs.
        if (!externalDrive) Play();
    }

    /// <summary>Self-animate the sweep, then destroy. Used by Spawn() and dropped prefabs.</summary>
    public void Play()
    {
        cam = Camera.main;
        StartCoroutine(Run());
    }

    /// <summary>Switch to manual mode: the owner positions this transform and calls Drive() each frame.</summary>
    public void DriveBegin()
    {
        externalDrive = true;
        cam = Camera.main;
        if (mat != null) mat.SetFloat(ID_Chroma, chroma);
    }

    /// <summary>Push one frame of the ring in world units. Set transform.position to the centre first.</summary>
    public void Drive(float worldRadius, float worldWidth, float strengthNow)
    {
        if (mat == null) return;
        if (cam == null) cam = Camera.main;
        if (cam == null) return;
        float diameter = 2f * (worldRadius + worldWidth);
        transform.localScale = new Vector3(diameter, diameter, 1f);
        PushToMaterial(worldRadius, Mathf.Max(0.0001f, worldWidth), strengthNow);
    }

    /// <summary>Tear down a manually-driven shockwave.</summary>
    public void Finish()
    {
        Destroy(gameObject);
    }

    IEnumerator Run()
    {
        if (cam == null) cam = Camera.main;

        // Stretch the quad to comfortably enclose the fully-grown ring.
        float diameter = 2f * (maxRadius + startWidth);
        transform.localScale = new Vector3(diameter, diameter, 1f);

        if (mat == null || cam == null) { Destroy(gameObject); yield break; }

        mat.SetFloat(ID_Chroma, chroma);

        for (float time = 0f; time < lifetime; time += Time.deltaTime)
        {
            float n = Mathf.Clamp01(time / lifetime);

            // Ease-out cubic: fast punch, smooth settle.
            float eased = 1f - Mathf.Pow(1f - n, 3f);
            float worldRadius = maxRadius * eased;
            float worldWidth  = Mathf.Lerp(startWidth, endWidth, eased);   // crest thins as it travels
            float decay       = (1f - n) * (1f - n);                       // smooth strength falloff

            PushToMaterial(worldRadius, worldWidth, strength * decay);
            yield return null;
        }

        Destroy(gameObject);
    }

    void PushToMaterial(float worldRadius, float worldWidth, float curStrength)
    {
        // Convert world sizes to viewport-height fractions (orthographic 2D).
        float ortho = Mathf.Max(0.0001f, cam.orthographicSize);
        float worldToVp = 0.5f / ortho;

        Vector3 vp = cam.WorldToViewportPoint(transform.position);
        mat.SetVector(ID_Center, new Vector4(vp.x, vp.y, 0f, 0f));
        mat.SetFloat(ID_Aspect, (float)Screen.width / Mathf.Max(1, Screen.height));
        mat.SetFloat(ID_Radius, worldRadius * worldToVp);
        mat.SetFloat(ID_RingWidth, Mathf.Max(0.001f, worldWidth * worldToVp));
        mat.SetFloat(ID_Strength, curStrength);
    }
}
