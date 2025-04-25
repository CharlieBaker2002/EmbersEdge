using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.DualShock;
using Random = UnityEngine.Random;

public class IM : MonoBehaviour //Input Manager
{
    public Camera cam;
    public static IM i;
    public PlayerInput pi;
    public static bool controller = false;
    public static System.Action<bool> OnControllerChange;
    public Gradient ps5Gradient;
    public AnimationCurve[] curves;
    public Color defaultCol;
    private List<VB> vbs = new List<VB>();
    private List<int> blocked = new List<int>();
    private bool resetHaptics = false;
    private bool colourChangeable = true;
    public Gradient healGradient;
    public Gradient dmgGradient;
    public Gradient[] dieGradients;
    public Color[] defaultCols;

    public RectTransform controllerCursor;
    public float cursorSensitivity = 11.5f;

    [SerializeField] DuoInputImage[] dii;
    Coroutine controllerMoving;

    private void Awake()
    {
        i = this;
        pi = new PlayerInput();

        controller = false;
        OnControllerChange = t =>
        {
            if (t) { pi.Player.Aim.Enable(); Cursor.visible = false; }
            else { pi.Player.Aim.Disable(); Cursor.visible = true; }
        };
    }

    private IEnumerator Start()
    {
        foreach (DuoInputImage i in dii)
        {
            DuoInputImage.duos.Add(i);
        }
        yield return new WaitForSeconds(0.25f);
        cursorSensitivity = Screen.width / 2f;
        ControllerOpReset();
        InputSystem.onDeviceChange += OnDeviceChange;
        yield return new WaitForSeconds(3f);
        //Rumble(5, 4);
    }

    private void OnDeviceChange(InputDevice device, InputDeviceChange change) 
    {
        if (change is InputDeviceChange.Added or InputDeviceChange.Removed)
        {
            ControllerOpReset();
           
        }
    }

    /// <summary>
    /// Remember if dual operation, returns a normalized vector regardless.
    /// </summary>
    /// <param name="position"></param>
    /// <param name="aimIfController"></param>
    /// <returns></returns>
    public Vector2 MousePosition(Vector2 position = new Vector2(), bool aimIfController = false)
    {
        if (position != Vector2.zero)
        {
            if (aimIfController && IM.controller)
            {
                if(pi.Player.Aim.ReadValue<Vector2>().sqrMagnitude > 0.1f)
                {
                    return pi.Player.Aim.ReadValue<Vector2>().normalized;
                }
                else
                {
                    return GS.QTV(GS.CS().rotation);
                }
            }
            else
            {
                var v = (Vector2)cam.ScreenToWorldPoint(Mouse.current.position.ReadValue()) - position;
                if (aimIfController)
                {
                    return v.normalized;
                }
                return v;
            }
        }
        else
        {
            if (aimIfController && IM.controller)
            {
                return (Vector2)cam.transform.position + CameraScript.i.correctScale * i.pi.Player.Aim.ReadValue<Vector2>();
            }
            return cam.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        }
    }

    /// <summary>
    /// Returns a tuple of the position and the extent of aim
    /// </summary>
    public (Vector2,float) PosAndExtent(Vector2 start, float maxMag, float minMag = 0f)
    {
        if (controller)
        {
            Vector2 input = i.pi.Player.Aim.ReadValue<Vector2>();
            float inputMagnitude = input.magnitude;
            float extent = Mathf.Clamp(inputMagnitude * maxMag, minMag, maxMag);
            Vector3 movement = input.normalized * extent;
            return (GS.CS().position + movement,extent);
        }
        Vector2 direction = cam.ScreenToWorldPoint(Mouse.current.position.ReadValue()) - (Vector3)start;
        float clampedMagnitude = Mathf.Clamp(direction.magnitude, minMag, maxMag);
        return (start + direction.normalized * clampedMagnitude,clampedMagnitude);
    }
    
    public Vector2 Pos(Vector2 start, float maxMag, float minMag = 0f)
    {
        if (controller)
        {
            Vector2 input = i.pi.Player.Aim.ReadValue<Vector2>();
            float inputMagnitude = input.magnitude;
            Vector3 movement = input.normalized *  Mathf.Clamp(inputMagnitude * maxMag, minMag, maxMag);;
            return GS.CS().position + movement;
        }
        Vector2 direction = cam.ScreenToWorldPoint(Mouse.current.position.ReadValue()) - (Vector3)start;
        return start + direction.normalized * Mathf.Clamp(direction.magnitude, minMag, maxMag);
    }

