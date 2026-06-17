using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Secondary boundary VFX: animated sprite "wisps" that spawn at random points along the map edge
/// like shooting stars — they streak along the boundary, weave in and out across the line, slow
/// down, and fade out. Each plays a looping sprite-sheet animation (frame stepped per-wisp in Update,
/// no tween) with the era material (GS.MatByEra), and rides the live boundary polygon (world space, so it tracks reshaping
/// + Shrink scaling).
///
/// Drop on an empty GameObject (e.g. a child of MapManager). Frames default to the JLVisual sheet.
/// </summary>
public class MapBoundaryWeave : MonoBehaviour
{
    [Header("Animation")]
    [Tooltip("Sprite-sheet frames (GS.LeanAnimateFPS). Falls back to Resources/JLVisual.")]
    public Sprite[] frames;
    public int fps = 12;

    [Header("Era material (GS.MatByEra)")]
    public bool bright = true;
    public bool lit = false;
    public bool superBright = false;

    [Header("Spawning (random shooting-star bursts)")]
    [Tooltip("Wisps spawned per second. If > 0 this is used; otherwise the random interval below is.")]
    public float emissionRate = 0f;
    [Tooltip("Random delay range between spawns (seconds). Used when Emission Rate is 0.")]
    public Vector2 spawnEvery = new Vector2(0.15f, 0.5f);
    [Tooltip("Cap on simultaneously alive wisps.")]
    public int maxWisps = 14;

    [Header("Life & motion")]
    public Vector2 lifetime = new Vector2(1.2f, 2.6f);
    [Tooltip("Initial travel speed along the edge (polygon-samples/sec).")]
    public Vector2 startSpeed = new Vector2(16f, 28f);
    [Tooltip("Fraction of start speed it slows to by the end (shooting-star deceleration).")]
    [Range(0f, 1f)] public float endSpeedFactor = 0.06f;
    [Tooltip("How far the wisp swings in/out across the line (world units).")]
    public float weaveAmplitude = 0.6f;
    public float weaveFrequency = 1.4f;
    public float spriteSize = 0.6f;
    [Range(0f, 1f)] public float peakAlpha = 0.9f;
    [Tooltip("Gentle spin, max degrees/second (random sign per wisp).")]
    public float spinMax = 40f;

    [Header("Wiring")]
    public string sortingLayer = "Projectiles";
    public int sortingOrder = 6;

    const float TAU = 6.2831853f;

    class Wisp
    {
        public Transform t;
        public SpriteRenderer sr;
        public float s;        // arc position along the boundary (sample units)
        public float dir;      // +1 / -1 travel direction
        public float startSpd;
        public float age;
        public float life;
        public float phase;
        public float amp;
        public float freq;
        public float size;
        // Varied weave: layered harmonics + organic Perlin wander.
        public float amp2, amp3, ratio2, ratio3, phase2, phase3;
        public float seed, noiseSpeed, noiseMix;
        // Subtle along-track speed pulsing.
        public float spdWobAmp, spdWobFreq, spdWobPhase;
        // Gentle spin.
        public float spin, spinSpeed;
    }

    readonly List<Wisp> wisps = new List<Wisp>();
    float nextSpawn;
    float emitAccum;

