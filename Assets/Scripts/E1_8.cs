using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// The Triptych: D1 mini-boss. An unblinking red eye in a hollow purple diamond,
// attended by three detached panels that orbit it — shield, body and weapon at
// once. Dungeon-only; fights the character.
//
// Kit: aimed bolt volleys with lead targeting, a telegraphed panel-lance throw,
// a charge-up radial shard bloom (panels contract and MIRROR player projectiles
// while charging), homing rift wisps that burst into bolts when they die, and a
// Mirror Waltz — an immaterial blink through the player when crowded. Strafes
// at mid range, sidesteps projectiles on a collision course, and rages at half
// health (stim, denser patterns, lit panels). Flings its panels as lances on death.
public class E1_8 : Unit, IRoomUnit, IOnDeath
{
    [Header("Core sprites")]
    public Sprite[] idleSprs;
    public Sprite[] chargeSprs;
    public Sprite[] rageSprs;
    [Header("Panels")]
    public Transform[] panels;
    public SpriteRenderer[] panelSRs;
    public Sprite panelIdle;
    public Sprite panelLit;
    public Sprite panelFlash;
    [Header("Projectiles")]
    public GameObject bolt;
    public GameObject shard;
    public GameObject wisp;
    public GameObject lance;
    public Light2D glow;

    private Transform cs;
    private Collider2D roomBounds;
    private bool phase2;
    private bool holding;
    private bool dashing;
    private float strafeSign = 1f;
    private float strafeTimer;
    private float dodgeReadyAt;
    private float waltzReadyAt;
    private float animT;
    private bool overrideAnim;
    private float orbitAngle;
    private float panelRadius = 0.55f;
    private float panelRadiusTarget = 0.55f;
    private float panelSpin = 70f;
    private float glowTarget = 0.55f;
    private int lastAttack = -1;

    private void Awake()
    {
        AS = GetComponent<ActionScript>();
        strafeSign = GS.PlusMinus();
    }

    protected override void Start()
    {
        base.Start();
        cs = GS.CS();
        ls.onDamageDelegate += OnDamaged;
        StartCoroutine(Brain());
    }

    public void RecieveRoom(Collider2D bounds, Vector2 pos)
    {
        roomBounds = bounds;
    }

    protected override void Update()
    {
        base.Update();
        Animate();
        OrbitPanels();
        if (!dashing)
        {
            Move();
            DodgeScan();
        }
    }

    // ---------- presentation ----------
    private void Animate()
    {
        if (!overrideAnim)
        {
            animT += Time.deltaTime * actRate * 0.8f;
            if (animT >= 1f) animT -= 1f;
            sr.sprite = GS.PercentParameter(phase2 ? rageSprs : idleSprs, animT);
        }
        if (glow != null)
        {
            glow.lightCookieSprite = sr.sprite;
            glow.intensity = Mathf.Lerp(glow.intensity, glowTarget, Time.deltaTime * 4f);
        }
        glowTarget = Mathf.Lerp(glowTarget, phase2 ? 0.8f : 0.55f, Time.deltaTime);
    }

    private void OrbitPanels()
    {
        orbitAngle += panelSpin * Time.deltaTime * actRate;
        panelRadius = Mathf.Lerp(panelRadius, panelRadiusTarget, Time.deltaTime * 6f);
        for (int i = 0; i < panels.Length; i++)
        {
            if (panels[i] == null || !panels[i].gameObject.activeSelf) continue;
            float a = (orbitAngle + i * 120f) * Mathf.Deg2Rad;
            Vector3 pos = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * panelRadius;
            panels[i].localPosition = Vector3.Lerp(panels[i].localPosition, pos, Time.deltaTime * 10f);
            panels[i].localRotation = Quaternion.Euler(0f, 0f, orbitAngle + i * 120f - 90f);
        }
    }

    private void SetPanelSprites(Sprite s)
    {
        foreach (SpriteRenderer p in panelSRs)
        {
            if (p != null) p.sprite = s;
        }
    }

    private void ResetPanels()
    {
        panelRadiusTarget = 0.55f;
        panelSpin = phase2 ? 110f : 70f;
        SetPanelSprites(phase2 ? panelLit : panelIdle);
    }

