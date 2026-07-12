using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>Expressive beats a drone can play. This is the ANIMATION SEAM: today every emote
/// renders as a minimal speech bubble (DroneSpeechBubble); when real body animations land they
/// hang off the same Drone.Emote(...) calls without touching the behaviour code.</summary>
public enum DroneEmote
{
    Greet, Chat, Query, Happy, Sing, Sleepy, Startled, Grumble, Card,
}

/// <summary>
/// Minimal world-space speech bubble pinned above a drone: a code-generated rounded-rect sprite
/// (greyscale — palette-safe) with a short ASCII glyph. Counter-rotates in LateUpdate because
/// drones spin their whole body to face travel/work directions. Attach() builds everything in
/// code — no prefab, mirroring the Building energy-status icon pattern.
/// </summary>
public class DroneSpeechBubble : MonoBehaviour
{
    Transform follow;
    Transform vis;                 // scaled/hidden child; the component itself stays active so coroutines run
    TextMeshPro label;
    Coroutine playing;
    float bob;

    // ASCII only — the default TMP atlas has no ♪/♥ glyphs, and missing chars render as boxes.
    static readonly string[] Glyphs = { "o/", "...", "?", "<3", "~", "zz", "!", "?!", "[A]" };
    static readonly string[] CardFaces = { "[A]", "[K]", "[Q]", "[J]", "[9]", "[2]" };

    static Sprite bubbleSprite;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => bubbleSprite = null;   // no-domain-reload: runtime texture dies with play mode

    public static DroneSpeechBubble Attach(Transform owner, SpriteRenderer sortRef)
    {
        var root = new GameObject("SpeechBubble");
        root.transform.SetParent(owner, false);
        var b = root.AddComponent<DroneSpeechBubble>();
        b.follow = owner;

        var visGo = new GameObject("Vis");
        visGo.transform.SetParent(root.transform, false);
        b.vis = visGo.transform;

        var sr = visGo.AddComponent<SpriteRenderer>();
        sr.sprite = BubbleSprite();
        if (sortRef != null)
        {
            sr.sortingLayerID = sortRef.sortingLayerID;
            sr.sortingOrder = sortRef.sortingOrder + 90;
        }

        var labelGo = new GameObject("Glyph");
        labelGo.transform.SetParent(visGo.transform, false);
        labelGo.transform.localPosition = new Vector3(0.02f, 0.26f, 0f);   // centre of the bubble body
        b.label = labelGo.AddComponent<TextMeshPro>();
        b.label.rectTransform.sizeDelta = new Vector2(0.5f, 0.3f);
        b.label.alignment = TextAlignmentOptions.Center;
        b.label.enableAutoSizing = true;
        b.label.fontSizeMin = 0.5f;
        b.label.fontSizeMax = 2.2f;
        b.label.color = new Color(0.13f, 0.13f, 0.15f);
        var mr = labelGo.GetComponent<MeshRenderer>();
        if (mr != null && sortRef != null)
        {
            mr.sortingLayerID = sortRef.sortingLayerID;
            mr.sortingOrder = sortRef.sortingOrder + 91;
        }

        visGo.SetActive(false);
        return b;
    }

    public bool Showing => vis != null && vis.gameObject.activeSelf;

    public void Show(DroneEmote e, float hold = 1.4f)
    {
        if (label == null || vis == null) return;
        label.text = e == DroneEmote.Card
            ? CardFaces[Random.Range(0, CardFaces.Length)]   // a different card every play
            : Glyphs[Mathf.Clamp((int)e, 0, Glyphs.Length - 1)];
        if (playing != null) StopCoroutine(playing);
        playing = StartCoroutine(Pop(hold));
    }

    public void Hide()
    {
        if (playing != null) { StopCoroutine(playing); playing = null; }
        if (vis != null) vis.gameObject.SetActive(false);
    }

    void OnDisable()
    {
        // owner parked/frozen (pilots board via SetActive(false)) — a half-popped bubble must
        // not reappear stuck when the drone thaws
        Hide();
    }

    IEnumerator Pop(float hold)
    {
        vis.gameObject.SetActive(true);
        float t = 0f;
        const float inT = 0.14f, outT = 0.1f;
        while (t < inT)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / inT);
            vis.localScale = Vector3.one * (1.15f * Mathf.Sin(k * Mathf.PI * 0.5f));   // slight overshoot
            yield return null;
        }
        t = 0f;
        while (t < hold)
        {
            t += Time.deltaTime;
            vis.localScale = Vector3.one * Mathf.Lerp(vis.localScale.x, 1f, 0.2f);
            bob = Mathf.Sin(Time.time * 4f) * 0.02f;
            yield return null;
        }
        bob = 0f;
        t = 0f;
        while (t < outT)
        {
            t += Time.deltaTime;
            vis.localScale = Vector3.one * (1f - Mathf.Clamp01(t / outT));
            yield return null;
        }
        vis.gameObject.SetActive(false);
        playing = null;
    }

    void LateUpdate()
    {
        // Pin upright above the drone — the body rotates constantly, the bubble must not.
        if (!Showing || follow == null) return;
        transform.SetPositionAndRotation(
            follow.position + new Vector3(0f, 0.45f + bob, 0f), Quaternion.identity);
    }

    /// <summary>Rounded speech bubble with a bottom tail, drawn once per session: white body,
    /// dark-grey 1px border, point-filtered. Pivot sits at the tail tip so the attach offset is
    /// simply "above the drone".</summary>
    static Sprite BubbleSprite()
    {
        if (bubbleSprite != null) return bubbleSprite;
        const int W = 44, H = 34, TAIL = 7, R = 8;
        var mask = new bool[W * H];
        for (int y = TAIL; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                // rounded rect body between y = TAIL..H-1
                float cx = Mathf.Clamp(x, R, W - 1 - R);
                float cy = Mathf.Clamp(y, TAIL + R, H - 1 - R);
                mask[y * W + x] = (new Vector2(x - cx, y - cy)).sqrMagnitude <= R * R;
            }
        }
        // tail: small triangle tapering down-left to the tip at (W/2 - 2, 0)
        for (int y = 0; y < TAIL; y++)
        {
            int half = 1 + (y * 4) / TAIL;
            for (int x = W / 2 - 2 - half; x <= W / 2 - 2 + half; x++)
                if (x >= 0 && x < W) mask[y * W + x] = true;
        }

        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        var body = Color.white;
        var edge = new Color(0.16f, 0.16f, 0.18f);
        var clear = new Color(0f, 0f, 0f, 0f);
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                if (!mask[y * W + x]) { tex.SetPixel(x, y, clear); continue; }
                bool border = x == 0 || x == W - 1 || y == 0 || y == H - 1
                    || !mask[y * W + x - 1] || !mask[y * W + x + 1]
                    || !mask[(y - 1) * W + x] || !mask[(y + 1) * W + x];
                tex.SetPixel(x, y, border ? edge : body);
            }
        }
        tex.Apply();
        bubbleSprite = Sprite.Create(tex, new Rect(0, 0, W, H),
            new Vector2((W / 2f - 2f) / W, 0f), 80f);
        return bubbleSprite;
    }
}
