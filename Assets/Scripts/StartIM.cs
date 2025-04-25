using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.DualShock;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class StartIM : MonoBehaviour
{
    public Camera cam;
    private PlayerInput pi;
    private bool controller = false;
    private System.Action<bool> OnControllerChange;
    public Gradient ps5Gradient;
    public RectTransform controllerCursor;
    public TextMeshProUGUI txt;
    public GraphicRaycaster GR;
    public bool startIM = true;

    private void Awake()
    {
        pi = new PlayerInput();
        OnControllerChange = _ =>
        {
            if (_) { if (startIM) { txt.text = Gamepad.current.displayName; } pi.Player.Aim.Enable(); Cursor.visible = false; pi.Player.Interact.Enable(); pi.Player.Interact.performed += Interact; }
            else { if (startIM) { txt.text = "Mouse & Keyboard"; } pi.Player.Aim.Disable(); Cursor.visible = true; pi.Player.Interact.Disable(); pi.Player.Interact.performed -= Interact; }
        };
        ControllerOpReset();
        InputSystem.onDeviceChange += OnDeviceChange;
    }

    private void Update()
    {
        if (controller)
        {
            MoveCursor();
        }
    }

    private void ControllerOpReset()
    {
        bool prev = controller;
        if (Gamepad.current == null)
        {
            controller = false;
            controllerCursor.gameObject.SetActive(false);
        }
        else
        {
            controller = true;
            controllerCursor.gameObject.SetActive(true);
            StartCoroutine(Vibrate());
        }
        if (prev != controller)
        {
            OnControllerChange.Invoke(controller);
            if (controller)
            {
                if (Gamepad.current is DualShockGamepad || DualShockGamepad.current != null)
                {
                    StartCoroutine(PS5InitColor());
                }
            }
        }
    }

    private IEnumerator Vibrate()
    {
        if (startIM)
        {
            yield return new WaitForSeconds(1f);
            Gamepad.current.SetMotorSpeeds(1f, 1f);
            yield return new WaitForSeconds(3f);
            Gamepad.current?.ResetHaptics();
        }
    }

    public IEnumerator PS5InitColor(int repeats = 2)
    {
        while(controller)
        {
            for (float i = 0f; i < 1.5f; i += Time.deltaTime)
            {
                if (DualShockGamepad.current != null)
                {
                    DualShockGamepad.current.SetLightBarColor(ps5Gradient.Evaluate(i / 1.5f));
                }
                else
                {
                    yield break;
                }
                yield return null;
            }
        }
    }

    private void OnDeviceChange(InputDevice i1, InputDeviceChange i2)
    {
        if (Application.isPlaying)
        {
            ControllerOpReset();
        }
    }

    public void MoveCursor()
    {
        Vector3 v = cam.WorldToScreenPoint(controllerCursor.position + 9f * Time.fixedDeltaTime * (Vector3)pi.Player.Aim.ReadValue<Vector2>());
        if (v.x > cam.pixelWidth)
        {
            v.x = cam.pixelWidth;
        }
        else if (v.x < 0)
        {
            v.x = 0;
        }
        if (v.y > cam.pixelHeight)
        {
            v.y = cam.pixelHeight;
        }
        else if (v.y < 0)
        {
            v.y = 0;
        }
        v = cam.ScreenToWorldPoint(v);
        controllerCursor.position = v;
    }

    void Interact(InputAction.CallbackContext ctx)
    {
        if (!controller)
        {
            return;
        }
        PointerEventData PED = new PointerEventData(EventSystem.current);
        PED.position = cam.WorldToScreenPoint(controllerCursor.position);
        List<RaycastResult> results = new List<RaycastResult>();
        GR.Raycast(PED, results);
        if (results.Count > 0)
        {
            foreach (var result in results)
            {
                if (result.gameObject.TryGetComponent<Button>(out var clickable))
                {
                    clickable.onClick.Invoke();
                    break;
                }
            }
        }
    }


    private void OnDestroy()
    {
        InputSystem.onDeviceChange -= OnDeviceChange;
        Gamepad.current?.ResetHaptics();
        pi.Disable();
        pi.Dispose();
    }

}