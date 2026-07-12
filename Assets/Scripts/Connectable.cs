using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Drag-to-connect interaction for a Building. Reusable across energy pylons, ember
/// cables, or any future "click on me, drag to another building, release" mechanic.
///
/// The host Building forwards its press (its OnClick is called on press by FocusRouter)
/// into BeginDrag(). While held, this component draws a LineRenderer that follows the
/// cursor world-position. On release:
///   • If the cursor barely moved (under dragClickThreshold), OnClickWithoutDrag fires
///     so the host can decide to open its UI etc.
///   • Otherwise, the building under the cursor is sent through Validate. If Validate
///     returns true, we run the snap-and-wiggle animation and hand the LineRenderer to
///     OnConnected — the host owns the LR from that point. If Validate returns false,
///     OnRejected fires with the rejected Building and the in-flight LR is destroyed.
///
/// Static currentDragger ensures only one Connectable drags at a time.
/// </summary>
public class Connectable : MonoBehaviour
{
    [SerializeField] private LineRenderer cablePrefab;
    [Tooltip("World-units of movement under which a press+release is treated as a click instead of a drag.")]
    [SerializeField] private float dragClickThreshold = 0.2f;
    [Tooltip("Snap animation duration in seconds.")]
    [SerializeField] private float snapDuration = 0.45f;
    [Tooltip("Number of vertices on the snapped cable during the wiggle.")]
    [SerializeField] private int snapVertices = 12;
    [Tooltip("Amplitude (world units) of the perpendicular wiggle at the cable midpoint.")]
    [SerializeField] private float snapWiggleAmp = 0.22f;
    [Tooltip("Texture repeats per world unit along the cable. Tile mode keeps the texture density constant regardless of cursor speed or how taut the cable is.")]
    [SerializeField] private float cableTextureTiling = 1f;

    /// <summary>Owner decides whether this target is acceptable (range, cap, type). Default = always accept.</summary>
    public Func<Building, bool> Validate;
    /// <summary>Fires after the snap animation completes. Owner takes ownership of the LineRenderer.</summary>
    public Action<Building, LineRenderer> OnConnected;
    /// <summary>Fires when release happened without meaningful drag (host should open UI etc.).</summary>
    public Action OnClickWithoutDrag;
    /// <summary>
    /// When set, a real drag released ANYWHERE fires this with the release world position and the
    /// cable retracts — no building targeting happens. For drag-to-aim hosts (e.g. Force Field
    /// wall shaping) rather than drag-to-connect ones.
    /// </summary>
    public Action<Vector3> OnDraggedRelease;
    /// <summary>Fires when release landed on a Building but Validate returned false (host can flash etc.).</summary>
    public Action<Building> OnRejected;
    /// <summary>
    /// Optional fallback when a real drag releases on no valid Building: the host inspects the
    /// release world point (e.g. a base ore tile) and returns true to claim it. The cable
    /// retracts either way — world targets never keep a standing cable.
    /// </summary>
    public Func<Vector3, bool> TryWorldTarget;

    private static Connectable currentDragger;

    private Building owner;
    private bool dragging;
    private Vector3 dragStartWorld;
    private LineRenderer dragLR;
    private readonly List<Vector3> dragPath = new();
    private Action<InputAction.CallbackContext> releaseHandler;

    void Awake()
    {
        owner = GetComponent<Building>();
    }

    void OnEnable()
    {
        if (IM.i != null && IM.i.pi != null)
        {
            releaseHandler = _ => OnInteractReleased();
            IM.i.pi.Player.Interact.canceled += releaseHandler;
        }
    }

    void OnDisable()
    {
        if (releaseHandler != null && IM.i != null && IM.i.pi != null)
        {
            IM.i.pi.Player.Interact.canceled -= releaseHandler;
            releaseHandler = null;
        }
        CancelDrag();
    }

