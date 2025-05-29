using System;
using System.Collections;
using UnityEngine;
using Random = UnityEngine.Random;

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
    public Extractor extract = null;

    // ──────────────────────────  RUNTIME  ──────────────────────────
    Vector3 spawnPos;
    Vector2 prevPos;                 // for heading
    private Vector2 current;
    void Awake()
    {
        if (!sr) sr = GetComponent<SpriteRenderer>();
        spawnPos = transform.position;
        prevPos  = spawnPos;
      
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
        float speed = 1f + Random.Range(-0.3f,0.3f);
        var seq = LeanTween.sequence();
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
        Vector3 start = transform.position;
        Vector3 dirSide = ((Vector2)(to - start)).Rotated(90f);
        Vector3 mid1 = Vector3.Lerp(spawnPos, to, 0.35f) + Random.Range(-0.6f,0.6f)*dirSide * arcHeight;
        Vector3 mid2 = Vector3.Lerp(spawnPos, to, 0.7f) + Random.Range(-0.3f,0.3f)*dirSide * arcHeight;
        Vector3[] path = { start, mid1, mid2, to };
        if(!quick)seq.append(LeanTween.delayedCall(0.4f * speed, () => { }));
        seq.append(LeanTween.move(gameObject, path, flightTime * speed).setEase(flightEase).setOnUpdate((Vector3 v) => FaceHeading()));
        seq.append(LeanTween.delayedCall(gameObject,0f, StopAllCoroutines)).insert(LeanTween.value(gameObject, 30f, 0f, offTime).setOnUpdate(t => em.rateOverTime = t));
        seq.append(()=> ps2.SetActive(true)); 
        seq.append(()=>onComplete?.Invoke());
        seq.append(sr.LeanAnimate(offSprites, offTime));
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
}