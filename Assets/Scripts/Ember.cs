using System;
using System.Collections;
using System.Numerics;
using UnityEngine;
using Quaternion = UnityEngine.Quaternion;
using Random = UnityEngine.Random;
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;

public class Ember : MonoBehaviour
{
    // ──────────────────────────  CONFIG  ──────────────────────────
    [Header("Sprites")]
    [SerializeField] private SpriteRenderer sr;
    [SerializeField] private Sprite[] loadSprites;   // charge-up frames
    [SerializeField] private Sprite[] onSprites;     // random flicker frames
    [SerializeField] private Sprite[] offSprites;    // power-down frames
    [SerializeField] public ParticleSystem ps;
    [SerializeField] private GameObject ps2;

    [Header("Flight")]
    [SerializeField] public Vector3 to;          // world destination
    [SerializeField] private float arcHeight = 1; // vertical lift of the arc
    [SerializeField] public float flightTime = 0.75f;
    [SerializeField] private LeanTweenType flightEase = LeanTweenType.easeInOutCubic;

    [Header("Timings")]
    [SerializeField] private float loadTime = 0.4f;

    [SerializeField] private float offTime  = 0.35f;

    [Header("Flicker-phase (between load and off)")]
    [SerializeField] private float flickerInterval = 0.05f;  // seconds between random sprite swaps

    public Action onComplete;

    [SerializeField] bool quick = false;
    public bool portalEmber = false;
    public bool reversePortalEmber = false;
    public bool charEmber = false;
    public Expander extract = null;
    [SerializeField] public ParticleSystem[] trailPS;

    [SerializeField] private ParticleSystemRenderer[] r;
    

    // ──────────────────────────  RUNTIME  ──────────────────────────
    Vector3 spawnPos;
    Vector2 prevPos;                 // for heading
    private Vector2 current;
    Coroutine flickerCo;             // the random-flicker loop, stopped when the flight ends

    // charEmber tracking: bump this to freeze all currently-tracking charEmbers
    public static int trackGen = 0;
    int myTrackGen;
    Vector3 charOffset;

    public static event Action<Vector3, Vector3> OnPortalBurst;
    static bool portalBurstShockwaveDone;   // one rematerialise shockwave per burst, not one per ember
    public static void TriggerPortalBurst(Vector3 charPos, Vector3 returnPos)
    {
        portalBurstShockwaveDone = false;
        OnPortalBurst?.Invoke(charPos, returnPos);
    }

    public static event Action OnPortalEmberBurst;
    const int MaxBurstFunnel = 10;       // at most this many embers funnel into the core per burst
    static int portalEmberBurstCount;    // reset each burst; embers past the cap just fade instead
    public static void TriggerPortalEmberBurst()
    {
        portalEmberBurstCount = 0;
        OnPortalEmberBurst?.Invoke();
    }

    void Awake()
    {
        if (!sr) sr = GetComponent<SpriteRenderer>();
        spawnPos = transform.position;
        prevPos  = spawnPos;
        myTrackGen = trackGen;
        if (charEmber)
        {
            Transform charT = GS.CS();
            if (charT != null) charOffset = spawnPos - charT.position;
            OnPortalBurst += HandlePortalBurst;
        }
        if (portalEmber) OnPortalEmberBurst += HandlePortalEmberBurst;
    }

    void OnDestroy()
    {
        if (charEmber)   OnPortalBurst      -= HandlePortalBurst;
        if (portalEmber) OnPortalEmberBurst -= HandlePortalEmberBurst;
    }

    // Funnel into the main core, mirroring the initial to-dungeon burst but aimed at the core instead of
    // (0,0): fling outward to a ring around the core first, THEN branch back in along a spline. The
    // burst-out gives BranchPath a consistent inward distance/direction, so it fans cleanly instead of
    // thrashing back and forth when the ember happens to start right on top of the core.
    void HandlePortalEmberBurst()
    {
        // Cap how many embers funnel into the core; the rest just finish their ambient drift and fade.
        if (++portalEmberBurstCount > MaxBurstFunnel) return;

        LeanTween.cancel(gameObject);
        StopAllCoroutines();            // hand off from PlaySequenceI; it would otherwise still time out into Cease

        Vector3 corePos = EmbersEdge.mainCore != null ? (Vector3)EmbersEdge.mainCore.transform.position : Vector3.zero;
        Vector3 fromCore = transform.position - corePos;
        Vector3 outDir = fromCore.sqrMagnitude > 0.0001f ? fromCore.normalized : (Vector3)Random.insideUnitCircle.normalized;
        Vector3 burstTarget = corePos + outDir * Random.Range(4f, 7f);
        Vector3 target = corePos + (Vector3)Random.insideUnitCircle * Random.Range(0f, 0.5f);

        LeanTween.move(gameObject, burstTarget, 0.15f).setEase(LeanTweenType.easeOutExpo)
            .setOnComplete(() =>
                LeanTween.move(gameObject, BranchPath(burstTarget, target), Random.Range(0.55f, 0.9f))
                    .setEase(flightEase).setOnComplete(Cease));
    }