    private void SetAlpha(float a)
    {
        Color c = sr.color; c.a = a; sr.color = c;
        foreach (SpriteRenderer p in panelSRs)
        {
            if (p == null) continue;
            Color pc = p.color; pc.a = a; p.color = pc;
        }
    }

    // ---------- movement ----------
    private void Move()
    {
        if (cs == null) return;
        if (holding)
        {
            AS.TryAddForce(-0.8f * AS.mass * AS.rb.linearVelocity, true);
            return;
        }
        Vector2 toC = cs.position - transform.position;
        float dist = toC.magnitude;
        strafeTimer -= Time.deltaTime * actRate;
        if (strafeTimer < 0f)
        {
            strafeTimer = Random.Range(1.6f, 3f);
            if (GS.Chance(40f)) strafeSign = -strafeSign;
        }
        // forces scale with mass: FixedUpdate applies force * dt / mass
        Vector2 tang = new Vector2(-toC.y, toC.x).normalized * strafeSign;
        Vector2 force = toC.normalized * Mathf.Clamp((dist - 3f) * 0.5f, -1f, 1f) + tang * 0.7f;
        force += RoomPull();
        AS.TryAddForce(actRate * AS.mass * force, true);
    }

    private Vector2 RoomPull() // gentle pull back inside the room bounds
    {
        if (roomBounds == null) return Vector2.zero;
        Bounds b = roomBounds.bounds;
        Vector3 p = transform.position;
        Vector2 pull = Vector2.zero;
        if (p.x < b.min.x + 1f) pull.x = 0.1f;
        else if (p.x > b.max.x - 1f) pull.x = -0.1f;
        if (p.y < b.min.y + 1f) pull.y = 0.1f;
        else if (p.y > b.max.y - 1f) pull.y = -0.1f;
        return pull;
    }

    private Vector2 ClampToRoom(Vector2 p)
    {
        if (roomBounds == null) return p;
        Bounds b = roomBounds.bounds;
        return new Vector2(Mathf.Clamp(p.x, b.min.x + 0.8f, b.max.x - 0.8f),
                           Mathf.Clamp(p.y, b.min.y + 0.8f, b.max.y - 0.8f));
    }

    // sidestep player projectiles that are on a collision course
    private void DodgeScan()
    {
        if (Time.time < dodgeReadyAt) return;
        Transform projs = GS.FindParent(GS.Parent.allyprojectiles);
        for (int i = 0; i < projs.childCount; i++)
        {
            Transform p = projs.GetChild(i);
            Vector2 toMe = transform.position - p.position;
            float d = toMe.magnitude;
            if (d > 2.8f || d < 0.3f) continue;
            if (!p.TryGetComponent<Rigidbody2D>(out var prb)) continue;
            Vector2 v = prb.linearVelocity;
            if (v.sqrMagnitude < 1f) continue;
            Vector2 vn = v.normalized;
            if (Vector2.Dot(vn, toMe.normalized) < 0.8f) continue;
            Vector2 lateral = toMe - vn * Vector2.Dot(toMe, vn); // offset from the trajectory line
            Vector2 dir = lateral.sqrMagnitude > 0.0004f ? lateral.normalized : new Vector2(-vn.y, vn.x) * GS.PlusMinus();
            dodgeReadyAt = Time.time + 0.7f;
            AS.TryAddForce(dir * (AS.mass * Random.Range(110f, 150f)), false); // ~2.2-3 u/s sidestep
            break;
        }
    }

    // ---------- brain ----------
    private IEnumerator Brain()
    {
        yield return WFAS(1.3f);
        while (ls != null && !ls.hasDied)
        {
            if (cs == null)
            {
                cs = GS.CS();
                yield return WFAS(0.5f);
                continue;
            }
            float dist = Vector2.Distance(cs.position, transform.position);
            if (Time.time > waltzReadyAt && dist < 1.8f)
            {
                yield return StartCoroutine(MirrorWaltz());
            }
            else
            {
                yield return StartCoroutine(PickAttack(dist));
            }
            yield return WFAS(Random.Range(0.8f, 1.4f) * (phase2 ? 0.6f : 1f));
        }
    }

