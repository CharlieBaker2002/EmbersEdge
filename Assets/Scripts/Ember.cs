using System;
using System.Collections;
using System.Numerics;
using UnityEngine;
using Quaternion = UnityEngine.Quaternion;
using Random = UnityEngine.Random;
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;

[RequireComponent(typeof(SpriteRenderer))]
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
    [SerializeField] private float flightTime = 0.75f;
    [SerializeField] private LeanTweenType flightEase = LeanTweenType.easeInOutCubic;

    [Header("Timings")]
    [SerializeField] private float loadTime = 0.4f;

    [SerializeField] private float offTime  = 0.35f;

    [Header("Flicker-phase (between load and off)")]
    [SerializeField] private float flickerInterval = 0.05f;  // seconds between random sprite swaps

    public Action onComplete;

    [SerializeField] bool quick = false;
    public bool portalEmber = false;
    public Extractor extract = null;
    [SerializeField] private ParticleSystem[] trailPS;

    [SerializeField] private ParticleSystemRenderer[] r;
    

    // ──────────────────────────  RUNTIME  ──────────────────────────
    Vector3 spawnPos;
    Vector2 prevPos;                 // for heading
    private Vector2 current;
    void Awake()
    {
        if (!sr) sr = GetComponent<SpriteRenderer>();
        UpdateColours(GS.era);
        spawnPos = transform.position;
        prevPos  = spawnPos;
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
        float speed = 1f + Random.Range(-0.3f,0.3f);
        var seq = LeanTween.sequence();
        if (!portalEmber)
        {
            if (!quick)
            {
                seq.insert(LeanTween.value(gameObject, 0f, 30f, loadTime).setOnUpdate(t => em.rateOverTime = t));
                seq.append(sr.LeanAnimate(loadSprites, loadTime));
            }
            else
            {
                LeanTween.value(gameObject, 0f, 30f, loadTime).setOnUpdate(t => em.rateOverTime = t);
                sr.LeanAnimate(loadSprites, loadTime);
            }
            seq.append(() => StartCoroutine(RandomFlicker()));
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
        else
        {
            seq.append(LeanTween.move(gameObject, path, flightTime * speed).setEase(flightEase).setOnUpdate((Vector3 v) => FaceHeading()));
            seq.append(LeanTween.delayedCall(gameObject,0f, StopAllCoroutines)).insert(LeanTween.value(gameObject, 30f, 0f, offTime).setOnUpdate(t => em.rateOverTime = t));
        }
        if (!portalEmber)
        {
            seq.append(()=> ps2.SetActive(true));
            seq.append(()=>onComplete?.Invoke());
            seq.append(sr.LeanAnimate(offSprites, offTime));
        }
        seq.append(LeanTween.delayedCall(gameObject, 1f, () =>
        {
            Destroy(gameObject);
        }));
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