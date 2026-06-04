using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Script-driven Pelter turret. Replaces the old Animator + AnimationPasser setup with a
/// single coroutine that drives the child SpriteRenderer (Building.sr) frame-by-frame.
///
/// The 47-frame sheet (frames[0..46], PNG order) is ONE contiguous loop:
///   - FIRE, walking DOWN sprites 1->31 in three shots (Full->NearFull->NearEmpty->Empty),
///     spawning a bullet on the shot's mid frame.
///   - RELOAD, walking UP sprites 31->46 (then back to the Full pose, 0) in three legs,
///     each leg yielding while it draws energy at the grid's DrawRate.
///
/// Energy is the key change: instead of an instant Power.Use() that ignored DrawRate, each
/// reload leg pages energy in over time (step = min(remaining, DrawRate*dt, Energy)). A leg's
/// duration is max(its animation time, the time the grid actually needs to deliver the energy),
/// so a fat supply plays at the authored speed while a starved one stretches the reload.
/// </summary>
public class PelterTurret : Building
{
    private static readonly int Thecolor = Shader.PropertyToID("thecolor");

    [SerializeField] private Finder f;
    [SerializeField] private GameObject[] bullets;
    [SerializeField] private Transform shootPoint;
    [SerializeField] private Transform cannon;
    [SerializeField] private SpriteRenderer anchorSR;

    [Header("Sprite animation (replaces the old Animator/controller)")]
    [Tooltip("All 47 Pelter frames in PNG order (0..46). Fire walks down 1->31, reload walks up 31->46.")]
    [SerializeField] private Sprite[] frames;
    [Tooltip("Seconds per frame while firing. Old authored 1/60 at 0.5x anim speed = 1/30s.")]
    [SerializeField] private float shootFrameTime = 1f / 30f;
    [Tooltip("Seconds per frame while reloading at base speed. Old authored 1/6 at 0.5x = 1/3s.")]
    [SerializeField] private float reloadFrameTime = 1f / 3f;
    [Tooltip("Seconds per reload frame once Fast Refill is bought and the grid can sustain it (old 2.5x burst).")]
    [SerializeField] private float fastReloadFrameTime = 1f / 15f;
    [Tooltip("Energy drawn to reload one round. The final (Full) round costs 3x this once Bigger Bullet is owned.")]
    [SerializeField] private float energyPerBullet = 1f;

    private const int MaxAmmo = 3;

    // Down-sweep: one shot per round, full -> empty. (fromSprite, toSprite, bulletSprite) inclusive.
    private static readonly (int from, int to, int fire)[] Shots =
    {
        (1, 11, 5),    // round 3->2 : Full      -> NearFull  (multi-shot when Bigger Bullet owned)
        (12, 22, 16),  // round 2->1 : NearFull  -> NearEmpty
        (23, 31, 27),  // round 1->0 : NearEmpty -> Empty
    };

    // Up-sweep: one leg per round, empty -> full. (fromSprite, toSprite) inclusive, played contiguously.
    private static readonly (int from, int to)[] ReloadLegs =
    {
        (31, 36),  // round 0->1
        (37, 41),  // round 1->2
        (42, 46),  // round 2->3  (then the Full pose, sprite 0)
    };

    // Resting pose shown per ammo level (index = ammo): Empty=31, NearEmpty=21, NearFull=11, Full=0.
    private static readonly int[] IdleSprite = { 31, 21, 11, 0 };

    private Transform target;
    private Quaternion lookRot;

    private bool fastUpgrade = false;       // "Fast Refill" upgrade
    private bool munitionsUpgrade = false;  // "Bigger Bullet" upgrade

    private int ammo = MaxAmmo;
    private Coroutine animLoop;

    public override void Start()
    {
        base.Start();
        f.OnFound += t => target = t;
        f.OnLost += () => target = null;
        lookRot = GS.VTQ(GetNearestEE(transform) - (Vector2)transform.position);
        AddUpgradeSlot(new int[] { 30, 8, 0, 0 }, "Fast Refill", null, true, () =>
        {
            fastUpgrade = true;
            f.refresh = 0.8f;
            GetComponent<SpriteRenderer>().material = ColourManager.AllyMat(1);
            Color cola = sr.material.GetColor(Thecolor);
            sr.material = ColourManager.AllyMat(1, false, cola);
            anchorSR.material = ColourManager.AllyMat(1);
        }, 3);
        AddUpgradeSlot(new int[] { 75, 0, 2, 0 }, "Bigger Bullet", null, true, () =>
        {
            munitionsUpgrade = true;
            GetComponent<SpriteRenderer>().material = ColourManager.AllyMat(2);
            sr.material = ColourManager.AllyMat(2, false, sr.material.GetColor(Thecolor));
            anchorSR.material = ColourManager.AllyMat(2);
        }, 6, false, null, null, () => fastUpgrade);
        MapManager.OnUpdateMap += () => lookRot = GS.VTQ(GetNearestEE(transform) - (Vector2)transform.position);
    }

