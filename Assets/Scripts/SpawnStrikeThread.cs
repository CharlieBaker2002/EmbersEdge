using UnityEngine;

/// <summary>
/// The spawn strike itself — the quietest reading of "something was sent here". One straight thread
/// grows from the source to the enemy; the hot head is baked into the strip texture at u = 1 and
/// the LineRenderer stretches that texture over whatever length is currently drawn, so the head
/// rides the growing tip with no geometry churn at all (the shader does the travelling). The
/// moment it lands it drops a ring pulse, then the whole thread dims and thins away.
/// </summary>
public class SpawnStrikeThread : MonoBehaviour
{
    /// <summary>Flight time — SpawnBoltFX.ArrivalDelay reads this to time the enemy's entrance.</summary>
    internal const float TRAVEL = 0.12f;   // source → enemy
    const float HOLD   = 0.03f;   // the thread sits at full length for a beat
    const float FADE   = 0.20f;   // then dims/thins to nothing

    LineRenderer lr;
    Material mat;
    Color baseCol;
    Vector3 end;                  // enemy, in local space (the GO sits on the source)
    Vector2 headWorld;
    float w0, t;
    float intensity = 1f, unitRadius = SpawnBoltFX.DEFAULT_RADIUS;
    bool arrived;

    public void Init(Vector2 from, Vector2 to, float intensity, float unitRadius = SpawnBoltFX.DEFAULT_RADIUS)
    {
        this.intensity = intensity;
        this.unitRadius = unitRadius;
        headWorld = to;
        end = new Vector3(to.x - from.x, to.y - from.y, 0f);

        mat = SpawnBoltFX.NewGlowMat(SpawnBoltFX.ThreadTex(), out baseCol);
        lr = SpawnBoltFX.NewLR(gameObject, mat, 29);
        lr.positionCount = 2;
        w0 = 0.05f * Mathf.Clamp(intensity, 0.6f, 1.4f);
        lr.widthMultiplier = w0;
        Draw(Mathf.Max(SpawnBoltFX.MIN_SEG / Mathf.Max(end.magnitude, 0.001f), 0.02f));
    }

    void Update()
    {
        if (lr == null) { Destroy(gameObject); return; }   // never ran Init — see SpawnBoltFX.NewFX

        t += Time.deltaTime;

        if (t < TRAVEL)
        {
            Draw(Mathf.Pow(t / TRAVEL, 0.7f));       // eases out as it lands
            return;
        }

        if (!arrived)
        {
            arrived = true;
            Draw(1f);
            SpawnStrikeRing.Ring(headWorld, intensity, unitRadius);
        }

        float f = Mathf.Clamp01((t - TRAVEL - HOLD) / FADE);
        lr.widthMultiplier = w0 * Mathf.Pow(1f - f, 1.4f);
        SpawnBoltFX.SetBrightness(mat, baseCol, 1f - f);
        if (f >= 1f) Destroy(gameObject);
    }

    /// <summary>Draw the first <paramref name="u"/> of the run — never shorter than a segment
    /// that still has finite geometry.</summary>
    void Draw(float u)
    {
        Vector3 tip = end * Mathf.Clamp01(u);
        if (tip.sqrMagnitude < SpawnBoltFX.MIN_SEG * SpawnBoltFX.MIN_SEG)
            tip = end.normalized * SpawnBoltFX.MIN_SEG;
        lr.SetPosition(0, Vector3.zero);
        lr.SetPosition(1, tip);
    }

    void OnDestroy()
    {
        if (mat != null) Destroy(mat);
    }
}