    void HandlePortalBurst(Vector3 charPos, Vector3 returnPos)
    {
        if (myTrackGen != trackGen) return;
        myTrackGen = -1;

        Vector3 outDir;
        if (returnPos.sqrMagnitude < 0.0001f)
        {
            // Going to base: burst behind the player away from origin, with a narrow spread
            Vector3 away = charPos.sqrMagnitude > 0.0001f ? charPos.normalized : Vector3.right;
            Vector3 perp = new Vector3(-away.y, away.x, 0f);
            outDir = (away + perp * Random.Range(-0.5f, 0.5f)).normalized;
        }
        else
        {
            outDir = transform.position - charPos;
            if (outDir.sqrMagnitude < 0.001f) outDir = (Vector3)Random.insideUnitCircle.normalized;
            outDir = outDir.normalized;
        }

        bool goingHome = returnPos.sqrMagnitude > 0.0001f;
        Vector3 burstTarget = charPos + outDir * Random.Range(3f, 5f);
        float outDur = goingHome ? 0.3f : 0.2f;
        float returnDur = goingHome ? Random.Range(0.7f, 1.1f) : 0.35f;

        Vector3 snapTo = returnPos;
        LeanTween.cancel(gameObject);
        StopAllCoroutines();            // hand off from PlaySequenceI; it would otherwise still time out into Cease
        LeanTween.move(gameObject, burstTarget, outDur).setEase(LeanTweenType.easeOutExpo)
            .setOnComplete(() =>
            {
                System.Action arrive = () =>
                {
                    transform.position = snapTo;
                    // Rematerialise punch — only the first ember to land fires it, so it's one wave, not one per ember.
                    if (!PortalScript.goingHomeNow && !portalBurstShockwaveDone)
                    {
                        portalBurstShockwaveDone = true;
                        Shockwave.Spawn(Vector2.zero, 15f, 0.025f, 2f);
                    }
                    Cease();
                };
                if (goingHome)
                {
                    // Return-to-base: unchanged straight punch back in.
                    LeanTween.move(gameObject, snapTo, returnDur).setEase(LeanTweenType.easeInCubic)
                        .setOnComplete(arrive);
                }
                else
                {
                    // To-dungeon: branch out via a curved spline so the embers fan through various
                    // places before converging on the portal at the base.
                    LeanTween.move(gameObject, BranchPath(burstTarget, snapTo), Random.Range(0.45f, 0.75f))
                        .setEase(flightEase).setOnComplete(arrive);
                }
            });
    }

    // A wandering cousin of FlySpline: the embers bow out to one side through a couple of "branch"
    // waypoints before converging on `target`, so they fan out across various places. Each path picks a
    // single side (so it reads as one clean arc, not a sine wave), bows hardest early and tapers to ~zero
    // at `target` (so it branches early and straightens into the destination). The bow scales with
    // distance but is clamped so it reads the same near or far. Returned as a continuous cubic-bezier
    // path — LeanTween.move needs the points in sets of four (anchor, control, control, anchor), so we
    // Catmull-Rom-fit controls through the waypoints to keep the curve smooth and the length /4.
    Vector3[] BranchPath(Vector3 from, Vector3 target)
    {
        Vector2 d = (Vector2)(target - from);
        Vector3 perp = (Vector3)(d.Rotated(90f).normalized);
        float bow = Mathf.Min(d.magnitude * 0.5f, 6f) * arcHeight;
        float side = Random.value < 0.5f ? -1f : 1f;       // one side per ember -> a clean arc, not a sine wave

        int branches = Random.Range(2, 4);                 // 2–3 mid waypoints = a gentle bend
        int n = branches + 2;
        Vector3[] w = new Vector3[n];                       // waypoints the ember actually weaves through
        w[0] = from;
        for (int i = 1; i <= branches; i++)
        {
            float f = i / (float)(branches + 1);           // evenly spaced along the line
            float taper = 1f - f;                          // hardest bow early, ~0 into the target
            w[i] = Vector3.Lerp(from, target, f) + perp * (side * bow * taper * Random.Range(0.6f, 1f));
        }
        w[n - 1] = target;

        int segs = n - 1;
        Vector3[] pts = new Vector3[segs * 4];             // 4 points per segment (LeanTween bezier)
        for (int i = 0; i < segs; i++)
        {
            Vector3 p0 = w[Mathf.Max(i - 1, 0)];
            Vector3 p1 = w[i];
            Vector3 p2 = w[i + 1];
            Vector3 p3 = w[Mathf.Min(i + 2, n - 1)];
            pts[i * 4 + 0] = p1;                           // anchor (segment start)
            pts[i * 4 + 1] = p1 + (p2 - p0) / 6f;          // Catmull-Rom -> bezier control
            pts[i * 4 + 2] = p2 - (p3 - p1) / 6f;          // Catmull-Rom -> bezier control
            pts[i * 4 + 3] = p2;                           // anchor (segment end; shared with next)
        }
        return pts;
    }