    IEnumerator Start()
    {
        while (MapManager.i == null || MapManager.i.poly == null || MapManager.i.poly.points.Length < 3)
            yield return null;

        // Fallback so it works even if the prefab's frames weren't assigned (JLVisual lives in Resources).
        if (frames == null || frames.Length == 0)
        {
            var all = Resources.LoadAll<Sprite>("JLVisual");
            var list = new List<Sprite>();
            foreach (var s in all) if (s != null && s.name.Contains("_")) list.Add(s);
            list.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));
            frames = list.ToArray();
        }
        if (frames == null || frames.Length == 0)
            Debug.LogWarning("[MapBoundaryWeave] No frames — assign JLVisual sprites or place them in Resources.");

        nextSpawn = Random.Range(spawnEvery.x, spawnEvery.y);
        GS.OnNewEra += SetEra;
    }

    void OnDestroy()
    {
        GS.OnNewEra -= SetEra;
    }

    void SetEra(int era)
    {
        Material m = GS.MatByEra(era, bright, lit, superBright);
        foreach (var w in wisps)
            if (w != null && w.sr != null) w.sr.sharedMaterial = m;
    }

    void Update()
    {
        if (MapManager.i == null || MapManager.i.poly == null) return;
        Vector2[] pts = MapManager.i.poly.points;
        int n = pts.Length;
        if (n < 3) return;
        Transform polyT = MapManager.i.poly.transform;

        // Spawn either at a fixed emission rate or on a random interval.
        if (emissionRate > 0f)
        {
            emitAccum += emissionRate * Time.deltaTime;
            while (emitAccum >= 1f)
            {
                emitAccum -= 1f;
                if (wisps.Count < maxWisps) Spawn(n);
            }
        }
        else
        {
            nextSpawn -= Time.deltaTime;
            if (nextSpawn <= 0f)
            {
                if (wisps.Count < maxWisps) Spawn(n);
                nextSpawn = Random.Range(spawnEvery.x, spawnEvery.y);
            }
        }

        // Centroid (local) for outward orientation.
        Vector2 c = Vector2.zero;
        for (int k = 0; k < n; k++) c += pts[k];
        c /= n;

        float dt = Time.deltaTime;
        float tnow = Time.time;

        for (int k = wisps.Count - 1; k >= 0; k--)
        {
            var w = wisps[k];
            w.age += dt;
            if (w.sr == null || w.t == null || w.age >= w.life)
            {
                if (w != null && w.t != null) Destroy(w.t.gameObject);
                wisps.RemoveAt(k);
                continue;
            }

            float u = Mathf.Clamp01(w.age / w.life);
            float ease = 1f - Mathf.Pow(1f - u, 2f);                 // ease-out

            // Slow down like a shooting star, with a subtle speed pulse for variety.
            float wob = 1f + w.spdWobAmp * Mathf.Sin(tnow * w.spdWobFreq * TAU + w.spdWobPhase);
            float spd = w.startSpd * Mathf.Lerp(1f, endSpeedFactor, ease) * wob;
            w.s += w.dir * spd * dt;
            w.s %= n; if (w.s < 0f) w.s += n;

            int i = (int)w.s % n;
            int j = (i + 1) % n;
            float frac = w.s - Mathf.Floor(w.s);

            Vector2 pLocal = Vector2.Lerp(pts[i], pts[j], frac);
            Vector2 edge = pts[j] - pts[i];
            float em = edge.magnitude;
            Vector2 tan = em > 1e-5f ? edge / em : Vector2.right;
            Vector2 nrm = new Vector2(tan.y, -tan.x);
            if (Vector2.Dot(nrm, pLocal - c) < 0f) nrm = -nrm;

            // Weave across the line: layered harmonics blended with organic Perlin wander, so each
            // wisp traces a different, non-repeating path rather than a clean sine. Settles as it slows.
            float weaveDecay = Mathf.Lerp(1f, 0.3f, ease);
            float wt = tnow * w.freq * TAU + w.phase;
            float harm = Mathf.Sin(wt)
                       + w.amp2 * Mathf.Sin(wt * w.ratio2 + w.phase2)
                       + w.amp3 * Mathf.Sin(wt * w.ratio3 + w.phase3);
            harm /= (1f + w.amp2 + w.amp3);
            float wander = (Mathf.PerlinNoise(w.seed, tnow * w.noiseSpeed) - 0.5f) * 2f;
            float shape = Mathf.Lerp(harm, wander, w.noiseMix);
            float swing = w.amp * weaveDecay * shape;
            pLocal += nrm * swing;

            w.t.position = polyT.TransformPoint(pLocal);

            // Step the looping sprite-sheet frame straight off age (was a per-wisp LeanAnimateFPS
            // .setLoopClamp() tween — up to maxWisps of those ran forever and never freed their slot).
            if (frames != null && frames.Length > 0)
                w.sr.sprite = frames[(int)(w.age * fps) % frames.Length];

            // Fade in quickly, fade out over the tail.
            float fadeIn = Mathf.Clamp01(w.age / (w.life * 0.15f));
            float fadeOut = Mathf.Clamp01((w.life - w.age) / (w.life * 0.55f));
            float a = peakAlpha * fadeIn * fadeOut;
            Color col = w.sr.color; col.a = a; w.sr.color = col;

            // Slight shrink as it fades.
            w.t.localScale = Vector3.one * (w.size * Mathf.Lerp(0.85f, 1f, fadeOut));

            // Gentle spin.
            w.spin += w.spinSpeed * dt;
            w.t.rotation = Quaternion.Euler(0f, 0f, w.spin);
        }
    }

    void Spawn(int n)
    {
        var go = new GameObject("Wisp");
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingLayerName = sortingLayer;
        sr.sortingOrder = sortingOrder;
        sr.sharedMaterial = GS.MatByEra(GS.era, bright, lit, superBright);
        if (frames != null && frames.Length > 0)
            sr.sprite = frames[0];   // frames are stepped per-wisp in Update (no tween — see below)
        Color c0 = sr.color; c0.a = 0f; sr.color = c0;   // start transparent, fade in

        wisps.Add(new Wisp
        {
            t = go.transform,
            sr = sr,
            s = Random.Range(0f, n),                        // random point on the rim
            dir = Random.value < 0.5f ? -1f : 1f,
            startSpd = Random.Range(startSpeed.x, startSpeed.y),
            age = 0f,
            life = Random.Range(lifetime.x, lifetime.y),
            phase = Random.Range(0f, TAU),
            amp = weaveAmplitude * Random.Range(0.7f, 1.3f),
            freq = weaveFrequency * Random.Range(0.8f, 1.2f),
            size = spriteSize * Random.Range(0.8f, 1.2f),

            // Varied weave per wisp.
            amp2 = Random.Range(0.2f, 0.6f),
            amp3 = Random.Range(0.1f, 0.35f),
            ratio2 = Random.Range(1.7f, 2.7f),
            ratio3 = Random.Range(3.3f, 4.7f),
            phase2 = Random.Range(0f, TAU),
            phase3 = Random.Range(0f, TAU),
            seed = Random.Range(0f, 100f),
            noiseSpeed = Random.Range(0.4f, 1.1f),
            noiseMix = Random.Range(0.2f, 0.6f),

            spdWobAmp = Random.Range(0f, 0.35f),
            spdWobFreq = Random.Range(0.5f, 1.5f),
            spdWobPhase = Random.Range(0f, TAU),

            spin = Random.Range(0f, 360f),
            spinSpeed = Random.Range(-spinMax, spinMax),
        });
    }
}