    private IEnumerator PickAttack(float dist)
    {
        // weighted, distance-aware, never the same attack twice in a row
        float[] weights = new float[4]; // volley, lance, hunt, bloom
        weights[0] = 1f;
        weights[1] = dist < 4.5f ? 1f : 0.5f;
        weights[2] = dist > 3f ? 1f : 0.4f;
        weights[3] = dist < 2.6f ? 1.3f : 0.5f;
        if (lastAttack >= 0) weights[lastAttack] *= 0.15f;
        float total = 0f;
        foreach (float w in weights) total += w;
        float roll = Random.Range(0f, total);
        int pick = 0;
        for (int i = 0; i < weights.Length; i++)
        {
            roll -= weights[i];
            if (roll <= 0f) { pick = i; break; }
        }
        lastAttack = pick;
        switch (pick)
        {
            case 0: yield return StartCoroutine(Volley()); break;
            case 1: yield return StartCoroutine(PanelLance()); break;
            case 2: yield return StartCoroutine(RiftHunt()); break;
            default: yield return StartCoroutine(ShardBloom()); break;
        }
    }

    // aim at where the player is going, not where they are
    private Vector2 Lead(float projSpeed)
    {
        Vector2 to = cs.position - transform.position;
        Vector2 vel = GS.AS != null ? (Vector2)GS.AS.rb.linearVelocity : Vector2.zero;
        Vector2 dest = (Vector2)cs.position + vel * Mathf.Min(to.magnitude / projSpeed, 0.8f);
        Vector2 dir = dest - (Vector2)transform.position;
        return dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.up;
    }

    // ---------- attacks ----------
    private IEnumerator Volley()
    {
        panelRadiusTarget = 0.7f;
        panelSpin = 160f;
        SetPanelSprites(panelLit);
        int n = phase2 ? 6 : 4;
        for (int i = 0; i < n; i++)
        {
            if (cs == null) break;
            Vector2 dir = GS.Rotated(Lead(6.5f), 6f, true);
            var p = Instantiate(bolt, transform.position + (Vector3)(dir * 0.35f), Quaternion.identity, GS.FindParent(GS.Parent.enemyprojectiles));
            p.GetComponent<ProjectileScript>().SetValues(dir, tag, ActRateProjectileStrength());
            glowTarget = 1.1f;
            yield return WFAS(phase2 ? 0.16f : 0.24f);
        }
        ResetPanels();
    }

    private IEnumerator PanelLance()
    {
        int throws = phase2 ? 2 : 1;
        for (int k = 0; k < throws; k++)
        {
            int pi = Random.Range(0, panels.Length);
            if (!panels[pi].gameObject.activeSelf) pi = (pi + 1) % panels.Length;
            if (!panels[pi].gameObject.activeSelf) break;
            panelSRs[pi].sprite = panelFlash;
            panelSpin = 240f;
            yield return WFAS(0.45f);
            if (cs == null) break;
            Vector3 from = panels[pi].position;
            panels[pi].gameObject.SetActive(false);
            var p = Instantiate(lance, from, Quaternion.identity, GS.FindParent(GS.Parent.enemyprojectiles));
            p.GetComponent<ProjectileScript>().SetValues(Lead(8f), tag, ActRateProjectileStrength());
            StartCoroutine(ReshowPanel(pi, 0.9f));
            yield return WFAS(0.3f);
        }
        ResetPanels();
    }

    private IEnumerator ReshowPanel(int i, float t)
    {
        yield return WFAS(t);
        if (panels[i] == null) yield break;
        panelSRs[i].sprite = phase2 ? panelLit : panelIdle;
        panels[i].localPosition = Vector3.zero; // re-materialise at the core and swing back out
        panels[i].gameObject.SetActive(true);
    }

    private IEnumerator ShardBloom()
    {
        holding = true;
        panelRadiusTarget = 0.3f;
        panelSpin = 260f;
        SetPanelSprites(panelFlash);
        AS.convertProjectiles = true; // contracted panels mirror incoming fire
        overrideAnim = true;
        float t = 0f;
        const float chargeT = 0.95f;
        while (t < chargeT)
        {
            t += Time.deltaTime * actRate;
            sr.sprite = GS.PercentParameter(chargeSprs, Mathf.Clamp01(t / chargeT));
            glowTarget = 0.6f + t;
            yield return null;
        }
        AS.convertProjectiles = false;
        overrideAnim = false;
        int n = phase2 ? 12 : 8;
        float baseAng = Random.Range(0f, 360f / n);
        for (int i = 0; i < n; i++)
        {
            var p = Instantiate(shard, transform.position, Quaternion.identity, GS.FindParent(GS.Parent.enemyprojectiles));
            p.GetComponent<ProjectileScript>().SetValues(GS.VTheta(baseAng + i * (360f / n)), tag, ActRateProjectileStrength());
        }
        Shockwave.Spawn(transform.position, 1.6f, 0.015f, 0.45f);
        glowTarget = 1.4f;
        holding = false;
        ResetPanels();
        yield return WFAS(0.4f);
    }

