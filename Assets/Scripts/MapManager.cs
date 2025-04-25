 using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.Splines;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

public class MapManager : MonoBehaviour
{
    public static MapManager i;

    public RenderTexture dungeonTex;
    public GameObject par;
    public RenderTexture homeTexture;
    [SerializeField] Camera[] cams;
    public RawImage raw;
    [SerializeField] RectTransform[] mmt; //minimaptransform
    bool mapBigger = false;
    Vector2[] sizeDelta = new Vector2[2];
    [SerializeField] Vector3[] poses;
    Vector3[] initPoses;
    [SerializeField] private Material[] mats;
    private Sprite savedSprite;
    
    [Space(6)]
    [SerializeField] LineRenderer lr;
    [SerializeField] SplineContainer sc;
    [SerializeField] SpriteMask sr;
    public PolygonCollider2D poly;
    private static bool? fading = null;
    private static Coroutine c;
    [Header("Represents time to wait to update the lr for rotation. Negative is backwards")]
    [SerializeField] float spinWait = -0.1f;
    const float oneoversixty = 1f / 60f;
    float timebuf = 0f;
    float tim;

    (int, BezierKnot) mapchangedata;
    readonly int textureSize = 400;
    readonly float scale = 40;
    bool fff = false; //finish follow flag

    public List<ActionScript> asses = new List<ActionScript>();
    
    bool shrinking = false;
    public static System.Action OnUpdateMap;

    private Texture2D tex;

    #region MiniMap

    void SwapMap()
    {
        if (raw.texture == homeTexture)
        {
            if (!RefreshManager.i.ARENAMODE)
            {
                raw.texture = dungeonTex;
                cams[0].gameObject.SetActive(false);
            }
            else
            {
                raw.texture = null;
                par.SetActive(false);
            }
          
        }
        else if (raw.texture == dungeonTex)
        {
            raw.texture = null;
            par.SetActive(false);
        }
        else
        {
            par.SetActive(true);
            raw.texture = homeTexture;
            cams[0].gameObject.SetActive(true);
        }
    }

    public void SetMap(bool dungeon)
    {
        par.SetActive(true);
        if (dungeon)
        {
            raw.texture = dungeonTex;
        }
        else
        {
            raw.texture = homeTexture;
        }
    }

    public void SnapShotDungeon()
    {
        CameraScript.i.characterIcon.transform.position = DM.i.activeRoom.transform.position;
        Vector2 bounds = DM.i.activeRoom.col.bounds.size;
        float standard = new float[] { 5f, 7f, 9f }[GS.era];
        Vector2 scal = new Vector2(Mathf.CeilToInt(bounds.x/(standard * 1.75f)),Mathf.CeilToInt(bounds.y / (standard * 1.75f)));
        CameraScript.i.characterIcon.transform.localScale = scal;
        cams[1].gameObject.SetActive(true);
        GS.QA(() =>cams[1].gameObject.SetActive(false),1);
    }

    private void ResizeTexture(bool bigger)
    {
        bool home = (RenderTexture)raw.texture == homeTexture;
        if (home)
        {
            homeTexture = new RenderTexture(bigger ? 1024 : 256, bigger ? 1024 : 256, 32);
            raw.texture = homeTexture;
            cams[0].targetTexture = homeTexture;
        }
        else
        {
            //dungeonTex = new RenderTexture(bigger ? 2048 : 1024, bigger ? 2048 : 1024, 32);
            raw.texture = dungeonTex;
            cams[1].targetTexture = dungeonTex;
        }
    }

