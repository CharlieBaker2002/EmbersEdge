using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A spawn zone seeded into the dungeon rock by MineDungeonManager. It renews EVERY DAY and is armed
/// by TELEPORTING IN: if the excavation is already within its authored activation range when the
/// player arrives (or when a new day rolls mid-dive), it fires its day's wave-grid 0–30s later — each
/// grid ROW is one wave, spawned over timePerWave with interWaveWait between rows — materialising the
/// enemies on wall-hugging cells of the nearby excavation. Digging INTO range mid-visit does NOT
/// trigger it; that waits for the next day. Teleporting out before an armed plan finishes lands the
/// remainder INSTANTLY (the dungeon dimension then freezes, so they're waiting on the next dive).
/// Its body is the authored PNG (16 px = 1 world unit) replacing the walls it sits on, revealed by
/// the fog like everything else.
/// </summary>
public class MineSpawner : MonoBehaviour, IOnDeath
{
    const float WAKE_MAX = 30f;       // armed plans fire 0..this many seconds after arrival
    const float ACTIVE_POLL = 0.5f;   // seconds between excavation-distance checks

    MineSpawnerTemplate template;
    SpriteRenderer sr;
    bool ranToday;
    bool armed;          // in range at arrival/day-roll — today's plan WILL fire this visit
    float wake;          // seconds until the armed plan fires
    float pollT;
    bool nearDig;        // cached "excavation within activationRange"
    Coroutine running;
    // Today's not-yet-materialised enemies while a run is armed/underway — whatever is left here when
    // the player teleports out is flushed instantly.
    readonly List<MarauderSO> pending = new List<MarauderSO>();

    public void Init(MineSpawnerTemplate t)
    {
        template = t;
        BuildVisual();
        BuildLife();
        if (SpawnManager.instance != null) SpawnManager.instance.OnNewDay += OnNewDay;
        RollDay();
    }

    void OnDestroy()
    {
        if (SpawnManager.instance != null) SpawnManager.instance.OnNewDay -= OnNewDay;
    }

    // ----- destruction: EITHER mine out one footprint cell OR just kill it like a regular enemy -----
    bool minedOut;

    // HP = its spawner-budget point cost, scaled up per dimension (deeper = tankier): d1 x1, d2 x3, d3 x8.
    static float HpMult(int era) => era <= 0 ? 1f : era == 1 ? 3f : 8f;

    void BuildLife()
    {
        gameObject.tag = "Enemies";
        gameObject.layer = LayerMask.NameToLayer("Enemy Units");

        var rb = gameObject.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;

        Vector2 size = template.WorldSize;
        var col = gameObject.AddComponent<BoxCollider2D>();
        col.size = size.x > 0f && size.y > 0f ? size : Vector2.one;

        float hp = Mathf.Max(1f, MineAuthoringSO.SpawnerPoints(template) * HpMult(GS.era));
        var life = gameObject.AddComponent<LifeScript>();
        life.maxHp = hp;
        life.hp = hp;
        if (life.onDeaths == null) life.onDeaths = new List<MonoBehaviour>();   // runtime-added LifeScript: list isn't serializer-initialised
        life.onDeaths.Add(this);
    }

    /// <summary>IOnDeath: LifeScript killed it (shot/meleed down like a regular enemy).</summary>
    public void OnDeath() => MinedOut();

    /// <summary>Called as soon as it dies EITHER way — any one footprint cell mined out, or its HP hits
    /// 0 — it's destroyed, dropping whatever's left in its plan.</summary>
    public void MinedOut()
    {
        if (minedOut) return;   // both death paths (mining, LifeScript) route through here
        minedOut = true;
        if (running != null) StopCoroutine(running);
        pending.Clear();
        Destroy(gameObject);
    }

    void OnNewDay() => RollDay();

    // A new day: the spawner renews — clear any leftover run. It stays dormant until armed by a
    // teleport-in; if the day rolls while the player is ALREADY in the dungeon, arm right away.
    void RollDay()
    {
        ranToday = false;
        armed = false;
        if (running != null) { StopCoroutine(running); running = null; }
        pending.Clear();
        if (PortalScript.i != null && PortalScript.i.inDungeon) OnEnterDungeon();
    }

