using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class HoverManager : MonoBehaviour
{
    public Camera mainCamera;
    public GraphicRaycaster GR;
    private List<IHoverable> lastHover = new List<IHoverable>();
    private IHoverable[] copyArray;

    private void Awake()
    {
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

    void Interact()
    {
        if (IM.controller && !TutorialManager.tutorial)
        {
            if (!IM.i.CActive())
            {
                IM.i.OpenCursor();
                return;
            }
        }
        PointerEventData PED = new PointerEventData(EventSystem.current);
        PED.position = IM.i.MouseScreen();
        List<RaycastResult> results = new List<RaycastResult>();
        GR.Raycast(PED, results);
        if (results.Count > 0)
        {
            foreach (var result in results)
            {
                if(!result.gameObject.CompareTag("UI"))continue;
                if (result.gameObject.TryGetComponent<IClickable>(out var clickable))
                {
                    clickable.OnClick();
                    return;
                }
            }
        }
        RaycastHit2D[] hit;
        if (IM.controller)
        {
            hit = Physics2D.RaycastAll(IM.i.CWorldPoint(), Vector3.forward, 1000, LayerMask.GetMask("Ally Buildings", "Ally Units", "CharOnly", "UI"));
        }
        else
        {
            hit = Physics2D.RaycastAll(new Vector3(IM.i.MousePosition().x, IM.i.MousePosition().y, -100), Vector3.forward, 1000, LayerMask.GetMask("Ally Buildings", "Ally Units", "CharOnly", "UI"));
        }
        if (hit.Length > 0)
        {
            foreach (RaycastHit2D h in hit)
            {
                if (h.collider.attachedRigidbody != null)
                {
                    if (h.collider.attachedRigidbody.TryGetComponent<IClickable>(out var clickable))
                    {
                        clickable.OnClick();
                        return;
                    }
                }
                else
                {
                    if (h.collider.TryGetComponent<IClickable>(out var clickable))
                    {
                        clickable.OnClick();
                        return;
                    }
                }
            }
        }
        else
        {
            IM.i.CloseCursor();
        }
    }

    void HoverInteract()
    {
        copyArray = lastHover.ToArray();
        lastHover.Clear();
        PointerEventData PED = new PointerEventData(EventSystem.current);
        PED.position = IM.i.MouseScreen();
        List<RaycastResult> results = new List<RaycastResult>();
        GR.Raycast(PED, results);
        if (results.Count > 0)
        {
            foreach (var result in results)
            {
                if(!result.gameObject.CompareTag("UI"))continue;
                if (result.gameObject.TryGetComponent<IHoverable>(out var clickable))
                {
                    lastHover.Add(clickable);
                    if (copyArray.Contains(clickable))
                    {
                        continue;
                    }
                    clickable.OnHover();
                }
            }
        }
        RaycastHit2D[] hit;
        if (IM.controller)
        {
            hit = Physics2D.RaycastAll(IM.i.CWorldPoint(), Vector3.forward, 1000, LayerMask.GetMask("Ally Buildings", "Ally Units", "CharOnly", "UI"));
        }
        else
        {
            hit = Physics2D.RaycastAll(new Vector3(IM.i.MousePosition().x, IM.i.MousePosition().y, -100), Vector3.forward, 1000, LayerMask.GetMask("Ally Buildings", "Ally Units", "CharOnly", "UI"));
        }
        if (hit.Length > 0)
        {
            foreach (RaycastHit2D h in hit)
            {
                if (!h.transform.CompareTag("UI")) continue;
                if (h.collider.attachedRigidbody != null)
                {
                    if (h.collider.attachedRigidbody.TryGetComponent<IHoverable>(out var clickable))
                    {
                        lastHover.Add(clickable);
                        if (copyArray.Contains(clickable))
                        {
                            continue;
                        }
                        clickable.OnHover();
                    }
                }
                else
                {
                    if (h.collider.TryGetComponent<IHoverable>(out var clickable))
                    {
                        lastHover.Add(clickable);
                        if (copyArray.Contains(clickable))
                        {
                            continue;
                        }
                        clickable.OnHover();
                    }
                }
            }
        }
        else
        {
            //IM.i.CloseCursor();
        }

        var newAr = copyArray.Intersect(lastHover);
        foreach (var gone in copyArray.Where(x => !newAr.Contains(x)))
        {
            gone.OnDeHover();
        }
    }

}