    private void PressMap(InputAction.CallbackContext ctx) //AND MAYBE EVEN TELEPORT MATE!
    {
        if (mapBigger)
        {
            MakeSmaller();
            return;
        }
        else if(raw.texture == null)
        {
            SwapMap();
            return;
        }
        StartCoroutine(HoldMap());


        IEnumerator HoldMap()
        {
            float tim = Time.deltaTime;
            while(tim < 0.4f)
            {
                tim += Time.deltaTime;
                yield return null;
                if(IM.i.pi.Player.Map.ReadValue<float>() == 0f)
                {
                    SwapMap();
                    yield break;
                }
            }
            if (raw.texture == dungeonTex)
            {
                if (PortalScript.i.canPortal && PortalScript.i.inDungeon && DM.i.activeRoom.defeated == true)
                {
                    cams[1].gameObject.SetActive(true);
                    StartResize();
                    CharacterScript.CS.AS.Stop();
                    CharacterScript.CS.locked = false;
                    if (!CameraScript.i.locked)
                    {
                        CameraScript.i.Lock();
                    }
                    IM.i.pi.Player.LockMap.Disable();
                    Room prevRoom = DM.i.activeRoom;
                    foreach (Light2D l in prevRoom.transform.parent.GetComponentsInChildren<Light2D>())
                    {
                        l.intensity *= 2;
                    }
                    Room closeRoom;
                    while (true)
                    {
                        if (IM.i.pi.Player.Map.ReadValue<float>() == 0f)
                        {
                            PortalScript.i.TeleShortCut(prevRoom);
                            ResetAsIfNothingHappened(prevRoom);
                            MakeSmaller();
                           
                            yield break;
                        }
                        else
                        {
                            cams[1].transform.position += 0.5f * (Vector3)IM.i.pi.Player.Movement.ReadValue<Vector2>();
                            closeRoom = DM.i.rs.OrderBy(n => n.defeated ? Vector2.Distance(cams[1].transform.position, n.transform.position) : 999999).First();
                            if (closeRoom != prevRoom)
                            {
                                foreach (Light2D l in prevRoom.transform.parent.GetComponentsInChildren<Light2D>())
                                {
                                    l.intensity /= 2f;
                                }
                                foreach (Light2D l in closeRoom.transform.parent.GetComponentsInChildren<Light2D>())
                                {
                                    l.intensity *= 2f;
                                }
                                prevRoom = closeRoom;
                            }
                        }
                        yield return null;
                    }
                }
            }
            else
            {
                StartResize();
                while(IM.i.pi.Player.Map.ReadValue<float>() != 0f)
                {
                    yield return null;
                }
                MakeSmaller();
            }
        }

        void ResetAsIfNothingHappened(Room r)
        {
            foreach (Light2D l in r.transform.parent.GetComponentsInChildren<Light2D>())
            {
                l.intensity /= 2f;
            }
            CharacterScript.CS.locked = true;
            IM.i.pi.Player.LockMap.Enable();
            LeanTween.move(cams[1].gameObject, new Vector2(DM.i.activeRoom.transform.position.x, DM.i.activeRoom.transform.position.y), 0.5f).setOnComplete( SnapShotDungeon);
        }

        void StartResize()
        {
            mmt[0].LeanCancel();
            mmt[1].LeanCancel();
            mapBigger = true;
            mmt[0].LeanSize(sizeDelta[0] * 2f, 0.55f).setOnUpdate((float ctx) => mmt[0].anchoredPosition = new Vector2(-mmt[0].sizeDelta.x * 0.5f, -mmt[0].sizeDelta.y * 0.5f));
            mmt[1].LeanSize(sizeDelta[1] * 2f, 0.5f).setOnUpdate((float ctx) => mmt[1].anchoredPosition = new Vector2(-mmt[1].sizeDelta.x * 0.5f, -mmt[1].sizeDelta.y * 0.5f));
            ResizeTexture(true);
        }

        void MakeSmaller()
        {
            mmt[0].LeanCancel();
            mmt[1].LeanCancel();
            mapBigger = false;
            mmt[0].LeanSize(sizeDelta[0], 0.55f).setOnUpdate((float ctx) => mmt[0].anchoredPosition = new Vector2(-mmt[0].sizeDelta.x * 0.5f, -mmt[0].sizeDelta.y * 0.5f));
            mmt[1].LeanSize(sizeDelta[1], 0.5f).setOnUpdate((float ctx) => mmt[1].anchoredPosition = new Vector2(-mmt[1].sizeDelta.x * 0.5f, -mmt[1].sizeDelta.y * 0.5f));
            ResizeTexture(false);
        }
    }

    #endregion
    #region Aesthetics