    protected override void BEnable()
    {
        f.engaged = true;
        if (Finder.turretsOn)
        {
            f.enabled = true;
        }
        else
        {
            StartCoroutine(TurnOffInASec());
        }
        // Coroutines outlive a disabled MonoBehaviour, so own the loop explicitly (cf. ChargerTurret).
        if (animLoop == null) animLoop = StartCoroutine(AnimLoop());
    }

    protected override void BDisable()
    {
        f.engaged = false;
        if (animLoop != null) { StopCoroutine(animLoop); animLoop = null; }
        ClearEnergyStatusImmediate();   // turret off — hide at once, don't linger
    }

    IEnumerator TurnOffInASec()
    {
        yield return new WaitForSeconds(0.5f);
        if (!Finder.turretsOn)
        {
            f.enabled = false;
        }
    }

    private void Update()
    {
        if (target != null)
        {
            cannon.transform.rotation = Quaternion.RotateTowards(cannon.transform.rotation,
                GS.VTQ(target.position - cannon.transform.position), 270 * Time.deltaTime);
        }
        else
        {
            cannon.transform.rotation = Quaternion.Lerp(cannon.transform.rotation, lookRot, 0.25f * Time.deltaTime);
        }
    }

    // ---- Sprite state machine ---------------------------------------------------------------

    IEnumerator AnimLoop()
    {
        if (frames == null || frames.Length <= 46)
        {
            Debug.LogError($"{name}: PelterTurret needs all 47 frames assigned; got {(frames == null ? 0 : frames.Length)}.");
            yield break;
        }

        ammo = MaxAmmo;
        sr.sprite = frames[IdleSprite[ammo]];

        while (true)
        {
            if (target != null && ammo > 0)
            {
                // Shoot a loaded round (down-sweep 1->31). Always fire what we have.
                yield return FireShot(MaxAmmo - ammo);   // 0,1,2 as we descend full->empty
                ammo--;
                sr.sprite = frames[IdleSprite[ammo]];
            }
            else if (ammo < MaxAmmo)
            {
                // Refill toward full (up-sweep 31->46->0), paying energy per round. Standard fires
                // again after a single round (1:1); Fast Refill batches the whole magazine. Reload
                // decides which and returns early when it's time to shoot what we hold.
                yield return Reload();
            }
            else
            {
                // Full, no target: idle on the Full pose.
                sr.sprite = frames[IdleSprite[MaxAmmo]];
                ClearEnergyStatus();
                yield return null;
            }
        }
    }

    IEnumerator FireShot(int shotIndex)
    {
        var s = Shots[shotIndex];
        for (int sp = s.from; sp <= s.to; sp++)
        {
            sr.sprite = frames[sp];
            if (sp == s.fire)
            {
                if (shotIndex == 0) ShootBig();   // Full->NearFull only carries the multi-shot
                else Shoot();
            }
            yield return Hold(shootFrameTime);
        }
    }