    /// <summary>Called by host on its OnClick (which fires on press). Starts a drag if no one else is dragging.</summary>
    public bool BeginDrag()
    {
        if (currentDragger != null) return false;
        dragging = true;
        currentDragger = this;
        dragStartWorld = owner != null ? owner.transform.position : transform.position;
        dragPath.Clear();
        dragPath.Add(dragStartWorld);
        if (cablePrefab != null)
        {
            dragLR = Instantiate(cablePrefab, dragStartWorld, Quaternion.identity);
            dragLR.positionCount = 1;
            dragLR.SetPosition(0, dragStartWorld);
            ApplyTileTexture(dragLR);
        }
        return true;
    }

    /// <summary>
    /// Force the cable onto Tile texture mode. RepeatPerSegment ties the texture to the
    /// number/spacing of path points, so a fast drag (fewer points) or the taut 2-point
    /// final cable would stretch the texture. Tile repeats by world length instead — the
    /// density is identical whether the cable is a 50-point scribble or a straight line.
    /// </summary>
    void ApplyTileTexture(LineRenderer lr)
    {
        if (lr == null) return;
        lr.textureMode = LineTextureMode.Tile;
        Vector2 ts = lr.textureScale;
        ts.x = cableTextureTiling;
        lr.textureScale = ts;
    }

    void CancelDrag()
    {
        if (dragLR != null) Destroy(dragLR.gameObject);
        dragLR = null;
        dragging = false;
        dragPath.Clear();
        if (currentDragger == this) currentDragger = null;
    }

    void Update()
    {
        if (!dragging || currentDragger != this) return;
        Vector3 cur = CursorWorld();
        if ((cur - dragPath[dragPath.Count - 1]).sqrMagnitude > 0.01f)
        {
            dragPath.Add(cur);
            if (dragLR != null)
            {
                dragLR.positionCount = dragPath.Count;
                dragLR.SetPosition(dragPath.Count - 1, cur);
            }
        }
    }

    void OnInteractReleased()
    {
        if (!dragging || currentDragger != this) return;
        Vector3 release = CursorWorld();
        bool dragged = (release - dragStartWorld).sqrMagnitude > dragClickThreshold * dragClickThreshold;

        // Take ownership of the in-flight cable and clear drag state up front, so a fresh
        // drag can start immediately while this one snaps or retracts on its own coroutine.
        var lr = dragLR;
        Vector3 start = dragStartWorld;
        dragLR = null;
        dragPath.Clear();
        dragging = false;
        currentDragger = null;

        if (!dragged)
        {
            // A press-release in place: no cable, just the UI toggle.
            if (lr != null) Destroy(lr.gameObject);
            OnClickWithoutDrag?.Invoke();
            return;
        }

        if (OnDraggedRelease != null)
        {
            // Drag-to-aim host: hand over the release point and whip the cable back.
            OnDraggedRelease(release);
            if (lr != null) StartCoroutine(Retract(lr, start));
            return;
        }

        Building target = BuildingUnderCursor(release);
        if (target == null || target == owner)
        {
            // Released on empty space → offer the point to the world-target hook (ore tiles
            // etc.), then snap the cable back into the pylon either way.
            TryWorldTarget?.Invoke(release);
            if (lr != null) StartCoroutine(Retract(lr, start));
            return;
        }

        bool accept = Validate == null || Validate(target);
        if (!accept)
        {
            // Invalid building — the player may still have been aiming at what's UNDER it
            // (ore tiles sit beneath building colliders); only flash rejection if the world
            // hook doesn't claim the spot.
            if (TryWorldTarget == null || !TryWorldTarget(release))
                OnRejected?.Invoke(target);
            if (lr != null) StartCoroutine(Retract(lr, start));
            return;
        }

        // Valid: hand the LR to the snap animation, then to the owner via OnConnected.
        if (lr != null)
            StartCoroutine(SnapAnimate(lr, start, target.transform.position, target));
        else
            OnConnected?.Invoke(target, null);
    }