    public float MouseMag()
    {
        return IM.i.pi.Player.Aim.ReadValue<Vector2>().magnitude;
    }

    public Vector2 MouseScreen()
    {
        if (!controller)
        {
            return Mouse.current.position.ReadValue();
        }
        else
        {
            if (CActive())
            {
                return controllerCursor.position;
            }
            else
            {
                return Vector2.zero;
            }
        }
    }

    public Vector2 MouseWorld()
    {
        if (!controller)
        {
            return cam.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        }
        else
        {
            if (CActive())
            {
                return controllerCursor.position;
            }
            OpenCursor();
            return Vector2.zero;
        }
    }


    public void Reset()
    {
        pi.Disable();
        pi.Dispose();
        pi.Enable();
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
        }
        if (prev != controller)
        {
            OnControllerChange.Invoke(controller);
            if (controller)
            {
                Rumble(1f, 0);
                if (Gamepad.current is DualShockGamepad || DualShockGamepad.current != null)
                {
                    StartCoroutine(PS5InitColor());
                }
            }
        }
        DuoInputImage.SetAll(controller);
    }

    /// <summary>
    /// Modes: 0 for linear, 1 for tremmor (increase then hold then decrease), 2 for charging (up but decelerating), 3 for zap (long decrease), 4 for suspense( accelerating up)
    /// </summary>
    /// <param name="t"></param>
    /// <param name="changeLF">Vary LF over time?</param>
    /// <param name="changeHF">Vary HF over time? </param>
    /// <param name="LF">non-varied LF</param>
    /// <param name="HF">non-varied HF</param>
    public int Rumble(float t, int mode, bool changeLF = true, bool changeHF = true, float LF = 0.1f, float HF = 0.35f, float coef = 1f, float initWait = 0f)
    {
        if (controller)
        {
            int ID = Random.Range(0, 1000000);
            if (mode == 0)
            {
                if (LF == 0.1f && HF == 0.35f)
                {
                    LF = 0.125f;
                    HF = 0.235f;
                }
                if(changeLF == false)
                {
                    LF = 0;
                }
                if(changeHF == false)
                {
                    HF = 0;
                }
                StartCoroutine(StandardRumble(LF, HF, t, ID, initWait));
                return ID;
            }
            else
            {
                StartCoroutine(IRumble(t, curves[mode - 1], changeLF, changeHF, LF, HF, coef, ID, initWait));
                return ID;
            }
        }
        return -1;
    }

    IEnumerator StandardRumble(float LF, float HF, float t, int ID, float initWait)
    {
        if (initWait > 0f)
        {
            yield return new WaitForSeconds(initWait);
        }
        for (float i = 0; i < t; i += Time.deltaTime)
        {
            vbs.Add(new VB(LF, HF,ID));
            yield return null;
        }
    }

    IEnumerator IRumble(float t, AnimationCurve c, bool changeLF, bool changeHF, float LF, float HF, float coef2, int ID, float initWait)
    {
        if (initWait > 0f)
        {
            yield return new WaitForSeconds(initWait);
        }
        float coef = c.keys[c.keys.Length - 1].time / t;
        for (float i = 0; i < t; i += Time.deltaTime)
        {
            vbs.Add(new VB((changeLF ? c.Evaluate(i * coef) : LF) * coef2, (changeHF ? c.Evaluate(i * coef) : HF) * coef2,ID));
            yield return null;
        }
    }

    public void SetColour(Gradient g, float duration = 1, int repetitions = 1)
    {
        if(DualShockGamepad.current != null && colourChangeable)
        {
            StopCoroutine(nameof(Colour));
            StartCoroutine(Colour(g, duration, repetitions));
        }
    }

    private IEnumerator Colour(Gradient g, float duration, int repetitions)
    {
        float coef = 1 / duration;
        for(int j = 0; j < repetitions; j++)
        {
            for (float i = 0; i < duration; i += Time.deltaTime)
            {
                DualShockGamepad.current?.SetLightBarColor(g.Evaluate(i * coef));
                yield return null;
            }
        }
        DualShockGamepad.current?.SetLightBarColor(GetCurrentCol());
    }

    private void Update()
    {
        if (controller)
        {
            cursorSensitivity = Screen.width * 0.4f;
            if (controllerCursor.gameObject.activeInHierarchy)
            {
                MoveCursor();
            }
        }
    }

    private Color GetCurrentCol()
    {
        if (PortalScript.i.inDungeon)
        {
            return defaultCols[GS.Era1()];
        }
        else
        {
            return defaultCols[0];
        }
    }

    public IEnumerator PS5InitColor(int repeats = 2)
    {
        if (colourChangeable)
        {
            for (int j = 0; j < repeats; j++)
            {
                for (float i = 0f; i < 1.5f; i += Time.deltaTime)
                {
                    if (DualShockGamepad.current != null)
                    {
                        DualShockGamepad.current?.SetLightBarColor(ps5Gradient.Evaluate(i / 1.5f));
                    }
                    else
                    {
                        yield break;
                    }
                    yield return null;
                }
            }
            DualShockGamepad.current.SetLightBarColor(GetCurrentCol());
        }
    }

    public void BlockColourChangeForT(float t)
    {
        if (colourChangeable == true)
        {
            colourChangeable = false;
            StartCoroutine(BlockColourChange(t));
        }
    }

    IEnumerator BlockColourChange(float t)
    {
        yield return new WaitForSeconds(t);
        colourChangeable = true;
    }

    private void LateUpdate()
    {
        if (controller)
        {
            VB? best = null;
            foreach (VB vb in vbs)
            {
                if (IsBlocked(vb))
                {
                    continue;
                }
                if (!best.HasValue)
                {
                    best = vb;
                }
                else
                {
                    if (vb.Magnitude() > best.Value.Magnitude())
                    {
                        best = vb;
                    }
                }
            }
            if (!best.HasValue)
            {
                if (!resetHaptics)
                {
                    Gamepad.current.ResetHaptics();
                    resetHaptics = true;
                }
            }
            else
            {
                resetHaptics = false;
                Gamepad.current.SetMotorSpeeds(best.Value.LF, best.Value.HF);
            }
            for(int i = 0; i < blocked.Count; i++)
            {
                bool delete = true;
                for(int j = 0; j < vbs.Count; j++)
                {
                    if (blocked[i] == vbs[j].ID)
                    {
                        delete = false;
                    }
                }
                if (delete)
                {
                    blocked.RemoveAt(i);
                    i--;
                }
            }
            vbs.Clear();
        }
    }

    private void OnDestroy()
    {
        InputSystem.onDeviceChange -= OnDeviceChange;
        pi.Disable();
        pi.Dispose();
    }

    private bool IsBlocked(VB v)
    {
        return blocked.Contains(v.ID);
    }

    public void BlockVB(int ID)
    {
        blocked.Add(ID);
    }

    public readonly struct VB
    {
        public VB(float lf, float hf, int IDP)
        {
            LF = lf;
            HF = hf;
            ID = IDP;
        }

        public float Magnitude()
        {
            return LF * 2 + HF;
        }

        public float LF { get; }
        public float HF { get; }
        public int ID { get; }
    }

    public void MoveCursor()
    {
        Vector3 v = controllerCursor.position + cursorSensitivity * Time.unscaledDeltaTime * (Vector3)pi.Player.Aim.ReadValue<Vector2>();
        if (v.x > Screen.width)
        {
            v.x = Screen.width;
        }
        else if (v.x < 0)
        {
            v.x = 0;
        }
        if (v.y > Screen.height)
        {
            v.y = Screen.height;
        }
        else if (v.y < 0)
        {
            v.y = 0;
        }
        controllerCursor.position = v;
    }

    public bool CActive()
    {
        return controllerCursor.gameObject.activeInHierarchy;
    }

    public void CloseCursor()
    {
        controllerCursor.gameObject.SetActive(false);
    }

    public void OpenCursor()
    {
        if (controller)
        {
            controllerCursor.gameObject.SetActive(true);
        }
        controllerCursor.position = new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, controllerCursor.position.z);
    }

    public Vector3 CWorldPoint()
    {
        Vector3 v = CameraScript.i.cam.ScreenToWorldPoint(controllerCursor.position);
        return new Vector3(v.x, v.y, -100);
    }

    public void KeepControllerMoving()
    {
        controllerMoving = StartCoroutine(KeepCursorMovingI());
    }

    public void StopControllerMoving()
    {
        if(controllerMoving != null)
        {
            StopCoroutine(controllerMoving);
        }
    }

    IEnumerator KeepCursorMovingI()
    {
        while (true)
        {
            if (controller)
            {
                MoveCursor();
            }
            yield return new WaitForSecondsRealtime(Time.unscaledDeltaTime);
        }
    }
}