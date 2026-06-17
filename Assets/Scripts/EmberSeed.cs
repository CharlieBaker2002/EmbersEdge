using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// A seed-mine planted by the Sower (E1_7). Either dropped in place or lobbed
// in an arc at a spot (Lob) — it only starts arming once it lands. Pulses
// faster and brighter as it arms, then erupts into a radial spark burst —
// instantly, if the character or an ally unit steps on it. Shooting it
// before it arms snuffs it quietly (projectiles don't set it off).
public class EmberSeed : MonoBehaviour, IOnCollide
{
    public Sprite[] pulseSprs;
    public GameObject spark;
    public Light2D glow;
    public float armTime = 2.8f;
    public int sparkCount = 4;

    private SpriteRenderer sr;
    private LifeScript ls;
    private Collider2D col;
    private int allyUnitsLayer;
    private int characterLayer;
    private float t;
    private float phase;
    private bool erupted;
    private bool flying;

    private void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        ls = GetComponent<LifeScript>();
        col = GetComponent<Collider2D>();
        allyUnitsLayer = LayerMask.NameToLayer("Ally Units");
        characterLayer = LayerMask.NameToLayer("Character");
        var AS = GetComponent<ActionScript>();
        if (AS != null && !AS.onCollides.Contains(this))
        {
            AS.onCollides.Add(this);
        }
    }

    // stepping on a seed sets it off — units and the character only, never projectiles
    public void OnCollide(Collision2D c)
    {
        if (erupted || flying || ls.hasDied || c.collider == null) return;
        int l = c.collider.gameObject.layer;
        if (l == allyUnitsLayer || l == characterLayer)
        {
            Erupt();
        }
    }

    public void Lob(Vector2 dest)
    {
        flying = true;
        StartCoroutine(LobI(dest));
    }

    private IEnumerator LobI(Vector2 dest)
    {
        col.enabled = false;
        Vector3 start = transform.position;
        float dur = 0.75f + 0.12f * Vector2.Distance(start, dest); // unhurried, readable arc
        for (float f = 0f; f < 1f; f += Time.deltaTime / dur)
        {
            float h = Mathf.Sin(f * Mathf.PI); // fake height for the top-down arc
            transform.position = Vector3.Lerp(start, dest, f);
            transform.localScale = Vector3.one * (1f + 0.7f * h);
            if (glow != null)
            {
                glow.intensity = 0.2f + 0.35f * h;
            }
            yield return null;
        }
        transform.position = dest;
        transform.localScale = Vector3.one;
        col.enabled = true;
        flying = false;
    }

    private void Update()
    {
        if (erupted || flying) return;
        t += Time.deltaTime;
        float a = Mathf.Clamp01(t / armTime);
        phase += Time.deltaTime * (1.5f + 8f * a * a); // blink accelerates as it arms
        float pulse = 0.5f + 0.5f * Mathf.Sin(phase * Mathf.PI * 2f);
        sr.sprite = GS.PercentParameter(pulseSprs, Mathf.Clamp01(pulse * (0.3f + 0.7f * a)));
        transform.localScale = Vector3.one * (1f + 0.08f * a * Mathf.Sin(phase * Mathf.PI * 2f));
        if (glow != null)
        {
            glow.lightCookieSprite = sr.sprite;
            glow.intensity = 0.25f + a * (0.35f + 0.65f * pulse);
        }
        if (t >= armTime)
        {
            Erupt();
        }
    }

    private void Erupt()
    {
        erupted = true;
        float baseAng = Random.Range(0f, 90f);
        for (int i = 0; i < sparkCount; i++)
        {
            var p = Instantiate(spark, transform.position, Quaternion.identity, GS.FindParent(GS.Parent.enemyprojectiles));
            p.GetComponent<ProjectileScript>().SetValues(GS.VTheta(baseAng + i * (360f / sparkCount)), tag);
        }
        Shockwave.Spawn(transform.position, 1.1f, 0.012f, 0.4f);
        ls.OnDie();
    }
}
