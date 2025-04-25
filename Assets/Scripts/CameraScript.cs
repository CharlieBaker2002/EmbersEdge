using UnityEngine;
using System.Collections;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

public class CameraScript : MonoBehaviour
{
    public Transform character;
    public Camera cam;
    public float correctScale = 3.5f;
    public bool locked = true;
    //private bool isZooming = false;
    private Vector2 direction;
    private InputAction move;
    private CharacterScript CS;
    private bool noMove = false;
    public static CameraScript i;
    private Coroutine initzoom;
    [SerializeField] Volume v;
    LensDistortion ld;
    
    public float shakeStrength;
    [SerializeField] int shakeInd = 0;
    
    [SerializeField] private TilemapRenderer floor;
    [SerializeField] private Material floormat;
    [SerializeField] private Material standardmat;
    
    [SerializeField] private Image redWarning;
    public Transform characterIcon;
    [SerializeField] private Light2D globalLight;

    public const float dungDistort = 0.2f;

    Coroutine distort = null;

    public void SetZeroScale()
    {
        ld.scale.Override(0f);
    }

    private void Awake()
    {
        i = this;
        cam = GetComponent<Camera>();
        //correctScale = cam.orthographicSize;
        v.sharedProfile.TryGet(out ld);
        ld.intensity.Override(0f);
        ld.scale.Override(1f);
        cam.ResetAspect();
       
      
    }

    private IEnumerator Start()
    {
        move = IM.i.pi.Player.Movement;
        yield return new WaitForSeconds(0.05f);
        if (RefreshManager.i.STARTSEQUENCE)
        {
            IM.i.pi.Player.Movement.Disable();
            locked = false;
            transform.position = new Vector3(1000f, 1000f, transform.position.z);
            UIManager.i.cg.alpha = 0;
        }
        else
        {
            transform.position = Vector3.zero;
            locked = true;
            DM.finishDungeon = true;
            cam.orthographicSize = correctScale;
        }
        
        direction = new Vector2();
        IM.i.pi.Player.LockMap.performed += _ => Lock();
        CS = GS.character.GetComponent<CharacterScript>();
        
    }
    

    public static void QuickLeanDistort(float intensity, float scale)
    {
        LeanTween.value(i.gameObject, i.ld.intensity.value, intensity, 2f).setOnUpdate(x => i.ld.intensity.Override(x)).setEaseInOutQuad();
        LeanTween.value(i.gameObject, i.ld.scale.value, scale, 2f).setOnUpdate(x => i.ld.scale.Override(x)).setEaseInOutQuad();
    }

    public void CameraSequence(Transform dungeonCam)
    {
        if (!RefreshManager.i.STARTSEQUENCE && GS.Era1() == RefreshManager.i.STARTDUNGEON)
        {
            return;
        }
        UIManager.i.FadeOutCanvas();
        noMove = true;
        transform.position = new Vector3(dungeonCam.transform.position.x, dungeonCam.transform.position.y, -10);
        initzoom = StartCoroutine(Zoom(70f + 20 * GS.era, false, 0.02f));
    }

    /// <summary>
    /// Full time is 2.6x duration
    /// </summary>
    public static void Flip(Vector2 p, float scale = 10, float duration = 2f)
    {
        i.locked = false;
        i.noMove = true;
        i.StartCoroutine(i.FlipI(p, scale, duration));
    }

