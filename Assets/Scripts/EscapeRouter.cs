using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Single owner of the Escape InputAction. Modal/transient states (placement, group UI,
/// rally-setting, etc.) call Push(handler) on enter and Remove(handler) on exit.
/// Pressing Escape pops the topmost handler and invokes it; with the stack empty,
/// invokes the default action (pause-menu toggle, set by UIManager).
/// Goal: exactly one Escape behaviour is active at any moment, globally.
/// </summary>
public class EscapeRouter : MonoBehaviour
{
    public static EscapeRouter i;
    private readonly List<Action> stack = new();
    private Action defaultAction;
    private bool subscribed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        if (i != null) return;
        var go = new GameObject("EscapeRouter");
        DontDestroyOnLoad(go);
        i = go.AddComponent<EscapeRouter>();
    }

    void Awake()
    {
        if (i != null && i != this) { Destroy(this); return; }
        i = this;
    }

    void Start()
    {
        StartCoroutine(SubscribeWhenReady());
    }

    IEnumerator SubscribeWhenReady()
    {
        while (IM.i == null || IM.i.pi == null) yield return null;
        IM.i.pi.Player.Escape.performed += OnEscape;
        IM.i.pi.Player.Escape.Enable();
        subscribed = true;
    }

    void OnDestroy()
    {
        if (subscribed && IM.i != null && IM.i.pi != null)
        {
            IM.i.pi.Player.Escape.performed -= OnEscape;
        }
        if (i == this) i = null;
    }

    public void SetDefault(Action a)
    {
        defaultAction = a;
    }

    public void Push(Action handler)
    {
        if (handler == null) return;
        stack.Remove(handler); // dedupe — never two copies of the same handler
        stack.Add(handler);
    }

    public void Remove(Action handler)
    {
        if (handler == null) return;
        stack.Remove(handler);
    }

    void OnEscape(InputAction.CallbackContext _)
    {
        if (stack.Count > 0)
        {
            var top = stack[stack.Count - 1];
            stack.RemoveAt(stack.Count - 1);
            top?.Invoke();
            return;
        }
        if (defaultAction != null)
        {
            defaultAction.Invoke();
            return;
        }
        // Fallback: SetDefault may not have run yet (script execution order). Resolve
        // UIManager on demand so a fresh scene's first Esc still pauses correctly.
        if (UIManager.i != null) UIManager.i.InvokeDefaultEscape();
    }
}