    void Update()
    {
        if (charEmber && myTrackGen == trackGen)
        {
            Transform charT = GS.CS();
            if (charT == null) return;
            Vector3 charPos = charT.position;
            if (float.IsNaN(charPos.x) || float.IsNaN(charPos.y) || float.IsNaN(charPos.z)) return;
            to = charPos;
            transform.position = charPos + charOffset;
        }
    }
    
    void UpdateColours(int era)
    {
        Material mat = GS.MatByEra(era, true, false, true);
        if (!portalEmber)
        {
            sr.material = mat;
        }
        if (ps) r[0].material = mat;
        if (ps2) r[1].material = mat;
        if(trailPS[0]) {r[2].material = mat; r[2].trailMaterial = mat;}
        if (trailPS[1]) {r[3].material = mat; r[3].trailMaterial = mat;}
    }

    void SetParticle()
    {
        if (extract != null)
        {
            extract.SetParticle(transform.position, transform.up * 0.02f);
        }
    }

    void Start()
    {
        if(sr!= null) UpdateColours(GS.era);
        onComplete += SetParticle;
        PlaySequence();
    }

    // ──────────────────────────  MAIN SEQUENCE  ──────────────────────────
    // Driven by a single coroutine rather than a LeanTween.sequence(). The old sequence allocated ~12
    // tween slots per ember (a master + value-tweens for the emission ramps + value-tweens stepping the
    // sprite sheets + several delayedCalls), all held for the ember's whole life. With embers emitted
    // continuously and ~100 flung at once on a teleport, that spiked LeanTween's pool to exhaustion
    // ("out of spaces"), after which new tweens silently failed and embers froze mid-animation. Only the
    // bezier flight genuinely needs the tween engine, so that stays a LeanTween.move; the emission ramps,
    // sprite frames and timing are plain coroutine work now. Same timeline + visuals, ~1 tween per ember.
    void PlaySequence()
    {
        trailPS[0]?.gameObject.SetActive(true);
        trailPS[1]?.gameObject.SetActive(true);

        if (reversePortalEmber)
        {
            FlySpline(spawnPos, to, flightTime);
            return;
        }

        if (charEmber)
        {
            if (trailPS[0] != null) { var m = trailPS[0].main; m.loop = true; trailPS[0].Play(); }
            if (trailPS[1] != null) { var m = trailPS[1].main; m.loop = true; trailPS[1].Play(); }
        }

        StartCoroutine(PlaySequenceI());
    }

    IEnumerator PlaySequenceI()
    {
        // portalEmber has no main `ps` (it uses ps2 + trailPS), so guard the deref — em is only touched
        // on the !portalEmber paths below.
        var em = ps ? ps.emission : default;
        float speed = 1f + Random.Range(-0.3f, 0.3f);
        bool normal = !portalEmber && !charEmber;

        // Emission ramps up while the rest plays out — the old code used seq.insert here, i.e. it
        // overlapped and never gated the timeline, so we fire-and-forget it the same way.
        if (!portalEmber && ps) StartCoroutine(RampEmission(em, 0f, 30f, loadTime));

        // Load frames + flicker, normal embers only. quick embers overlap the load with the flight (no
        // await, no pre-roll); the others let the load play out first, exactly as the sequence did.
        if (normal)
        {
            if (quick) StartCoroutine(GS.Animate(sr, loadSprites, loadTime, false));
            else       yield return StartCoroutine(GS.Animate(sr, loadSprites, loadTime, false));
            flickerCo = StartCoroutine(RandomFlicker());
        }

        Vector3 start = transform.position;
        Vector3 dirSide = ((Vector2)(to - start)).Rotated(90f);
        Vector3 mid1 = Vector3.Lerp(spawnPos, to, 0.35f) + Random.Range(-0.6f, 0.6f) * dirSide * arcHeight;
        Vector3 mid2 = Vector3.Lerp(spawnPos, to, 0.7f) + Random.Range(-0.3f, 0.3f) * dirSide * arcHeight;
        Vector3[] path;
        if (portalEmber)
            path = Random.Range(0, 2) == 0 ? new[] { start, mid1, mid2, start }
                                           : new[] { start, mid2, mid1, start };
        else
            path = new[] { start, mid1, mid2, to };

        if (!quick) yield return new WaitForSeconds(0.4f * speed);

        if (portalEmber)
        {
            LeanTween.move(gameObject, path, flightTime * speed).setEase(flightEase);
            yield return new WaitForSeconds(flightTime * speed);
        }
        else if (!charEmber)
        {
            LeanTween.move(gameObject, path, flightTime * speed).setEase(flightEase)
                .setOnUpdate((Vector3 v) => FaceHeading());
            yield return new WaitForSeconds(flightTime * speed);

            // Flight done: stop the flicker, ramp emission back to zero, hand off to ps2 + onComplete,
            // then play the power-down frames.
            if (flickerCo != null) { StopCoroutine(flickerCo); flickerCo = null; }
            if (ps) StartCoroutine(RampEmission(em, 30f, 0f, offTime));
            ps2.SetActive(true);
            onComplete?.Invoke();
            yield return StartCoroutine(GS.Animate(sr, offSprites, offTime, false));
        }
        // charEmber has no flight tween — it rides the character in Update and just times out below.

        float destroyDelay = charEmber ? flightTime : 1f;
        yield return new WaitForSeconds(destroyDelay);
        Cease();
    }

