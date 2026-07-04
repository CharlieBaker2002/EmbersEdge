using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>Implemented by anything that can be "the selected thing" — held battery, focused unit, etc.</summary>
public interface ISelectable
{
    void OnSelected();
    void OnDeselected();
}

/// <summary>
/// Single owner of the player's cursor focus: hover, click, and selection.
/// Replaces the previous HoverManager + SelectionManager split. One raycast pipeline
/// answers "what is under the cursor?" — used for hover events, click dispatch, and
/// the "Current" query that other systems (battery drop, build placement preview, etc.)
/// can read instead of running their own raycasts.
///
/// Selection (one ISelectable at a time) is layered on top: clicking an ISelectable
/// auto-selects it (deselecting the previous), and pressing Escape clears the
/// selection before any other EscapeRouter handler runs.
/// </summary>
public class FocusRouter : MonoBehaviour
{
    public static FocusRouter i;

    [Tooltip("Reserved — not used in the current pipeline but kept so the scene-serialised reference doesn't break.")]
    public Camera mainCamera;
    public GraphicRaycaster GR;

    private readonly List<IHoverable> lastHover = new();
    private IHoverable[] copyArray;

    /// <summary>While set, click dispatch is owned by a transient mode (e.g. Force Field "Move Field"
    /// aiming) — the router ignores presses so the mode's own drag handler isn't fought over.</summary>
    public static bool Suppressed;

    // -------- selection state --------
    private ISelectable currentSelection;
    public ISelectable Current => currentSelection;
    /// <summary>(previous, next). Fires after the transition has been applied.</summary>
    public event Action<ISelectable, ISelectable> OnSelectionChanged;
    private Action deselectViaEscape;

    private void Awake()
    {
        if (i != null && i != this) { Destroy(this); return; }
        i = this;
        Suppressed = false;   // never start a scene with click dispatch locked out
        deselectViaEscape = () => Clear();
        LeanTween.reset();
    }

    private IEnumerator Start()
    {
        yield return null;
        IM.i.pi.Player.Interact.started += _ => Interact();
        IM.i.pi.Player.Interact.Enable();
        while (true)
        {
            yield return new WaitForSecondsRealtime(0.16667f);
            HoverInteract();
        }
    }

    // -------- click pipeline --------

    void Interact()
    {
        // A transient aiming mode (e.g. Force Field "Move Field") owns the cursor — don't dispatch.
        if (Suppressed) return;

        bool planting = BM.i != null && BM.i.planting;

        if (!planting && IM.controller && !TutorialManager.tutorial)
        {
            if (!IM.i.CActive())
            {
                IM.i.OpenCursor();
                return;
            }
        }

        // UI canvas first — graphic raycaster.
        if (GR != null)
        {
            var ped = new PointerEventData(EventSystem.current) { position = IM.i.MouseScreen() };
            var results = new List<RaycastResult>();
            GR.Raycast(ped, results);
            foreach (var result in results)
            {
                if (!result.gameObject.CompareTag("UI")) continue;
                if (result.gameObject.TryGetComponent<IClickable>(out var clickable))
                {
                    DispatchClick(clickable);
                    return;
                }
            }
        }

        // From here on the click is a world click. While a building is being placed, that click
        // belongs to placement only — BM's TryPlace runs on Interact.performed, just after this
        // (Interact.started) handler. Dispatching a world IClickable here would select or click an
        // existing building under the cursor, masking the place (most noticeable when dropping a
        // building right next to others). UI clicks above are still honoured, so the build menu's
        // Back arrow works mid-placement — clicking it cancels planting before TryPlace fires.
        if (planting) return;

        // World — 2D physics raycast. Coincident 2D hits have no stable order, so a cable's
        // EdgeCollider2D (CableLink) — which starts at its pylon and shares the pylon's layer —
        // would randomly win the press over the pylon itself, leaving press-and-drag to start a
        // new cable only ~half the time. Demote CableLink to a fallback: a cable is only picked
        // when nothing else is under the cursor (i.e. clicking it mid-span, away from buildings).
        var hits = WorldHits();
        if (hits.Length > 0)
        {
            IClickable cableFallback = null;
            foreach (var h in hits)
            {
                IClickable clickable = ClickableOf(h.collider);
                if (clickable == null) continue;
                if (clickable is CableLink) { cableFallback ??= clickable; continue; }
                DispatchClick(clickable);
                return;
            }
            if (cableFallback != null) { DispatchClick(cableFallback); return; }
            IM.i.CloseCursor();
        }
        else
        {
            IM.i.CloseCursor();
        }
    }