    /// <summary>Teleported into the dungeon (or a new day rolled mid-dive): if the dig is ALREADY
    /// within range, today's plan fires at a random 0–30s. Digging into range later does not count.</summary>
    public void OnEnterDungeon()
    {
        if (ranToday || armed || running != null || template == null) return;
        if (template.PlanForDay(SpawnManager.daySinceNewEra + 1) == null) { ranToday = true; return; }
        if (MineField.i == null ||
            !MineField.i.AnyExcavatedWithin(transform.position, template.activationRange)) return;
        armed = true;
        wake = Random.Range(0f, WAKE_MAX);
    }

    /// <summary>Teleported back to base: dungeon time freezes, so an armed-but-unfired or mid-run plan
    /// lands its remaining enemies INSTANTLY — they're waiting (frozen) when the player returns.</summary>
    public void OnLeaveDungeon()
    {
        if (running != null) { StopCoroutine(running); running = null; }
        else if (armed && !ranToday)
        {
            var plan = template != null ? template.PlanForDay(SpawnManager.daySinceNewEra + 1) : null;
            if (plan != null)
            {
                ranToday = true;
                for (int row = 0; row < plan.rows; row++) pending.AddRange(plan.WaveEnemies(row));
            }
        }
        armed = false;
        foreach (var so in pending) SpawnOne(so);
        pending.Clear();
    }

    void BuildVisual()
    {
        sr = gameObject.AddComponent<SpriteRenderer>();
        if (template.image != null)
        {
            // World size derives from the PNG: 16×16 px = 1×1 units, 32×32 = 2×2 …
            sr.sprite = Sprite.Create(template.image,
                new Rect(0f, 0f, template.image.width, template.image.height),
                new Vector2(0.5f, 0.5f), MineSpawnerTemplate.PixelsPerUnit);
        }
        // Above the rock (0) / ore glow (1), below the fog (5) — the fog reveals it like the walls.
        sr.sortingOrder = 3;
    }

    void Update()
    {
        // cheap cadence on the excavation-distance test (it scans cells)
        pollT -= Time.deltaTime;
        if (pollT <= 0f)
        {
            pollT = ACTIVE_POLL;
            nearDig = MineField.i != null &&
                      MineField.i.AnyExcavatedWithin(transform.position, template.activationRange);
        }

        // subtle breathing while the dig is in range, so a live spawner reads as live
        if (sr != null)
        {
            float a = nearDig ? 0.85f + 0.15f * Mathf.Sin(Time.time * 2.5f) : 1f;
            sr.color = new Color(1f, 1f, 1f, a);
        }

        if (ranToday || running != null || !armed) return;
        wake -= Time.deltaTime;
        if (wake > 0f) return;

        var plan = template.PlanForDay(SpawnManager.daySinceNewEra + 1);
        if (plan == null) { ranToday = true; armed = false; return; }   // nothing authored for today
        running = StartCoroutine(RunDay(plan));
    }

    IEnumerator RunDay(SpawnerDay plan)
    {
        ranToday = true;
        armed = false;
        // the WHOLE day's roster goes into pending up front, so an early teleport-out can flush the rest
        pending.Clear();
        for (int row = 0; row < plan.rows; row++) pending.AddRange(plan.WaveEnemies(row));
        for (int row = 0; row < plan.rows; row++)   // one ROW = one wave, top row first
        {
            List<MarauderSO> wave = plan.WaveEnemies(row);
            if (wave.Count == 0) continue;
            float gap = plan.timePerWave > 0f ? plan.timePerWave / wave.Count : 0f;
            foreach (var so in wave)
            {
                SpawnOne(so);
                pending.Remove(so);
                if (gap > 0f) yield return new WaitForSeconds(gap);
            }
            if (plan.interWaveWait > 0f) yield return new WaitForSeconds(plan.interWaveWait);
        }
        running = null;
    }

    // Materialise one enemy on a random wall-hugging floor cell of the in-range dig (ember flash
    // included), with a fading hint line back to the spawner so the direction of attack reads.
    void SpawnOne(MarauderSO so)
    {
        if (so == null || so.prefab == null || MineField.i == null) return;
        Vector2? at = MineField.i.RandomExcavatedWallHugNear(transform.position, template.activationRange);
        if (at == null) return;
        MineFX.EnemySpawnFlash(at.Value);
        MineFX.HintLine(at.Value, transform.position);
        var g = Instantiate(so.prefab, at.Value, Quaternion.identity, GS.FindParent(GS.Parent.enemies));
        foreach (ILA ila in g.GetComponentsInChildren<ILA>()) ila.UpdateCoef(1f);
    }
}