    IEnumerator Start()
    {
        GS.OnNewEra += (ctx) => { lr.material = mats[ctx]; };
        GS.bounds = poly;
        sizeDelta[0] = mmt[0].sizeDelta;
        sizeDelta[1] = mmt[1].sizeDelta;
        IM.i.pi.Player.Map.started += PressMap;
        IM.i.pi.Player.Map.Enable();
        yield return null;
        UpdateLRFromSpline();
        GenerateSpriteFromPoly();
        FadeBoundary(true);
        yield return new WaitForSeconds(1.5f);
        Vector3 v;
        //spinnage
        while (true)
        {
            if (spinWait > 0)
            {
                v = lr.GetPosition(0);
                for (int i = 0; i < lr.positionCount - 1; i++)
                {
                    lr.SetPosition(i, lr.GetPosition(i + 1));
                }
                lr.SetPosition(lr.positionCount - 1, v);
            }
            else if (spinWait < 0f)
            {
                v = lr.GetPosition(lr.positionCount - 1);
                for (int i = lr.positionCount - 1; i > 0; i--)
                {
                    lr.SetPosition(i, lr.GetPosition(i - 1));
                }
                lr.SetPosition(0, v);
            }
            else
            {
                yield return null;
                continue;
            }
            tim = Time.time;
            yield return new WaitForSeconds(Mathf.Abs(spinWait - timebuf));
            timebuf = (spinWait - timebuf) - (Time.time - tim); //difference in wanted time and actual time; -0.1 means 0.1 seconds longer wait than wanted. So we minus 0.1 seconds from the next "wait command".
        }
    }

    public static void SetSpin(float acco)
    {
        i.StartCoroutine(i.ISetSpin(acco));
    }

    private IEnumerator ISetSpin(float acco)
    {
        for (float t = 0f; t < EmbersEdge.warmUpTime; t += Time.fixedDeltaTime)
        {
            i.spinWait = Mathf.Lerp(i.spinWait, oneoversixty / acco, 0.1f * t * oneoversixty);
            yield return new WaitForFixedUpdate();
        }
    }

    public static void DeSpin()
    {
        i.StartCoroutine(i.ISetSpin(-1f));
    }

    private void Awake()
    {
        i = this;
        fading = null;
    }

    public static void FadeBoundary(bool fadeIn)
    {
        if (fading.HasValue)
        {
            if (fading.Value == fadeIn)
            {
                return;
            }
            else
            {
                i.StopCoroutine(c);
            }
        }
        fading = fadeIn;
        c = i.StartCoroutine(i.IFade(fadeIn));
    }

    private IEnumerator IFade(bool fadeIn)
    {
        if (fadeIn)
        {
            for (float i = 0f; i < 0.2f; i += 0.125f * Time.deltaTime)
            {
                lr.startWidth = i;
                lr.endWidth = i;
                yield return null;
            }
        }
        else
        {
            for (float i = 1f; i > 0f; i -= 0.125f * Time.deltaTime)
            {
                lr.startWidth = i;
                lr.endWidth = i;
                yield return null;
            }
        }
        yield return null;
        fading = null;
    }

    #endregion
    #region WorldBoundary

    /// <returns> (V2,float): (closest position offset by distNormal, t)</returns>
    public (Vector2,float) ProximityData(Vector2 pos, float dist = 0f, bool dirCheck = false)
    {
        SplineUtility.GetNearestPoint(sc.Spline, (Vector3)pos, out Unity.Mathematics.float3 clos, out float t);
        if (dist == 0f)
        {
            return ((Vector2)(Vector3)clos, t);
        }
        Vector2 close = (Vector2)(Vector3)clos;
        Vector2 dir = (pos - close).normalized;
        if (dirCheck)
        {
            if((close + dir).sqrMagnitude < close.sqrMagnitude)
            {
                dir *= -1f;
            }
        }
        return (close + dir * dist,t);
    }

    float TriCheck(Vector2 p1, Vector2 p2, Vector2 p3)
    {
        return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
    }

    bool PointInTriangle(Vector2 pt, Vector2 v1, Vector2 v2, Vector2 v3)
    {
        float d1, d2, d3;
        bool has_neg, has_pos;

        d1 = TriCheck(pt, v1, v2);
        d2 = TriCheck(pt, v2, v3);
        d3 = TriCheck(pt, v3, v1);

        has_neg = (d1 < 0) || (d2 < 0) || (d3 < 0);
        has_pos = (d1 > 0) || (d2 > 0) || (d3 > 0);

        return !(has_neg && has_pos);
    }

