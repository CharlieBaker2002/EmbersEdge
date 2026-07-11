using UnityEngine;

/// <summary>
/// The hauling bag on a drone's back — a bag of water, animated entirely in code.
///
/// VISUAL MODEL: DroneBag_0..10 are 11 same-size fill frames (13×12 px). At init each frame is
/// split into 3 horizontal band sprites (Sprite.Create over sub-rects, pivot at each band's
/// bottom-centre) and three chained child SpriteRenderers display them: seg0 attaches to the
/// drone, seg2 is the free tip. Rotating each segment relative to the one below makes the bag
/// BEND, not just tilt.
///
/// MOTION MODEL: one damped spring per segment, kicked by the DRONE'S OWN lateral acceleration
/// (the bag is driven by how the drone moves, not by time). Segments further from the body get
/// a bigger kick and lag behind — the tip whips while the base has settled.
///
/// FILL CHARACTER (fill fraction = cargo space used):
///   • empty  — amplitude gate is 0: the bag hangs dead still.
///   • mid    — low stiffness, high damping ratio, high per-segment divergence: loose, fluid,
///     sloshing sway where the bands visibly disagree with each other.
///   • full   — high stiffness AND high mass with LOW damping: the bag turns rigid-ish (bands
///     move together — divergence collapses) but swings as one heavy pendulum, slower frequency,
///     larger overshoot. Plus a small scale bump so a full bag reads bigger.
/// Fill also picks the sprite frame (round(fill·10)).
/// </summary>
public class SackWobble : MonoBehaviour
{
    [Header("Cargo (the bag defines hauling)")]
    public int maxSpace = 8;
    public int orbSpace = 1;
    public int chipSmallSpace = 1;
    public int chipBigSpace = 2;
    public int chipLargeSpace = 3;
    public int batterySpace = 4;
    public int equipmentSpace = 3;

    [Header("Wobble feel")]
    [Tooltip("Angular kick (deg/s²) per m/s² of the drone's lateral acceleration.")]
    public float kickGain = 40f;
    public float maxSwingDeg = 30f;
    [Tooltip("Spring stiffness (s⁻²): loose (mid-fill) → stiff (full).")]
    public float kLoose = 25f, kFull = 55f;
    [Tooltip("Damping: mid-fill slosh is well damped (fluid); a full bag is underdamped and keeps swinging.")]
    public float cLoose = 6f, cFull = 2.5f;
    [Tooltip("Effective mass: full = heavier = slower frequency, bigger overshoot.")]
    public float mEmpty = 1f, mFull = 2.6f;
    [Tooltip("Per-segment kick growth away from the body (tip whips most).")]
    public float segmentLag = 0.7f;
    public float fullScaleBump = 1.08f;

    const int SEGS = 3;
    SpriteRenderer[] seg;
    Sprite[][] bandCache;    // [frame][band]
    int frameCount;
    int currentFrame = -1;
    float fill;

    readonly float[] theta = new float[SEGS];
    readonly float[] omega = new float[SEGS];
    Rigidbody2D droneRb;
    Vector2 prevVel;
    bool built;

    void Awake() => Build();