    // Drive a particle system's emission rate over time without a tween (was a LeanTween.value).
    IEnumerator RampEmission(ParticleSystem.EmissionModule em, float from, float to, float dur)
    {
        if (dur <= 0f) { em.rateOverTime = to; yield break; }
        for (float x = 0f; x < dur; x += Time.deltaTime)
        {
            em.rateOverTime = Mathf.Lerp(from, to, x / dur);
            yield return null;
        }
        em.rateOverTime = to;
    }

    // The neat 4-point spline flight shared by the return-home embers (main core -> origin) and the
    // to-dungeon funnel (origin -> main core): a LeanTween path from `from` to `target` with two
    // control points bowed perpendicular to the line (scaled by arcHeight) so the ember curves in
    // smoothly instead of darting straight.
    void FlySpline(Vector3 from, Vector3 target, float time)
    {
        Vector3 side = ((Vector2)(target - from)).Rotated(90f);
        Vector3 m1 = Vector3.Lerp(from, target, 0.35f) + side * (Random.Range(-0.6f, 0.6f) * arcHeight);
        Vector3 m2 = Vector3.Lerp(from, target, 0.7f)  + side * (Random.Range(-0.3f, 0.3f) * arcHeight);
        LeanTween.move(gameObject, new[] { from, m1, m2, target }, time).setEase(flightEase).setOnComplete(Cease);
    }
    // ──────────────────────────  CEASE / DESTROY  ──────────────────────────
    public void Cease()
    {
        if (charEmber)   OnPortalBurst      -= HandlePortalBurst;
        if (portalEmber) OnPortalEmberBurst -= HandlePortalEmberBurst;
        myTrackGen = -1;                // freeze any charEmber tracking
        LeanTween.cancel(gameObject);
        StopAllCoroutines();

        // Stop emitting — existing particles live out their lifetimes
        if (ps != null)       ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        if (trailPS[0] != null) trailPS[0].Stop(true, ParticleSystemStopBehavior.StopEmitting);
        if (trailPS[1] != null) trailPS[1].Stop(true, ParticleSystemStopBehavior.StopEmitting);

        // Fade sprite out
        if (sr != null)
        {
            float startAlpha = sr.color.a;
            LeanTween.value(gameObject, startAlpha, 0f, 0.35f)
                .setOnUpdate(a => { if (sr) { Color c = sr.color; c.a = a; sr.color = c; } });
        }

        // Destroy after the longest particle can reasonably finish
        float grace = ps != null ? ps.main.startLifetime.constantMax : 2f;
        Destroy(gameObject, Mathf.Max(grace, 0.5f));
    }

    // ──────────────────────────  HELPERS  ──────────────────────────
    IEnumerator RandomFlicker()
    {
        while (true)
        {
            sr.sprite = onSprites[Random.Range(0, onSprites.Length)];
            yield return new WaitForSeconds(flickerInterval);
        }
    }

    void FaceHeading()
    {
        current = transform.position;
        var dir = current - prevPos;
        if (!(dir.sqrMagnitude > 0.0000001f)) return;
        float z = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0, 0, z - 90);
        prevPos = current;
    }

    public void AdjustTrail(float magnitude)
    {
        if(magnitude<2f) magnitude = 2f;
        //var em = trailPS[0].emission;
        var vol = trailPS[0].velocityOverLifetime;
        vol.orbitalXMultiplier /= 2f;
        vol.orbitalZMultiplier /= magnitude;
        //em = trailPS[1].emission;
        vol = trailPS[1].velocityOverLifetime;
        vol.orbitalXMultiplier /= 2f;
        vol.orbitalZMultiplier /= magnitude;
    }
}