    /// <summary>
    /// Adds a new point to the map
    /// </summary>
    public (int,BezierKnot) MapChange(Vector3 EEpos, bool updateMask)
    {
        //Find three closest knots to the position. Determine the single closest knot.
        //Make vectors vA[1,2,3] from knots to the position
        //Make triangle from furthest two to the position and determine if the closest point is within.
        //If so, the new position is to replace the closest position. Otherwise add a new one.
        //Returns position changed and vector2 to signify if it replaced somewhere else.
        (Vector2, float)[] knots = new (Vector2, float)[sc.Spline.Count];
        int i = 0;
        float minDist = 99999f;
        int minindex = -1;
        BezierKnot returnV = new();
        foreach (BezierKnot k in sc.Spline.Knots)
        {
            knots[i] = ((Vector2)(Vector3)k.Position, ((Vector2)(EEpos - (Vector3)k.Position)).sqrMagnitude);
            if (knots[i].Item2 < minDist)
            {
                minDist = knots[i].Item2;
                minindex = i;
                returnV = k;
            }
            i++;
        }
        int ind = minindex;
        int[] indexs = new int[2] { GetNextIndex(minindex - 1), GetNextIndex(minindex + 1) }; //L & R Knots
        if (PointInTriangle(knots[minindex].Item1, knots[indexs[0]].Item1, EEpos, knots[indexs[1]].Item1)) //if closest point is within the hypothetical triangle between new point, and other two points
        {
            sc.Spline.SetKnot(minindex, new BezierKnot(EEpos));
            sc.Spline.SetTangentMode(TangentMode.AutoSmooth);
        }
        else
        {
            returnV = new BezierKnot(Vector3.zero);
            SplineUtility.GetNearestPoint(sc.Spline, EEpos, out _, out float t);
            SplineUtility.GetNearestPoint(sc.Spline, (Vector3)knots[minindex].Item1, out _, out float torg);
            if (t > torg)
            {
                if (ind == 0)
                {
                    if(t > 0.5f) //fixing the 0-point issue (due to torg always being 0, t is always > torg)
                    {
                        ind = 0;
                    }
                    else
                    {
                        ind = 1;
                    }
                }
                else
                {
                    ind = minindex + 1;
                }
            }
            sc.Spline.Insert(ind, new BezierKnot((Vector3)(Vector2)EEpos), TangentMode.AutoSmooth);
        }
        UpdateLRFromSpline();
        if (updateMask)
        {
            GenerateSpriteFromPoly();
            PushBackEE();
            CheckExtras();
        }
        return (ind,returnV);
    }

    void CheckExtras()
    {
        BoundsInt bounds = TilemapCorruption.i.extras.cellBounds;
        for (int x = bounds.xMin; x < bounds.xMax; x++)
        {
            for (int y = bounds.yMin; y < bounds.yMax; y++)
            {
                Vector3Int cellPosition = new(x, y, 0);
                TileBase tile = TilemapCorruption.i.extras.GetTile(cellPosition);
                if (tile != null)
                {
                    Vector2 v = TilemapCorruption.i.extras.CellToWorld(cellPosition);
                    if (InsideBounds(v,true))
                    {
                        TilemapCorruption.i.Event(cellPosition, tile);
                    }
                }
            }
        }
    }

    public void UndoChange((int,BezierKnot) tup)
    {
        if((Vector3)tup.Item2.Position == Vector3.zero)
        {
            sc.Spline.RemoveAt(tup.Item1);
        }
        else
        {
            sc.Spline.SetKnot(tup.Item1, tup.Item2);
            sc.Spline.SetTangentMode(TangentMode.AutoSmooth);
        }
    }

    private int GetNextIndex(int val)
    {
        if(val == sc.Spline.Count)
        {
            val = 0;
        }
        else if(val == -1)
        {
            val = sc.Spline.Count - 1;
        }
        return val;
    }

    private void UpdateLRFromSpline()
    {
        Vector3[] vs = new Vector3[999];
        for (int i = 0; i < 999; i++)
        {
            vs[i] = sc.Spline.EvaluatePosition(i / 999f);
        }
        lr.SetPositions(vs);
    }

    private void UpdatePolyFromLR()
    {
        Vector2[] vs = new Vector2[999];
        for (int i = 0; i < 999; i++)
        {
            vs[i] = lr.GetPosition(i);
        }
        poly.points = vs;
    }

    public void GenerateSpriteFromPoly()
    {
        if (tex != null)
        {
            Destroy(tex);
        }
        tex = new Texture2D(textureSize, textureSize);
        UpdatePolyFromLR();
        float coef = scale / textureSize;
        for (int x = 0; x < textureSize; x++)
        {
            for (int y = 0; y < textureSize; y++) // 40 wide, covering 400 positions
            {
                Vector2 worldPos = new(x * coef - 0.5f * textureSize * coef, y * coef - 0.5f * textureSize * coef);
                if (poly.OverlapPoint(worldPos))
                {
                    tex.SetPixel(x, y, Color.white);
                }
                else
                {
                    tex.SetPixel(x, y, Color.clear);
                }
            }
        }
        tex.Apply();
        Sprite sprite = Sprite.Create(tex, new Rect(0, 0, textureSize,textureSize), new Vector2(0.5f, 0.5f), textureSize/scale);
        sr.sprite = sprite;
    }

