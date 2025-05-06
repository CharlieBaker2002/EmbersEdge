using System.Collections;
using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class Ember : MonoBehaviour
{
    // ──────────────────────────  CONFIG  ──────────────────────────
    [Header("Sprites")]
    [SerializeField] private SpriteRenderer sr;
    [SerializeField] private Sprite[] loadSprites;   // charge-up frames
    [SerializeField] private Sprite[] onSprites;     // random flicker frames
    [SerializeField] private Sprite[] offSprites;    // power-down frames

    [Header("Flight")]
    [SerializeField] private Vector3 to;          // world destination
    [SerializeField] private float arcHeight = 1; // vertical lift of the arc
    [SerializeField] private float flightTime = 0.75f;
    [SerializeField] private LeanTweenType flightEase = LeanTweenType.easeInOutCubic;

    [Header("Timings")]
    [SerializeField] private float loadTime = 0.4f;
    [SerializeField] private float loadFPS  = 18f;
    [SerializeField] private LeanTweenType loadEase = LeanTweenType.easeOutQuart;

    [SerializeField] private float offTime  = 0.35f;
    [SerializeField] private float offFPS   = 18f;
    [SerializeField] private LeanTweenType offEase  = LeanTweenType.easeInQuart;

    [Header("Flicker-phase (between load and off)")]
    [SerializeField] private float flickerInterval = 0.05f;  // seconds between random sprite swaps
    [SerializeField] private float flickerDuration = 0.4f;   // how long to stay in the "on" state before powering down

    // ──────────────────────────  RUNTIME  ──────────────────────────
    Vector3 spawnPos;
    Vector3 prevPos;                 // for heading
    Coroutine flickerRoutine;

    void Awake()
    {
        if (!sr) sr = GetComponent<SpriteRenderer>();
        spawnPos = transform.position;
        prevPos  = spawnPos;
    }

    void Start() => PlaySequence();

    // ──────────────────────────  MAIN SEQUENCE  ──────────────────────────
    void PlaySequence()
    {
        var seq = LeanTween.sequence();
        
        // — 1. Charge-up animation ————————————————————————————
        seq.append(LeanTween.delayedCall(gameObject, loadTime, () => sr.LeanAnimateFPS(loadSprites, (int)loadFPS)).setEase(loadEase));

        // — 2. Random flicker ("on" state) ——————————————————————
        seq.append(() =>
            seq.append(LeanTween.delayedCall(gameObject, flickerDuration,
                () => flickerRoutine = StartCoroutine(RandomFlicker()))));

        // — 3. Arc flight ——————————————————————————————————————
        Vector3 start = transform.position;
        Vector3 mid1 = Vector3.Lerp(spawnPos, to, 0.2f) + Vector3.right * arcHeight;
        Vector3 mid2 = Vector3.Lerp(spawnPos, to, 0.8f) + Vector3.left * arcHeight;
        Vector3[] path = { start, mid1, mid2, to };

        seq.append(
            LeanTween.move(gameObject, path, flightTime)
                     .setEase(flightEase)
                     .setOnUpdate((Vector3 v) => FaceHeading(v))
        );

        // — 4. Power-down animation ————————————————————————————
        seq.append(LeanTween.delayedCall(gameObject, offTime, () =>
        {
            if (flickerRoutine != null) StopCoroutine(flickerRoutine);
            sr.LeanAnimateFPS(offSprites, (int)offFPS);
        }).setEase(offEase));

        // — 5. Destroy when finished ————————————————————————————
        seq.append(() => Destroy(gameObject));
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

    void FaceHeading(Vector3 current)
    {
        Vector2 dir = current - prevPos;
        if (dir.sqrMagnitude > 0.0001f)
        {
            float z = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0, 0, z);
            prevPos = current;
        }
    }
}