    /// <summary>
    /// Reverse of the snap: collapse the cable's vertices back into the pylon (toWorld) with
    /// the same damped-sine elastic feel, then destroy it. Used when a drag is released on
    /// empty space / an invalid target, and when a connected cable is deleted.
    /// </summary>
    public IEnumerator Retract(LineRenderer lr, Vector3 toWorld, Action onComplete = null)
    {
        if (lr == null) { onComplete?.Invoke(); yield break; }

        int N = Mathf.Max(snapVertices, 2);
        Vector3[] startPositions = new Vector3[N];
        int oldCount = Mathf.Max(lr.positionCount, 1);
        for (int i = 0; i < N; i++)
        {
            float u = (float)i / (N - 1);
            int idx = Mathf.Clamp(Mathf.RoundToInt(u * (oldCount - 1)), 0, oldCount - 1);
            startPositions[i] = lr.GetPosition(idx);
        }
        lr.positionCount = N;

        Vector3 far = startPositions[N - 1];
        Vector3 delta = far - toWorld;
        Vector3 perp = delta.sqrMagnitude > 1e-6f ? new Vector3(-delta.y, delta.x, 0f).normalized : Vector3.up;

        float t = 0f;
        while (t < snapDuration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / snapDuration);
            // ease-in: accelerate as it whips back into the pylon
            float ek = k * k;
            float decay = Mathf.Exp(-k * 5f);
            for (int i = 0; i < N; i++)
            {
                float u = (float)i / (N - 1);
                Vector3 collapsed = Vector3.Lerp(startPositions[i], toWorld, ek);
                float envelope = Mathf.Sin(u * Mathf.PI);
                float wiggle = Mathf.Sin(k * 18f - u * 4f) * decay * snapWiggleAmp * envelope;
                lr.SetPosition(i, collapsed + perp * wiggle);
            }
            yield return null;
        }
        if (lr != null) Destroy(lr.gameObject);
        onComplete?.Invoke();
    }

    IEnumerator SnapAnimate(LineRenderer lr, Vector3 a, Vector3 b, Building target)
    {
        int N = Mathf.Max(snapVertices, 2);
        Vector3[] startPositions = new Vector3[N];
        int oldCount = Mathf.Max(lr.positionCount, 1);
        for (int i = 0; i < N; i++)
        {
            float u = (float)i / (N - 1);
            int idx = Mathf.Clamp(Mathf.RoundToInt(u * (oldCount - 1)), 0, oldCount - 1);
            startPositions[i] = lr.GetPosition(idx);
        }
        lr.positionCount = N;

        Vector3 delta = b - a;
        float len = delta.magnitude;
        Vector3 perp = len > 1e-3f ? new Vector3(-delta.y, delta.x, 0f).normalized : Vector3.up;

        float t = 0f;
        while (t < snapDuration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / snapDuration);
            // ease-out for the snap-to-straight portion
            float ek = 1f - (1f - k) * (1f - k);
            // damped sine perpendicular to the cable, zero at endpoints
            float decay = Mathf.Exp(-k * 5f);
            for (int i = 0; i < N; i++)
            {
                float u = (float)i / (N - 1);
                Vector3 straight = Vector3.Lerp(a, b, u);
                Vector3 baseP = Vector3.Lerp(startPositions[i], straight, ek);
                float envelope = Mathf.Sin(u * Mathf.PI);
                float wiggle = Mathf.Sin(k * 18f - u * 4f) * decay * snapWiggleAmp * envelope;
                lr.SetPosition(i, baseP + perp * wiggle);
            }
            yield return null;
        }
        lr.positionCount = 2;
        lr.SetPosition(0, a);
        lr.SetPosition(1, b);
        OnConnected?.Invoke(target, lr);
    }

    Building BuildingUnderCursor(Vector3 world)
    {
        var hits = Physics2D.OverlapPointAll(world);
        for (int i = 0; i < hits.Length; i++)
        {
            var col = hits[i];
            var rb = col.attachedRigidbody;
            IClickable clickable = null;
            if (rb != null) rb.TryGetComponent(out clickable);
            if (clickable == null) col.TryGetComponent(out clickable);
            if (clickable is Building b) return b;
            if (clickable is IClickableCarrier carrier && carrier.clickable is Building b2) return b2;
        }
        return null;
    }

    Vector3 CursorWorld() => IM.controller ? IM.i.CWorldPoint() : (Vector3)IM.i.MousePosition();
}
