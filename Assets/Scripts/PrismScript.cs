using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// The deployed Prism crystal. Projects a zone of influence: enemy projectiles
// inside the zone are caught — converted to ally projectiles and refracted at
// the nearest enemy (split in two at level 3) — and enemy units inside are
// slowed (Status system, so act rate drops too). ActionScript.convertProjectiles
// stays on as a contact backstop for anything that slips a scan tick.
// Visuals: rotating dashed zone ring that breathes and flashes on a catch,
// three mint shards orbiting the rim, crystal shimmer + flash frame, Light2D
// sized to the zone. Shatters after its lifetime, max catches, or melee damage.
public class PrismScript : MonoBehaviour
{
    public Sprite[] shimmerSprs;
    public Sprite flashSpr;
    public Sprite shardSpr;
    public SpriteRenderer zone;
    public Light2D glow;
    public float lifetime = 4f;
    public int maxCatches = 6;

    private ActionScript AS;
    private LifeScript ls;
    private SpriteRenderer sr;
    private int level = 1;
    private float atr;
    private float radius = 1.6f;
    private int catches;
    private float animT;
    private float flashT;
    private float scanTimer;
    private float slowTimer;
    private float zoneAlpha = 0.35f;
    private Transform[] shards;
    private readonly HashSet<ProjectileScript> caught = new HashSet<ProjectileScript>();
    private readonly Dictionary<Unit, float> slowGate = new Dictionary<Unit, float>();

    private void Awake()
    {
        AS = GetComponent<ActionScript>();
        ls = GetComponent<LifeScript>();
        sr = GetComponent<SpriteRenderer>();
        AS.rooted = true;
        AS.convertProjectiles = true;
    }

    public void Set(int lvl, float intellect)
    {
        level = lvl;
        atr = intellect;
        lifetime = 3f + level;
        maxCatches = 3 + 3 * level;
        radius = 1.2f + 0.4f * level;
        transform.localScale = Vector3.one * (1f + 0.15f * (level - 1));
    }

    private void Start()
    {
        if (zone != null)
        {
            // ring sprite is 1 world unit across; counter the root's scale
            zone.transform.localScale = Vector3.one * (2f * radius / transform.localScale.x);
        }
        if (glow != null)
        {
            glow.pointLightOuterRadius = radius + 0.3f;
        }
        MakeShards();
        StartCoroutine(Life());
    }

    private void MakeShards()
    {
        if (shardSpr == null) return;
        shards = new Transform[3];
        for (int i = 0; i < shards.Length; i++)
        {
            var g = new GameObject("Shard");
            g.transform.SetParent(transform, false);
            var s = g.AddComponent<SpriteRenderer>();
            s.sprite = shardSpr;
            s.sharedMaterial = sr.sharedMaterial;
            s.sortingLayerID = sr.sortingLayerID;
            s.sortingOrder = sr.sortingOrder - 1;
            s.color = new Color(1f, 1f, 1f, 0.85f);
            g.transform.localScale = Vector3.one * 0.55f;
            shards[i] = g.transform;
        }
    }

    private void Update()
    {
        Animate();
        ScanProjectiles();
        SlowUnits();
    }

    private void Animate()
    {
        if (flashT > 0f)
        {
            flashT -= Time.deltaTime;
            sr.sprite = flashSpr;
        }
        else
        {
            animT += Time.deltaTime * 0.8f;
            if (animT >= 1f) animT -= 1f;
            sr.sprite = GS.PercentParameter(shimmerSprs, animT);
        }
        if (glow != null)
        {
            glow.lightCookieSprite = sr.sprite;
            glow.intensity = Mathf.Lerp(glow.intensity, 0.6f, Time.deltaTime * 5f);
        }
        if (zone != null)
        {
            zone.transform.Rotate(0f, 0f, 25f * Time.deltaTime);
            zoneAlpha = Mathf.Lerp(zoneAlpha, 0.3f + 0.1f * Mathf.Sin(Time.time * 3f), Time.deltaTime * 6f);
            Color c = zone.color;
            c.a = zoneAlpha;
            zone.color = c;
        }
        if (shards != null)
        {
            float r = radius / Mathf.Max(0.01f, transform.localScale.x);
            for (int i = 0; i < shards.Length; i++)
            {
                if (shards[i] == null) continue;
                float a = Time.time * 1.4f + i * (2f * Mathf.PI / shards.Length);
                float wob = 1f + 0.06f * Mathf.Sin(Time.time * 5f + i * 2f);
                shards[i].localPosition = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * (r * wob);
                shards[i].localRotation = Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg - 90f);
            }
        }
    }

    // Catch every enemy projectile inside the zone; also pick up anything the
    // contact backstop converted (engine sets father = this transform).
    private void ScanProjectiles()
    {
        scanTimer -= Time.deltaTime;
        if (scanTimer > 0f) return;
        scanTimer = 0.05f;
        Transform foes = GS.FindParent(GS.Parent.enemyprojectiles);
        for (int i = 0; i < foes.childCount; i++)
        {
            Transform child = foes.GetChild(i);
            if ((child.position - transform.position).sqrMagnitude > radius * radius) continue;
            if (!child.TryGetComponent<ProjectileScript>(out var ps) || caught.Contains(ps)) continue;
            caught.Add(ps);
            ps.Convert();
            ps.father = transform;
            Refract(ps);
        }
        Transform pals = GS.FindParent(GS.Parent.allyprojectiles);
        for (int i = 0; i < pals.childCount; i++)
        {
            if (!pals.GetChild(i).TryGetComponent<ProjectileScript>(out var ps)) continue;
            if (ps.father != transform || caught.Contains(ps)) continue;
            caught.Add(ps);
            Refract(ps);
        }
    }

    private void SlowUnits()
    {
        slowTimer -= Time.deltaTime;
        if (slowTimer > 0f) return;
        slowTimer = 0.4f;
        var foes = GS.FindEnemies(tag, transform.position, radius, false);
        if (foes == null) return;
        foreach (Transform f in foes)
        {
            if (f == null || !f.TryGetComponent<Unit>(out var u)) continue;
            if (slowGate.TryGetValue(u, out float next) && Time.time < next) continue;
            slowGate[u] = Time.time + 1f;
            GS.Stat(u, "slow", 1.2f, 0.55f); // value1 = duration in seconds (Copter/E1_Boss convention)
        }
    }

    private void Refract(ProjectileScript ps)
    {
        catches++;
        flashT = 0.12f;
        zoneAlpha = 0.85f;
        if (glow != null)
        {
            glow.intensity = 1.6f;
        }
        ps.damage *= 1.25f + 0.25f * level + 0.1f * atr;
        ps.timer = Mathf.Max(ps.timer, 1.5f);
        Transform target = GS.FindNearestEnemy(tag, transform.position, 9f, false);
        Vector2 dir = target != null ? (Vector2)(target.position - transform.position) : Random.insideUnitCircle;
        ps.ChangeDirection(dir.normalized);
        if (level >= 3)
        {
            var copy = Instantiate(ps.gameObject, transform.position, Quaternion.identity, GS.FindParent(GS.Parent.allyprojectiles)).GetComponent<ProjectileScript>();
            copy.SetValues(GS.Rotated(dir.normalized, 25f, true), tag);
            copy.father = transform;
            caught.Add(copy);
        }
        if (catches >= maxCatches)
        {
            Shatter();
        }
    }

    private IEnumerator Life()
    {
        yield return new WaitForSeconds(lifetime);
        Shatter();
    }

    private void Shatter()
    {
        if (ls.hasDied) return;
        ls.OnDie();
    }
}