    /// <summary>
    /// Walk back up the strip, one round per leg, paying each round's energy as we go. A round only
    /// loads (ammo++) once its energy is fully drawn, and the sprite tracks the SLOWER of animation
    /// time and energy delivered — so a starved grid shows no reload progress and never hands out a
    /// free round, while an instant supply is still gated to the authored frame timing. If we
    /// already hold a round and a target is waiting while the grid can't pay for the next round
    /// within its normal reload time, we stop reloading and return so AnimLoop goes and shoots it.
    /// </summary>
    IEnumerator Reload()
    {
        while (ammo < MaxAmmo)
        {
            var leg = ReloadLegs[ammo];
            int span = leg.to - leg.from;                              // sprite steps in this leg
            bool finalRound = ammo == MaxAmmo - 1;                     // NearFull->Full leg
            float cost = energyPerBullet * (finalRound && munitionsUpgrade ? 3f : 1f);
            float frameTime = fastUpgrade ? fastReloadFrameTime : reloadFrameTime;
            float legAnimTime = (span + 1) * frameTime;
            float throttleTime = (span + 1) * reloadFrameTime;        // judged vs the default speed

            float drawn = 0f, t = 0f;
            while (drawn < cost - 1e-4f || t < legAnimTime)
            {
                // Full capacity = drawing this round's energy within the reload's CURRENT frame time
                // (faster once Fast Refill is bought). If the grid can't sustain that rate the reload
                // visibly slows, and this reports "insufficient" — e.g. an upgraded Pelter on a single
                // battery. (A standard reload only needs ~0.5/s, so one battery still reads Powered.)
                ReportEnergyDraw(cost / legAnimTime);

                // When to stop reloading and go shoot the round(s) we already hold:
                //  - Standard (no Fast Refill): after EVERY round -> shoot-one / recharge-one.
                //  - Fast Refill: keep going to batch the whole magazine, UNLESS the grid is so
                //    slow that the batch can't be fed in a normal reload's time (then it wins
                //    nothing over 1:1 and just leaves us defenceless, so drop back to 1:1).
                if (ammo > 0 && target != null && (!fastUpgrade || !CanDrawPromptly(cost - drawn, throttleTime)))
                    yield break;

                drawn += DrawStep(cost - drawn);
                t += Time.deltaTime;
                float frac = Mathf.Clamp01(Mathf.Min(drawn / cost, t / legAnimTime));
                sr.sprite = frames[leg.from + Mathf.Min(span, Mathf.FloorToInt(frac * (span + 1)))];
                yield return null;
            }
            sr.sprite = frames[leg.to];
            ammo++;
        }
        sr.sprite = frames[IdleSprite[MaxAmmo]];
        ClearEnergyStatus();   // full again — clear any overlay
    }

    /// <summary>Draws up to <paramref name="remaining"/> this frame, rate-limited by DrawRate. Returns what arrived.</summary>
    float DrawStep(float remaining)
    {
        if (remaining <= 1e-5f) return 0f;
        float step = Mathf.Min(remaining, Power.DrawRate * Time.deltaTime, Power.Energy);
        return (step > 0f && Power.Use(step)) ? step : 0f;
    }

    /// <summary>
    /// True when the grid both holds <paramref name="amount"/> energy and can deliver it within
    /// <paramref name="withinTime"/> at its current draw rate. When false mid-reload (with a round
    /// loaded and a target waiting) the turret stops reloading and goes to shoot instead.
    /// </summary>
    bool CanDrawPromptly(float amount, float withinTime)
    {
        if (amount <= 1e-4f) return true;
        return Power.Energy >= amount - 1e-3f && Power.DrawRate * withinTime >= amount - 1e-3f;
    }

    IEnumerator Hold(float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.deltaTime) yield return null;
    }

    // ---- Bullet spawning (was driven by animation events, now called from FireShot) ----------

    public void ShootBig()
    {
        if (target != null)
        {
            if (munitionsUpgrade)
            {
                for (int i = 0; i < 2; i++)
                {
                    var g = Instantiate(bullets[0], shootPoint.position,
                        GS.VTQ(shootPoint.position - transform.position), GS.FindParent(GS.Parent.allyprojectiles));
                    g.GetComponent<Seeking>().target = target;
                    if (i == 0)
                    {
                        g.GetComponent<Rigidbody2D>().linearVelocity =
                            5f * GS.Rotated(target.transform.position - g.transform.position, 17.5f);
                    }
                    else
                    {
                        g.GetComponent<Rigidbody2D>().linearVelocity =
                            5f * GS.Rotated(target.transform.position - g.transform.position, -17.5f);
                    }
                }

                var b = Instantiate(bullets[1], shootPoint.position, GS.VTQ(shootPoint.position - transform.position),
                    GS.FindParent(GS.Parent.allyprojectiles));
                b.GetComponent<Seeking>().target = target;
                b.GetComponent<Rigidbody2D>().linearVelocity =
                    6f * (target.transform.position - b.transform.position);
            }
            else
            {
                var g = Instantiate(bullets[0], shootPoint.position,
                    GS.VTQ(shootPoint.position - transform.position), GS.FindParent(GS.Parent.allyprojectiles));
                g.GetComponent<Seeking>().target = target;
                g.GetComponent<Rigidbody2D>().linearVelocity =
                    5f * (target.transform.position - g.transform.position);
            }
        }
    }

    public void Shoot()
    {
        if (target != null)
        {
            var g = Instantiate(bullets[0], shootPoint.position,
                GS.VTQ(shootPoint.position - transform.position), GS.FindParent(GS.Parent.allyprojectiles));
            g.GetComponent<Seeking>().target = target;
            g.GetComponent<Rigidbody2D>().linearVelocity =
                5f * (target.transform.position - g.transform.position);
        }
    }
}
