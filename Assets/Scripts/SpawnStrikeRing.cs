using UnityEngine;

/// <summary>
/// The arrival mark: one thin ring opening out of the spawn point, or — collapsed to a stub of a
/// circle under a fat line width — the soft dot flash the QuietArc lands on. Both are the same
/// looped LineRenderer wearing the era's glow band, fading by width + `thecolor` rather than by
/// colour alpha (which the Glow Unlit graph ignores).
/// </summary>
public class SpawnStrikeRing : MonoBehaviour
{
    LineRenderer lr;
    Material mat;
    Color baseCol;
    Vector3[] unit;
    float r0, r1, w0, w1, life, t;

    /// <summary>A ring pulse opening at <paramref name="pos"/>, drawn AROUND the unit that just
    /// arrived — <paramref name="unitRadius"/> is its measured half-width, so a small marauder gets
    /// a small circle and a boss gets a wide one.</summary>
    public static SpawnStrikeRing Ring(Vector2 pos, float intensity = 1f,
                                       float unitRadius = SpawnBoltFX.DEFAULT_RADIUS)
    {
        float k = Mathf.Clamp(intensity, 0.6f, 1.4f);
        return Make(pos, "SpawnStrikeRing",
                    r0: unitRadius * 0.3f, r1: unitRadius * 1.25f * k,
                    w0: Mathf.Clamp(unitRadius * 0.13f, 0.03f, 0.3f), w1: 0.01f,
                    life: 0.26f, dim: 1f);
    }

    static SpawnStrikeRing Make(Vector2 pos, string name,
                                float r0, float r1, float w0, float w1, float life, float dim)
    {
        if (!Application.isPlaying) return null;

        // Enough segments to stay round at the ring's WIDEST, but never so many that the circle
        // starts out with sub-MIN_SEG spacing (that is what degenerates LineRenderer strips) — so
        // the count follows r1 and the start radius is floored to match it.
        int segs = Mathf.Clamp(Mathf.RoundToInt(2f * Mathf.PI * Mathf.Max(r1, 0.05f) / 0.16f), 16, 72);
        r0 = Mathf.Max(r0, segs * SpawnBoltFX.MIN_SEG / (2f * Mathf.PI));

        var go = SpawnBoltFX.NewFX(name, pos);
        var ring = go.AddComponent<SpawnStrikeRing>();
        ring.Init(segs, r0, r1, w0, w1, life, dim);
        return ring;
    }

    void Init(int segs, float r0, float r1, float w0, float w1, float life, float dim)
    {
        this.r0 = Mathf.Max(r0, SpawnBoltFX.MIN_SEG);
        this.r1 = Mathf.Max(r1, this.r0);
        this.w0 = w0;
        this.w1 = w1;
        this.life = life;

        unit = new Vector3[segs];
        for (int i = 0; i < segs; i++)
        {
            float a = i / (float)segs * Mathf.PI * 2f;
            unit[i] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
        }

        mat = SpawnBoltFX.NewGlowMat(SpawnBoltFX.BandTex(), out baseCol, dim);
        lr = SpawnBoltFX.NewLR(gameObject, mat, 30);
        lr.loop = true;
        lr.positionCount = segs;
        Apply(0f);
    }

    void Update()
    {
        // A component that never ran Init (a stray serialised into a scene, say) has no line to
        // drive — it takes itself out rather than throwing every frame forever.
        if (lr == null || unit == null) { Destroy(gameObject); return; }

        t += Time.deltaTime;
        float f = Mathf.Clamp01(t / life);
        Apply(f);
        if (f >= 1f) Destroy(gameObject);
    }

    void Apply(float f)
    {
        float ease = 1f - Mathf.Pow(1f - f, 2.2f);          // opens fast, settles
        float r = Mathf.Lerp(r0, r1, ease);
        for (int i = 0; i < unit.Length; i++) lr.SetPosition(i, unit[i] * r);
        lr.widthMultiplier = Mathf.Lerp(w0, w1, f) * Mathf.Pow(1f - f, 0.8f);
        SpawnBoltFX.SetBrightness(mat, baseCol, Mathf.Pow(1f - f, 1.1f));
    }

    void OnDestroy()
    {
        if (mat != null) Destroy(mat);
    }
}