    void DispatchClick(IClickable clickable)
    {
        // ISelectables auto-select on click — keeps "what did the player just pick?" in
        // one place rather than asking every IClickable to remember to call Select().
        if (clickable is ISelectable s)
        {
            Select(s);
        }
        clickable.OnClick();
    }

    // -------- hover pipeline --------

    void HoverInteract()
    {
        copyArray = lastHover.ToArray();
        lastHover.Clear();

        if (GR != null)
        {
            var ped = new PointerEventData(EventSystem.current) { position = IM.i.MouseScreen() };
            var results = new List<RaycastResult>();
            GR.Raycast(ped, results);
            foreach (var result in results)
            {
                if (!result.gameObject.CompareTag("UI")) continue;
                if (result.gameObject.TryGetComponent<IHoverable>(out var hoverable))
                {
                    lastHover.Add(hoverable);
                    if (copyArray.Contains(hoverable)) continue;
                    hoverable.OnHover();
                }
            }
        }

        var hits = WorldHits();
        foreach (var h in hits)
        {
            if (!h.transform.CompareTag("UI")) continue;
            if (h.collider.attachedRigidbody != null)
            {
                if (h.collider.attachedRigidbody.TryGetComponent<IHoverable>(out var hoverable))
                {
                    lastHover.Add(hoverable);
                    if (copyArray.Contains(hoverable)) continue;
                    hoverable.OnHover();
                }
            }
            else if (h.collider.TryGetComponent<IHoverable>(out var hoverable))
            {
                lastHover.Add(hoverable);
                if (copyArray.Contains(hoverable)) continue;
                hoverable.OnHover();
            }
        }

        var stillHovered = copyArray.Intersect(lastHover);
        foreach (var gone in copyArray.Where(x => !stillHovered.Contains(x)))
        {
            gone.OnDeHover();
        }
    }

    RaycastHit2D[] WorldHits()
    {
        var mask = LayerMask.GetMask("Ally Buildings", "Ally Units", "CharOnly", "UI");
        if (IM.controller)
        {
            return Physics2D.RaycastAll(IM.i.CWorldPoint(), Vector3.forward, 1000, mask);
        }
        var p = IM.i.MousePosition();
        return Physics2D.RaycastAll(new Vector3(p.x, p.y, -100), Vector3.forward, 1000, mask);
    }

    /// <summary>Resolve the IClickable for a collider (rigidbody carrier first, then the collider itself).</summary>
    static IClickable ClickableOf(Collider2D col)
    {
        if (col.attachedRigidbody != null && col.attachedRigidbody.TryGetComponent<IClickable>(out var c)) return c;
        if (col.TryGetComponent<IClickable>(out var c2)) return c2;
        return null;
    }

    // -------- selection API --------

    public void Select(ISelectable s)
    {
        if (ReferenceEquals(currentSelection, s)) return;
        var prev = currentSelection;
        currentSelection = s;
        prev?.OnDeselected();
        s?.OnSelected();
        if (EscapeRouter.i != null)
        {
            EscapeRouter.i.Remove(deselectViaEscape);
            if (s != null) EscapeRouter.i.Push(deselectViaEscape);
        }
        OnSelectionChanged?.Invoke(prev, s);
    }

    /// <summary>Clear only if `s` is current — safe for callers that don't know if they've been superseded.</summary>
    public void Deselect(ISelectable s)
    {
        if (!ReferenceEquals(currentSelection, s)) return;
        Select(null);
    }

    public void Clear() => Select(null);
}