    private IEnumerator RiftHunt()
    {
        int n = phase2 ? 3 : 2;
        panelSpin = 130f;
        SetPanelSprites(panelLit);
        for (int i = 0; i < n; i++)
        {
            Transform mount = panels[i % panels.Length];
            Vector3 from = mount.gameObject.activeSelf ? mount.position : transform.position;
            var w = Instantiate(wisp, from, Quaternion.identity, GS.FindParent(GS.Parent.enemyprojectiles));
            var ps = w.GetComponent<ProjectileScript>();
            ps.SetValues(((Vector2)(from - transform.position)).normalized, tag, ActRateProjectileStrength());
            ps.father = transform;
            yield return WFAS(0.3f);
        }
        ResetPanels();
    }

    // immaterial blink through the player to the far side, spitting bolts mid-dash
    private IEnumerator MirrorWaltz()
    {
        waltzReadyAt = Time.time + (phase2 ? 4.5f : 7f);
        dashing = true;
        AS.immaterial = true;
        SetAlpha(0.45f);
        Vector2 through = ((Vector2)(transform.position - cs.position)).normalized;
        Vector2 dest = ClampToRoom((Vector2)cs.position - through * 3f);
        StartCoroutine(AS.OffVelCapForT(0.5f));
        AS.TryAddForce((dest - (Vector2)transform.position).normalized * (AS.mass * 280f), false); // ~5.6 u/s blink
        panelSpin = 420f;
        panelRadiusTarget = 0.25f;
        for (int i = 0; i < 3; i++)
        {
            yield return WFAS(0.09f);
            if (cs == null) continue;
            var p = Instantiate(bolt, transform.position, Quaternion.identity, GS.FindParent(GS.Parent.enemyprojectiles));
            p.GetComponent<ProjectileScript>().SetValues(((Vector2)(cs.position - transform.position)).normalized, tag, ActRateProjectileStrength());
        }
        yield return WFAS(0.25f);
        AS.immaterial = false;
        SetAlpha(1f);
        dashing = false;
        ResetPanels();
    }

    // ---------- phase / death ----------
    private void OnDamaged(float value)
    {
        if (value >= 0f || ls.hasDied || phase2) return;
        if (ls.hp >= 0.5f * ls.maxHp) return;
        phase2 = true;
        GS.Stat(this, "Stim", 15f, 1.2f);
        Shockwave.Spawn(transform.position, 2f, 0.02f, 0.5f);
        float baseAng = Random.Range(0f, 45f);
        for (int i = 0; i < 8; i++)
        {
            var p = Instantiate(shard, transform.position, Quaternion.identity, GS.FindParent(GS.Parent.enemyprojectiles));
            p.GetComponent<ProjectileScript>().SetValues(GS.VTheta(baseAng + i * 45f), tag, ActRateProjectileStrength());
        }
        SetPanelSprites(panelLit);
        waltzReadyAt = 0f; // rage entry refreshes the waltz
    }

    public void OnDeath()
    {
        Shockwave.Spawn(transform.position, 2.2f, 0.02f, 0.6f);
        for (int i = 0; i < panels.Length; i++)
        {
            if (panels[i] == null || !panels[i].gameObject.activeSelf) continue;
            Vector2 dir = (panels[i].position - transform.position).normalized;
            if (dir == Vector2.zero) dir = GS.RandCircleV2(1f, 1f);
            var p = Instantiate(lance, panels[i].position, Quaternion.identity, GS.FindParent(GS.Parent.enemyprojectiles));
            p.GetComponent<ProjectileScript>().SetValues(dir, tag);
        }
    }
}