    IEnumerator FlipI(Vector2 p, float scaleUp, float duration)
    {
        for (float i = 0f; i < duration; i += Time.deltaTime)
        {
            cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, correctScale + scaleUp, Time.deltaTime * 6f/duration * i);
            transform.rotation = Quaternion.Euler(i * i * 90f / Mathf.Pow(duration, 2), 0f, 0f);
            yield return null;
        }
        transform.position = new Vector3(p.x, p.y, -10f);
        cam.orthographicSize = correctScale + scaleUp;
        for (float i = duration; i > duration * 0.4f; i -= Time.deltaTime)
        {
            CameraScript.i.transform.rotation = Quaternion.Euler(i * i * 90f / Mathf.Pow(duration, 2), 0f, 0f);
            yield return null;
        }
        for (float i = duration * 0.4f; i > 0f; i -= Time.deltaTime)
        {
            CameraScript.i.transform.rotation = Quaternion.Euler(i * i * 90f / Mathf.Pow(duration, 2), 0f, 0f);
            cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, CameraScript.i.correctScale, Time.deltaTime * 6f/duration * (duration - (i + duration * 0.6f)));
            yield return null;
        }
        DistortLens(false, false,true);
        for (float i = duration * 0.6f; i > 0f; i -= Time.deltaTime)
        {
            cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, CameraScript.i.correctScale, Time.deltaTime * 6f/duration * (duration - i));
            yield return null;
        }
        yield return new WaitForSeconds(0.25f);
        StartCoroutine(ReturnToPlayer());
        yield return new WaitForSeconds(2f);
        // if (!RefreshManager.i.STARTSEQUENCE)
        // {
        //     yield return new WaitForSeconds(1f);
        // }
    }

    public void DistortLens(bool intoDistort, bool quick = false, bool fade = false, bool special = false)
    {
        LeanTween.cancel(gameObject);
        if(distort != null)
        {
            StopCoroutine(distort);
        }

        if (intoDistort)
        {
            if (fade)
            {
                UIManager.i.FadeOutCanvas();
            }
        }

        if (special)
        {
            distort = StartCoroutine(ZeroDistort(quick));
            return;
        }
        distort = StartCoroutine(Distort(intoDistort, quick,fade));
    }

    IEnumerator ZeroDistort(bool quick)
    {
        for (float i = 0; i < (quick ? 0.75f : 3f); i += Time.deltaTime)
        {
            ld.intensity.Override(Mathf.Lerp(ld.intensity.value, 0.775f, (quick ? 5f : 0.9f * i) * Time.deltaTime));
            ld.scale.Override(Mathf.Lerp(ld.scale.value, 0.05f, (quick ? 5.25f : 1.5f * i) * Time.deltaTime));
            yield return null;
        }
        ld.intensity.Override(0.775f);
        ld.scale.Override(0.05f);
    }

    IEnumerator Distort(bool intoDistort, bool quick, bool fade = false)
    {
        bool goingToDungeon = PortalScript.goingToDungeon;
        bool UId = false;
        if (RefreshManager.i.STARTSEQUENCE)
        {
            for (float i = 0; i < (quick ? 0.75f : 3f); i += Time.deltaTime)
            {
                ld.intensity.Override(Mathf.Lerp(ld.intensity.value, intoDistort ? 0.85f : !goingToDungeon ? 0f : dungDistort, (quick ? 5f: 0.9f * i) * Time.deltaTime));
                ld.scale.Override(Mathf.Lerp(ld.scale.value, intoDistort ? 0.25f : 1f, (quick ? 6f : 1.5f * i) * Time.deltaTime));
                yield return null;
            }
            if (!intoDistort)
            {
                ld.intensity.Override(!goingToDungeon ? 0f : dungDistort);
                if (fade)
                {
                    UIManager.i.FadeInCanvas();
                    RefreshManager.i.STARTSEQUENCE = false;
                    IM.i.pi.Player.Movement.Enable();
                    DM.finishDungeon = true;
                }
                ld.scale.Override(1f);
            }
            yield break;
        }
        for (float i = 0; i < (quick ? 0.75f : 3f); i += Time.deltaTime)
        {
            ld.intensity.Override(Mathf.Lerp(ld.intensity.value, intoDistort? 0.775f : !goingToDungeon ? 0f : dungDistort, (quick ? 5f : 0.9f * i) * Time.deltaTime));
            ld.scale.Override(Mathf.Lerp(ld.scale.value, intoDistort ? 0.325f : 1f, (quick ? 5.25f : 1.5f * i) * Time.deltaTime));
            if (!UId && !intoDistort && fade && i > 0.7f * (quick? 0.75f : 3f))
            {
                UIManager.i.FadeInCanvas();
                UId = true;
                if (PortalScript.i.goingHomeNow)
                {
                    PortalScript.i.goingHomeNow = false;
                    if (SpawnManager.instance.waveCompleted)
                    {
                        SpawnManager.instance.SetNextDay();
                    }
                }
            }
            yield return null;
        }
        if (intoDistort) yield break;
        ld.intensity.Override(!goingToDungeon ? 0f : 0.2f);
        ld.scale.Override(1f);
    }
       

    void Update()
    {
        if (!noMove)
        {
            if (locked)
            {
                transform.position = new Vector3(character.position.x, character.position.y,-10f);
            }
            else
            {
                direction = move.ReadValue<Vector2>();
                transform.position += 2f * Time.deltaTime * (Vector3)direction;
            }
        }
        if (!(shakeStrength > 0f)) return;
        if (shakeInd >= 2)
        {
            transform.position += GS.RandCircle(0.01f,0.02f) * shakeStrength;
            shakeInd = 0;
        }
        redWarning.color = Color.Lerp(redWarning.color, GetColorFromTime(), Time.deltaTime * shakeStrength);
    }

    private Color GetColorFromTime()
    {
        return new Color(20f * (1f + Mathf.Sin((1+shakeStrength) * Time.time)) * shakeStrength/255f, 0f, 0f, (2 + Mathf.Sin((1+shakeStrength) * Time.time)) * shakeStrength * 85f/255f);
    }
    

    void FixedUpdate()
    {
        if (shakeStrength>0f)
        {
            shakeInd+= 1;
        }
    }

    public void Lock()
    {
        if (locked)
        {
            locked = false;
            CS.locked = false;
        }
        else
        {
            StopCoroutine(ReturnToPlayer());
            StartCoroutine(ReturnToPlayer());
            CS.locked = true;
        }
    }
    /// <param name="multiplier"> Multiplies correct scale</param>
    /// <param name="delay"> inbetween  zoom out and zoom in</param>
    /// <param name="t1"> lerp coef1</param>
    /// <param name="t2"> lerp coef2</param>
    public Coroutine StartTemporaryZoom(float multiplier, float delay, float t1, float t2)
    {
        return StartCoroutine(TemporaryZoom(correctScale * multiplier, delay, t1, t2));
    }
    
    public Coroutine StartTemporaryZoomRegular(float val, float delay, float t1, float t2)
    {
        return StartCoroutine(TemporaryZoom(val, delay, t1, t2));
    }

    public IEnumerator DiveThrough(Vector2 newPos, float otherScale, bool special = false)
    {
        noMove = true;
        yield return StartCoroutine(Zoom(0.25f, false, 0.08f));
        transform.position = new Vector3(newPos.x, newPos.y, transform.position.z);
        if (special)
        {
            DistortLens(false, true,true);

        }
        yield return StartCoroutine(Zoom(otherScale, false, 0.12f));
        noMove = false;
    }

    IEnumerator TemporaryZoom(float scale, float delay, float t1, float t2)
    {
        yield return StartCoroutine(Zoom(scale, false, t1));
        yield return new WaitForSeconds(delay);
        yield return StartCoroutine(Zoom(correctScale, false, t2));
    }

    public static void ZoomPermanent(float scale, float tValue)
    {
        i.StartCoroutine(i.Zoom(scale, true, tValue));
    }

    IEnumerator Zoom(float scale, bool permanent, float tvalue)
    {
        tvalue *= 100;
        if (permanent)
        {
            correctScale = scale;
        }
        float currentScale = cam.orthographicSize;
        if(scale > currentScale)
        {
            while (currentScale < 0.99f * scale)
            {
                currentScale = Mathf.Lerp(currentScale, scale, tvalue * Mathf.Min(Time.deltaTime,0.02f));
                cam.orthographicSize = currentScale;
                yield return null;
            }
            //cam.orthographicSize = scale;
        }
        else
        {
            while (currentScale > 1.01f * scale)
            {
                currentScale = Mathf.Lerp(currentScale, scale, tvalue * Mathf.Min(Time.deltaTime, 0.02f));
                cam.orthographicSize = currentScale;
                yield return null;
            }
            //cam.orthographicSize = scale;
        }
    }

    public IEnumerator GoTo(Vector2 p)
    {
        noMove = true;
        int counter = 1;
        while (Vector2.Distance(transform.position, p) > 0.2f)
        {
            counter++;
            transform.position = new Vector3(Mathf.Lerp(transform.position.x, p.x, Mathf.Min(0.99f, Time.deltaTime * counter / 7f)), Mathf.Lerp(transform.position.y, p.y, Mathf.Min(0.99f, Time.deltaTime * counter / 7f)), transform.position.z);
            if (counter > 200)
            {
                break;
            }
            yield return null;
        }
        while (Vector2.Distance(transform.position, p) > 0.1f)
        {
            counter++;
            transform.position = new Vector3(Mathf.Lerp(transform.position.x, p.x, Mathf.Min(0.99f, Time.deltaTime * counter / 3f)), Mathf.Lerp(transform.position.y, p.y, Mathf.Min(0.99f, Time.deltaTime * counter / 3f)), transform.position.z);
            if (counter > 300)
            {
                break;
            }
            yield return null;
        }
        while (Vector2.Distance(transform.position, p) > 0.05f)
        {
            transform.Translate((p - (Vector2)transform.position) / 100f);
        }
        yield return null;
        transform.position = new Vector3(p.x, p.y, transform.position.z);
        noMove = false;
    }

    public IEnumerator ReturnToPlayer()
    {
        int counter = 1;
        while (Vector2.Distance(transform.position, character.position) > 0.2f)
        {
            counter++;
            transform.position = new Vector3(Mathf.Lerp(transform.position.x, character.position.x, Mathf.Min(0.99f, Time.deltaTime * counter / 7f)), Mathf.Lerp(transform.position.y, character.position.y, Mathf.Min(0.99f, Time.deltaTime * counter / 7f)), transform.position.z);
            if(counter > 200)
            {
                break;
            }
            yield return null;
        }
        while (Vector2.Distance(transform.position, character.position) > 0.1f)
        {
            counter++;
            transform.position = new Vector3(Mathf.Lerp(transform.position.x, character.position.x, Mathf.Min(0.99f, Time.deltaTime * counter / 3f)), Mathf.Lerp(transform.position.y, character.position.y, Mathf.Min(0.99f, Time.deltaTime * counter / 3f)), transform.position.z);
            if (counter > 300)
            {
                break;
            }
            yield return null;
        }
        while (Vector2.Distance(transform.position, character.position) > 0.05f)
        {
            transform.Translate((character.position - transform.position)/100f);
        }
        yield return null;
        transform.position = new Vector3(character.position.x, character.position.y, transform.position.z);
        locked = true;
        noMove = false;
    }

    public void EndInitSequence()
    {
        StartCoroutine(EndSequence());
    }

    private IEnumerator EndSequence()
    {
        if (!RefreshManager.i.STARTSEQUENCE && GS.Era1() == RefreshManager.i.STARTDUNGEON)
        {
            yield break;
        }
        if (initzoom != null)
        {
            StopCoroutine(initzoom);
        }
        DistortLens(true, true);
        yield return new WaitForSeconds(0.4f);
        yield return StartCoroutine(FlipI(Vector2.zero, 8f, 2.5f));
        IM.i.pi.Player.LockMap.Enable();
    }

    public void StartShaking()
    {
        redWarning.gameObject.SetActive(true);
        LeanTween.value(gameObject, 0f, 3f, 1f).setEaseOutBack().setOnUpdate(x => shakeStrength = x).setOnComplete(() =>
            LeanTween.value(gameObject, 3f, 0f, 20f).setEaseInExpo().setOnUpdate(x => shakeStrength = x));

    }

    public void StopShake()
    {
        correctScale = 3.5f;
        LeanTween.cancel(gameObject);
        shakeStrength = 0f;
        redWarning.color = new Color(0f, 0f, 0f, 0f);
        redWarning.gameObject.SetActive(false);
    }
}