    void Build()
    {
        if (built) return;
        built = true;
        droneRb = GetComponentInParent<Rigidbody2D>();

        var template = GetComponent<SpriteRenderer>();
        Sprite[] frames = DroneManager.LoadStripNumeric("DroneBag");
        if (frames == null || frames.Length == 0)
        {
            // no bag art — keep whatever single sprite is authored, no segmentation
            frameCount = 0;
            return;
        }
        frameCount = frames.Length;

        // Slice every frame into 3 horizontal bands (bottom band = attachment side).
        bandCache = new Sprite[frameCount][];
        for (int f = 0; f < frameCount; f++)
        {
            var src = frames[f];
            float bandH = src.rect.height / SEGS;
            bandCache[f] = new Sprite[SEGS];
            for (int b = 0; b < SEGS; b++)
            {
                var r = new Rect(src.rect.x, src.rect.y + b * bandH, src.rect.width, bandH);
                bandCache[f][b] = Sprite.Create(src.texture, r, new Vector2(0.5f, 0f), src.pixelsPerUnit,
                    0, SpriteMeshType.FullRect);
            }
        }

        // Chained segment renderers. The authored (whole-bag) renderer becomes a hidden template.
        float ppu = frames[0].pixelsPerUnit;
        float bandU = frames[0].rect.height / SEGS / ppu;
        float halfH = frames[0].rect.height * 0.5f / ppu;
        seg = new SpriteRenderer[SEGS];
        Transform parent = transform;
        for (int b = 0; b < SEGS; b++)
        {
            var go = new GameObject("sack_seg" + b);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = b == 0 ? new Vector3(0f, -halfH, 0f) : new Vector3(0f, bandU, 0f);
            seg[b] = go.AddComponent<SpriteRenderer>();
            if (template != null)
            {
                seg[b].sortingLayerID = template.sortingLayerID;
                seg[b].sortingOrder = template.sortingOrder + b;
                seg[b].sharedMaterial = template.sharedMaterial;
            }
            parent = go.transform;
        }
        if (template != null) template.enabled = false;
        SetFill(0f, true);
    }

    /// <summary>0..1 — cargo space used / maxSpace. Picks the sprite frame and retunes the springs.</summary>
    public void SetFill(float f, bool force = false)
    {
        fill = Mathf.Clamp01(f);
        transform.localScale = Vector3.one * Mathf.Lerp(1f, fullScaleBump, fill);
        if (frameCount == 0 || seg == null) return;
        int frame = Mathf.Clamp(Mathf.RoundToInt(fill * (frameCount - 1)), 0, frameCount - 1);
        if (frame == currentFrame && !force) return;
        currentFrame = frame;
        for (int b = 0; b < SEGS; b++) seg[b].sprite = bandCache[frame][b];
    }

    void OnEnable()
    {
        Build();
        prevVel = droneRb != null ? droneRb.linearVelocity : Vector2.zero;
        for (int b = 0; b < SEGS; b++) { theta[b] = 0f; omega[b] = 0f; }
    }

    void FixedUpdate()
    {
        if (seg == null || frameCount == 0) return;
        float dt = Time.fixedDeltaTime;

        // empty bag: dead still — snap the springs shut and skip the maths
        if (fill <= 1e-3f)
        {
            for (int b = 0; b < SEGS; b++)
            {
                if (Mathf.Abs(theta[b]) < 0.01f && Mathf.Abs(omega[b]) < 0.01f) continue;
                theta[b] = Mathf.Lerp(theta[b], 0f, 10f * dt);
                omega[b] = 0f;
                seg[b].transform.localRotation = Quaternion.Euler(0f, 0f, theta[b]);
            }
            if (droneRb != null) prevVel = droneRb.linearVelocity;
            return;
        }

        // the drive: the DRONE'S lateral acceleration, measured in the bag's frame
        Vector2 vel = droneRb != null ? droneRb.linearVelocity : Vector2.zero;
        Vector2 accel = (vel - prevVel) / dt;
        prevVel = vel;
        float lateral = Vector2.Dot(accel, transform.right);

        float k = Mathf.Lerp(kLoose, kFull, fill);
        float c = Mathf.Lerp(cLoose, cFull, fill);
        float m = Mathf.Lerp(mEmpty, mFull, fill);
        // fluidity peaks at half-full: that's when the bands diverge (sloshing water); a full bag
        // moves as one rigid mass, an emptier one barely carries the wave upward
        float fluidity = 4f * fill * (1f - fill);
        float amp = fill;   // overall amplitude gate grows with load

        for (int b = 0; b < SEGS; b++)
        {
            // damped spring in degrees: alpha = (-k·θ - c·ω + kick)/m
            float lag = 1f + b * segmentLag * (0.35f + fluidity);
            float kick = -lateral * kickGain * amp * lag;
            float alpha = (-k * theta[b] - c * omega[b] + kick) / m;
            omega[b] += alpha * dt;
            theta[b] += omega[b] * dt;
            theta[b] = Mathf.Clamp(theta[b], -maxSwingDeg, maxSwingDeg);
            seg[b].transform.localRotation = Quaternion.Euler(0f, 0f, theta[b]);
        }
    }
}