    private void OnDestroy()
    {
        if (tex != null)
        {
            Destroy(tex);
        }
    }

    public void StopFollowMouse(UnityEngine.InputSystem.InputAction.CallbackContext ctx)
    {
        if (poly.OverlapPoint(IM.i.MouseWorld()))
        {
            return;
        }
        fff = true;
        IM.i.pi.Player.Interact.performed -= StopFollowMouse;
    }

    IEnumerator IFollowMouse(EmbersEdge EE)
    {
        while (EE.InDungeon)
        {
            yield return null;
        }
        Vector2 v = Vector2.zero;
        Vector2 vprev = EE.transform.position;
        Vector2 vEE;
        yield return new WaitForSeconds(1f);
        IM.i.pi.Player.Interact.performed += StopFollowMouse;
        while (fff == false)
        {
            v = IM.i.MouseWorld();
            if (poly.OverlapPoint(v) || Vector2.Distance(poly.ClosestPoint(v),v) < 0.25f)
            {
                yield return null;
                continue;
            }
            mapchangedata = MapChange(ProximityData(v, 3f).Item1, false); //spline is only changed and then unchanged, so unless multithreading is used, this is read-safe (no changes are kept until fff == true).

            vEE = ProximityData(v,2f,true).Item1;
            vprev = vEE - vprev;
            for (int i = 0; i < EE.lr.positionCount; i++)
            {
                EE.positions[i] = EE.positions[i] + (Vector3)vprev;
            }
            vprev = vEE;
            EE.transform.position = vEE;
            UpdateLRFromSpline();
            UndoChange(mapchangedata);
            yield return null;
        }
        Vector2 pos = ProximityData(v, 3f).Item1;
        MapChange(pos,true);
        yield return null;
        EE.transform.position = ProximityData(pos, 2f, true).Item1;
        EE.transform.position = new Vector3(EE.transform.position.x, EE.transform.position.y, 1);
        EE.StartCoroutine(EE.MakeFX());
        yield return new WaitForSeconds(0.1f);
        OnUpdateMap.Invoke();
        fff = false;
    }

    //Cannot be during shift-phase or code must be changed.
    private void PushBackEE()
    {
        foreach(EmbersEdge EE in SpawnManager.instance.EEs)
        {
            if(EE.shiftPos == Vector2.zero)
            {
                EE.transform.position = ProximityData(EE.transform.position, 1.5f,true).Item1;
            }
            else
            {
                EE.transform.position = ProximityData(EE.shiftPos, 1.5f,true).Item1;
                EE.StopCoroutine(nameof(EE.MoveI));
                EE.hastiness = 0.13f;
            }
        }
    }




    public static bool InsideBounds(Vector2 p, bool leniant = false)
    {
        if (i.poly.OverlapPoint(p))
        {
            return true;
        }
        if (leniant)
        {
            if (i.poly.OverlapPoint(p + 0.5f * Vector2.up))
            {
                return true;
            }
            if (i.poly.OverlapPoint(p + 0.5f * Vector2.right))
            {
                return true;
            }
            if (i.poly.OverlapPoint(p + 0.5f * -Vector2.up))
            {
                return true;
            }
            if (i.poly.OverlapPoint(p + 0.5f * -Vector2.right))
            {
                return true;
            }
            if (i.poly.OverlapPoint(p + 0.5f * Vector2.one))
            {
                return true;
            }
            if (i.poly.OverlapPoint(p + 0.5f * Vector2.one))
            {
                return true;
            }
            if (i.poly.OverlapPoint(p + 0.5f * new Vector2(1, -1)))
            {
                return true;
            }
            if (i.poly.OverlapPoint(p + 0.5f * new Vector2(-1, 1)))
            {
                return true;
            }
        }
        return false;
    }

    public static void BeginPlace(EmbersEdge EE, bool noReturnToDungeon)
    {
        i.StartCoroutine(i.PlaceNewEE(EE, noReturnToDungeon));
    }

