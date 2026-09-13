using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Holds a freshly spawned enemy invisible until the spawn strike actually LANDS on it, then pops
/// it into existence — a fast scale-in from nothing to its authored size. Without this the enemy
/// is standing there before the bolt reaches it, which reads backwards.
///
/// Visual only: the unit is live from frame one (AI, colliders, bookkeeping all untouched), it
/// simply can't be seen for the ~0.12s the strike is in flight. Renderers and world canvases are
/// switched off rather than the object being scaled to nothing, so nothing downstream ever reads a
/// degenerate transform. Anything that leaves early — pooled, killed mid-flight — gets its scale
/// and renderer states put back in OnDisable, so a reused instance never comes back tiny.
/// </summary>
public class SpawnStrikeMaterialise : MonoBehaviour
{
    /// <summary>How long the pop takes once the strike lands.</summary>
    public const float GROW = 0.2f;
    const float START_SCALE = 0.15f;   // it arrives already the size of a spark, not a point

    Vector3 target;
    float delay, t;
    bool revealed, restored;
    readonly List<Renderer> hidden = new List<Renderer>();
    readonly List<Canvas> hiddenCanvases = new List<Canvas>();

    /// <summary>Hide <paramref name="go"/> now; reveal and grow it in <paramref name="delay"/>
    /// seconds. A second call on the same object just restarts the wait.</summary>
    public static void Play(GameObject go, float delay)
    {
        if (go == null || !Application.isPlaying) return;
        var m = go.GetComponent<SpawnStrikeMaterialise>();
        if (m == null) m = go.AddComponent<SpawnStrikeMaterialise>();
        m.Begin(delay);
    }

    void Begin(float delay)
    {
        if (!restored && hidden.Count == 0 && hiddenCanvases.Count == 0) target = transform.localScale;
        Restore(scaleBack: false);   // a restart re-hides from a known-good state

        this.delay = Mathf.Max(0f, delay);
        t = 0f;
        revealed = false;
        restored = false;

        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
        {
            if (!r.enabled) continue;
            r.enabled = false;
            hidden.Add(r);
        }
        foreach (Canvas c in GetComponentsInChildren<Canvas>(true))
        {
            if (!c.enabled) continue;
            c.enabled = false;
            hiddenCanvases.Add(c);
        }
        transform.localScale = target * START_SCALE;
    }

    void Update()
    {
        t += Time.deltaTime;

        if (!revealed)
        {
            if (t < delay) return;
            revealed = true;
            Restore(scaleBack: false);          // renderers back on, still small
            transform.localScale = target * START_SCALE;
        }

        float f = Mathf.Clamp01((t - delay) / GROW);
        // fast out of the gate, settling into size — swap for f*f if you want a true slow-start ease
        float ease = 1f - Mathf.Pow(1f - f, 3f);
        transform.localScale = target * Mathf.Lerp(START_SCALE, 1f, ease);

        if (f >= 1f)
        {
            transform.localScale = target;
            Destroy(this);
        }
    }

    void Restore(bool scaleBack)
    {
        foreach (Renderer r in hidden) if (r != null) r.enabled = true;
        foreach (Canvas c in hiddenCanvases) if (c != null) c.enabled = true;
        hidden.Clear();
        hiddenCanvases.Clear();
        if (scaleBack && target != Vector3.zero) transform.localScale = target;
    }

    // Pooled away or killed mid-flight: never leave an instance hidden or shrunk.
    void OnDisable() { restored = true; Restore(scaleBack: true); }
    void OnDestroy() { Restore(scaleBack: true); }
}
