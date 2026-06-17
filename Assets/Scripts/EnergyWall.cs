using System.Collections;
using UnityEngine;

// The wall projected by the Force Field building. One LifeScript covers the whole
// span — when any part of it falls, all of it falls. Geometry is a gentle bezier
// between two endpoints (bowing away from the tower); an EdgeCollider2D plus
// ActionScript.building makes it block and be attackable like any building.
// Visuals are a LineRenderer running the ForceField2D shader: intensity tracks
// stored charge and dims when the wall is stretched past its natural length or
// while it is re-weaving to a new shape (collider off — reshaping isn't free).
public class EnergyWall : MonoBehaviour
{
    public LineRenderer lr;
    public EdgeCollider2D col;
    public LifeScript ls;
    public float naturalLength = 2.4f;
    [SerializeField] private float width = 0.3f;

    private Vector2 a, b;
    private bool weaving;
    private float flickerT;
    private MaterialPropertyBlock mpb;
    private static readonly int IntensityID = Shader.PropertyToID("_Intensity");
    private static readonly int WallLenID = Shader.PropertyToID("_WallLen");

    public float Hp => ls.hp;
    public float MaxHp => ls.maxHp;
    public bool Weaving => weaving;

    public void Init(Vector2 aWorld, Vector2 bWorld, float hp)
    {
        ls.hp = Mathf.Clamp(hp, 1f, ls.maxHp);
        SetShape(aWorld, bWorld);
    }

    public void Heal(float amount)
    {
        if (ls.hasDied || amount <= 0f) return;
        ls.Change(Mathf.Min(amount, ls.maxHp - ls.hp), -1, false, false);
    }

    public void Reweave(Vector2 aWorld, Vector2 bWorld, float t)
    {
        StopAllCoroutines();
        StartCoroutine(ReweaveI(aWorld, bWorld, t));
    }

    private IEnumerator ReweaveI(Vector2 aWorld, Vector2 bWorld, float t)
    {
        weaving = true;
        col.enabled = false;
        Vector2 a0 = a, b0 = b;
        for (float f = 0f; f < 1f; f += Time.deltaTime / Mathf.Max(0.1f, t))
        {
            float e = f * f * (3f - 2f * f);
            SetShape(Vector2.Lerp(a0, aWorld, e), Vector2.Lerp(b0, bWorld, e));
            yield return null;
        }
        SetShape(aWorld, bWorld);
        col.enabled = true;
        weaving = false;
    }

    private void SetShape(Vector2 aWorld, Vector2 bWorld)
    {
        a = aWorld;
        b = bWorld;
        const int n = 14;
        Vector2 mid = (a + b) * 0.5f;
        Vector2 away = mid - (Vector2)transform.position;
        away = away.sqrMagnitude > 1e-4f ? away.normalized : Vector2.up;
        float len = Vector2.Distance(a, b);
        Vector2 ctrl = mid + away * (0.12f * len);
        var pts = new Vector3[n];
        var cpts = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            float u = i / (n - 1f);
            Vector2 p = (1 - u) * (1 - u) * a + 2 * (1 - u) * u * ctrl + u * u * b;
            pts[i] = new Vector3(p.x, p.y, 0f);
            cpts[i] = p - (Vector2)transform.position;
        }
        lr.positionCount = n;
        lr.SetPositions(pts);
        col.points = cpts;
    }

    private void Update()
    {
        float len = Vector2.Distance(a, b) * 1.02f;
        float stretch = Mathf.Clamp01(naturalLength / Mathf.Max(naturalLength, len));
        float hpFrac = Mathf.Clamp01(ls.hp / ls.maxHp);
        float intensity = (0.35f + 0.65f * hpFrac) * (0.4f + 0.6f * stretch);
        if (weaving)
        {
            flickerT += Time.deltaTime * 9f;
            intensity *= 0.25f + 0.15f * Mathf.Sin(flickerT * Mathf.PI * 2f);
        }
        if (mpb == null) mpb = new MaterialPropertyBlock();
        lr.GetPropertyBlock(mpb);
        mpb.SetFloat(IntensityID, intensity);
        mpb.SetFloat(WallLenID, Mathf.Max(0.1f, len));
        lr.SetPropertyBlock(mpb);
        lr.widthMultiplier = width * (0.7f + 0.5f * stretch);
    }
}
