using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// Reusable "engine" swirl: a cloud of orbs animated with the collected-orb hover
/// algorithm (the old OrbManager hover state — Perlin wander targets, 4/s lerp,
/// re-seeded phase every 2-4s), all around this transform's local (0,0).
/// The orb sprite is a 1x1 white pixel (emberorb), so the era material applied on
/// top carries the lighting/emission and the per-orb tint carries the colour.
/// </summary>
public class EngineAnimation : MonoBehaviour
{
    public int count = 100;
    public Sprite sprite;              // emberorb_0 (1x1 white @ 32ppu)
    public Material material;          // era material layered over the white sprite
    public float distortion = 1f;      // horizontal stretch of the wander field
    public float radius = 1f;          // scales the whole wander area
    public float orbScale = 2f;        // localScale per orb (sprite is 1/32 unit, so 2 = 1/16)
    public int sortingLayerID;
    public int sortingOrder;

    static readonly Color[] palette =
    {
        new Color(0.62f, 0.26f, 0.86f), // purple
        new Color(1f, 0.55f, 0.12f),    // orange
        new Color(1f, 0.86f, 0.25f),    // yellow
    };

    Transform[] orbs;
    float[] theta;
    float[] hovTimer;
    float[] rot;

    void Awake()
    {
        Build();
    }

    void Build()
    {
        if (orbs != null) return;
        orbs = new Transform[count];
        theta = new float[count];
        hovTimer = new float[count];
        rot = new float[count];
        for (int i = 0; i < count; i++)
        {
            var go = new GameObject("engine orb");
            go.layer = gameObject.layer;
            var t = go.transform;
            t.SetParent(transform, false);
            t.localPosition = Random.insideUnitCircle * 0.3f * radius;
            t.localScale = Vector3.one * orbScale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            if (material != null) sr.sharedMaterial = material;
            sr.color = palette[i % palette.Length];
            sr.sortingLayerID = sortingLayerID;
            sr.sortingOrder = sortingOrder;
            orbs[i] = t;
            theta[i] = Random.Range(0f, 2f * Mathf.PI);
            rot[i] = Random.Range(0, 8) * 45f;              // white-orb rotation quantisation
            hovTimer[i] = Random.Range(0.01f, 4f);          // staggered so re-seeds don't sync
        }
    }

    /// <summary>Era swap seam — restyle every orb in place.</summary>
    public void ApplyMaterial(Material m)
    {
        material = m;
        if (orbs == null) return;
        for (int i = 0; i < orbs.Length; i++)
        {
            if (orbs[i] == null) continue;
            orbs[i].GetComponent<SpriteRenderer>().sharedMaterial = m;
        }
    }

    void Update()
    {
        float dt = Time.deltaTime;
        float time = Time.time;
        for (int i = 0; i < count; i++)
        {
            Transform tr = orbs[i];
            if (tr == null) continue;
            hovTimer[i] -= dt;
            if (hovTimer[i] > 0f)
            {
                // the collected-orb hover: Perlin wander target around local (0,0)
                Vector2 target = new Vector2(
                    distortion * (-0.5f + Mathf.PerlinNoise(Mathf.Sin(theta[i] + 0.8f * time), 0.35f * time)),
                    -0.5f + Mathf.PerlinNoise(Mathf.Cos(theta[i] + 0.8f * time), 0.35f * time))
                    .Rotated(rot[i]) * radius;
                tr.localPosition = Vector3.Lerp(tr.localPosition, target, 4f * dt);
            }
            else if (hovTimer[i] < 0f)
            {
                theta[i] = Random.Range(0f, 2f * Mathf.PI);
                hovTimer[i] = Random.Range(2f, 4f);
            }
        }
    }
}
