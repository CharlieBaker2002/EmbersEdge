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
    [SerializeField] private ParticleSystem ps;
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
    public bool charEmber = false;
    public Extractor extract = null;
    [SerializeField] private ParticleSystem[] trailPS;

    [SerializeField] private ParticleSystemRenderer[] r;
    

    // ──────────────────────────  RUNTIME  ──────────────────────────
    Vector3 spawnPos;
    Vector2 prevPos;                 // for heading
    private Vector2 current;

    // charEmber tracking: bump this to freeze all currently-tracking charEmbers
    public static int trackGen = 0;
    int myTrackGen;
    Vector3 charOffset;

    public static event Action<Vector3, Vector3> OnPortalBurst;
    public static void TriggerPortalBurst(Vector3 charPos, Vector3 returnPos) => OnPortalBurst?.Invoke(charPos, returnPos);

    public static event Action OnPortalEmberBurst;
    public static void TriggerPortalEmberBurst() => OnPortalEmberBurst?.Invoke();

    void Awake()
    {
        if (!sr) sr = GetComponent<SpriteRenderer>();
        if(sr!= null) UpdateColours(GS.era);
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

    void HandlePortalEmberBurst()
    {
        LeanTween.cancel(gameObject);

        Vector3 corePos = EmbersEdge.mainCore != null ? (Vector3)EmbersEdge.mainCore.transform.position : Vector3.zero;
        Vector3 fromCore = transform.position - corePos;
        Vector3 outDir = fromCore.sqrMagnitude > 0.0001f
            ? fromCore.normalized
            : (Vector3)Random.insideUnitCircle.normalized;
        Vector3 burstTarget = corePos + outDir * Random.Range(6f, 10f);
        float returnDelay = Random.Range(0.3f, 1.5f);
        Vector3 returnTarget = corePos + (Vector3)Random.insideUnitCircle * Random.Range(0f, 0.5f);

        LeanTween.move(gameObject, burstTarget, 0.15f).setEase(LeanTweenType.easeOutExpo)
            .setOnComplete(() =>
                LeanTween.delayedCall(gameObject, returnDelay, () =>
                    StartCoroutine(BezierReturnI(returnTarget, 0.5f))));
    }

    IEnumerator BezierReturnI(Vector3 to, float duration)
    {
        Vector2 from2 = transform.position;
        Vector2 to2   = to;
        Vector2 dir   = to2 - from2;
        Vector2 perp  = new Vector2(-dir.y, dir.x).normalized;
        Vector2 ctrl  = (from2 + to2) * 0.5f + perp * Random.Range(-0.5f, 0.5f) * dir.magnitude;
        Vector2[] pts = new Vector2[] { from2, ctrl, to2 };

        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            transform.position = (Vector3)GS.Bez(pts, t);
            yield return null;
        }
        transform.position = to;
        Cease();
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

        LeanTween.cancel(gameObject);
        LeanTween.move(gameObject, burstTarget, outDur).setEase(LeanTweenType.easeOutExpo)
            .setOnComplete(() =>
                LeanTween.move(gameObject, returnPos, returnDur).setEase(LeanTweenType.easeInCubic)
                    .setOnComplete(Cease));
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
        onComplete += SetParticle;
        PlaySequence();
    }

    // ──────────────────────────  MAIN SEQUENCE  ──────────────────────────
    void PlaySequence()
    {
        var em = ps.emission;
        trailPS[0]?.gameObject.SetActive(true);
        trailPS[1]?.gameObject.SetActive(true);
        if (charEmber)
        {
            if (trailPS[0] != null) { var m = trailPS[0].main; m.loop = true; trailPS[0].Play(); }
            if (trailPS[1] != null) { var m = trailPS[1].main; m.loop = true; trailPS[1].Play(); }
        }
        float speed = 1f + Random.Range(-0.3f,0.3f);
        var seq = LeanTween.sequence();
        if (!portalEmber)
        {
            if (!quick)
            {
                seq.insert(LeanTween.value(gameObject, 0f, 30f, loadTime).setOnUpdate(t => em.rateOverTime = t));
                if(!portalEmber && !charEmber) seq.append(sr.LeanAnimate(loadSprites, loadTime));
            }
            else
            {
                LeanTween.value(gameObject, 0f, 30f, loadTime).setOnUpdate(t => em.rateOverTime = t);
                if(!portalEmber && !charEmber) sr.LeanAnimate(loadSprites, loadTime);
            }
            if(!portalEmber && !charEmber) seq.append(() => StartCoroutine(RandomFlicker()));
        }
        Vector3 start = transform.position;
        Vector3 dirSide = ((Vector2)(to - start)).Rotated(90f);
        Vector3 mid1 = Vector3.Lerp(spawnPos, to, 0.35f) + Random.Range(-0.6f,0.6f)*dirSide * arcHeight;
        Vector3 mid2 = Vector3.Lerp(spawnPos, to, 0.7f) + Random.Range(-0.3f,0.3f)*dirSide * arcHeight;
        Vector3[] path;
        if (portalEmber)
        {
            if (Random.Range(0, 2) == 0)
            {
                path = new []{ start,  mid1, mid2, start };
            }
            else
            {
                path = new []{ start,  mid2, mid1, start };
            }
        }
        else
        {
            path = new []{ start, mid1, mid2, to };
        }
        if(!quick)seq.append(LeanTween.delayedCall(0.4f * speed, () => { }));
        if (portalEmber)
        {
            seq.append(LeanTween.move(gameObject, path, flightTime * speed).setEase(flightEase));
        }
        else if (!charEmber)
        {
            seq.append(LeanTween.move(gameObject, path, flightTime * speed).setEase(flightEase).setOnUpdate((Vector3 v) => FaceHeading()));
            seq.append(LeanTween.delayedCall(gameObject,0f, StopAllCoroutines)).insert(LeanTween.value(gameObject, 30f, 0f, offTime).setOnUpdate(t => em.rateOverTime = t));
        }
        if (!portalEmber && !charEmber)
        {
            seq.append(()=> ps2.SetActive(true));
            seq.append(()=>onComplete?.Invoke());
            seq.append(sr.LeanAnimate(offSprites, offTime));
        }
        float destroyDelay = charEmber ? flightTime : 1f;
        seq.append(LeanTween.delayedCall(gameObject, destroyDelay, Cease));
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