    private IEnumerator PlaceNewEE(EmbersEdge EE, bool noReturnToDungeon)
    {
        IM.i.pi.Player.LockMap.Disable();
        IM.i.pi.Player.Movement.Disable();
        CameraScript.i.locked = false;
        int id = 0;
        if (SetM.quickTransition)
        {
            id = SpawnManager.instance.NewTS(3f, 5f);
        }
        yield return new WaitForSeconds(2f);
        yield return CameraScript.i.StartCoroutine(CameraScript.i.GoTo(EE.transform.position));
        CameraScript.i.DistortLens(true, false,true,true);
        EE.Dissapear();
        yield return null;
        yield return CameraScript.i.StartTemporaryZoom(1.1f, 0.5f, 1.5f, 1.5f);
        yield return new WaitForSeconds(2f);
        yield return CameraScript.i.StartCoroutine(CameraScript.i.DiveThrough(new Vector2(0,0),10f));
        CameraScript.i.DistortLens(false, false, false);
        IM.i.pi.Player.Movement.Enable();
        if (id != 0 || SetM.quickTransition)
        {
            var id1 = id;
            this.QA(()=>SpawnManager.instance.CancelTS(id1),1f);
        }
        yield return StartCoroutine(IFollowMouse(EE));
        
        if (!noReturnToDungeon)
        {
            id = 0;
            if (SetM.quickTransition)
            {
                id = SpawnManager.instance.NewTS(3f, 5f);
            }
            yield return new WaitForSeconds(0.5f);
            yield return CameraScript.i.StartCoroutine(CameraScript.i.GoTo(Vector2.zero));
            CameraScript.i.DistortLens(true, false,false,true);
            yield return new WaitForSeconds(2.5f);
            CameraScript.i.locked = true;
            PortalScript.goingToDungeon = true;
            yield return StartCoroutine(CameraScript.i.DiveThrough(GS.CS().position,CameraScript.i.correctScale,true));
            if (id != 0 || SetM.quickTransition)
            {
                SpawnManager.instance.CancelTS(id);
            }
            PortalScript.goingToDungeon = false;
        }
        else
        {
            CameraScript.i.locked = true;
            UIManager.i.FadeInCanvas();
            CameraScript.ZoomPermanent(CameraScript.i.correctScale,0.01f);
        }
        CameraScript.i.StartCoroutine(CameraScript.i.ReturnToPlayer());
        PortalScript.i.QuickOffSlider();
        yield return new WaitForSeconds(0.5f);
        IM.i.pi.Player.LockMap.Enable();
        PortalScript.i.YesPortal();
        EE.SetupUI();
    }

    public void OnTriggerExit2D(Collider2D collision)
    {
        if(collision.attachedRigidbody == null)
        {
            return;
        }
        if(collision.attachedRigidbody.TryGetComponent<ActionScript>(out var AS))
        {
            if(AS.ls!= null)
            {
                if (AS.ls.hasDied)
                {
                    return;
                }
                if (PortalScript.i.inDungeon)
                {
                    if(collision.attachedRigidbody.transform == CharacterScript.CS.transform)
                    {
                        return;
                    }
                    if(collision.attachedRigidbody.TryGetComponent<AllyAI>(out var ai))
                    {
                        if (CharacterScript.CS.group.Contains(ai))
                        {
                            return;
                        }
                    }
                }
                asses.Add(AS);
            }
        }
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if(collision.attachedRigidbody == null)
        {
            return;
        }
        if(collision.attachedRigidbody.TryGetComponent<ActionScript>(out var AS))
        {
            if (asses.Contains(AS))
            {
                asses.Remove(AS);
            }
        }
    }

    private void Update()
    {
        for (int i = 0; i < asses.Count; i++)
        {
            if (asses[i] == null)
            {
                asses[i] = asses[^1];
                asses.RemoveAt(asses.Count - 1);
                i--;
                continue;
            }

            if (InsideBounds(asses[i].transform.position))
            {
                asses.RemoveAt(i);
                i--;
            }
            else
            {
                asses[i].AddPush(2f * Time.fixedDeltaTime, true, -asses[i].transform.position);
            }
        }
    }
    #endregion
    

    public void Shrink()
    {
        if (shrinking) return;
        shrinking = true;
        StartCoroutine(IShrink());
        return;

        IEnumerator IShrink()
        {
            int day = SpawnManager.day;
            float max = 27.5f + 5f * GS.era;
            for (float t = 0f; t < 25 + 5f * GS.era; t += Time.deltaTime)
            {
                yield return null;
                transform.localScale = Vector3.one * (1f - 0.4f * t / max);
                if (SpawnManager.day != day)
                {
                    break;
                }
            }
            for (float t = 0f; t < 5f; t += Time.deltaTime)
            {
                yield return null;
                transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one, t * Time.deltaTime);
            }
            transform.localScale = Vector3.one;
            shrinking = false;
        }
    }
}