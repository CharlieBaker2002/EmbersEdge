 using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.Splines;
using Unity.Collections;
using UnityEngine.Rendering;
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
    private const int splineSampleCount = 100;   // higher‑res sampling for tighter mask fit
    private bool awaitingReadback = false;       // guard to avoid overlapping GPU readbacks
    readonly float scale = 40;
  // --- Area‑safety & smoothing constants ---
  private const float areaEpsilon        = 0.01f;  // Minimum extra area required for an expansion
  private const float minSmoothAngle     = 10f;   // Interior‑angle threshold (deg) – sharper angles will be softened
  private const float smoothDisplacement = 0.5f;   // Outward nudge (world units) for neighbour knots
  bool fff = false; //finish follow flag

    public List<ActionScript> asses = new List<ActionScript>();
    
    bool shrinking = false;
    public static System.Action OnUpdateMap;

    private Texture2D tex;
    // === GPU mask & buffer ===
    private NativeArray<Color32> pixelBuffer;
    private RenderTexture maskRT;
    private Mesh maskMesh;
    [SerializeField] private Material gpuMaskMat;   // unlit white pass‑through
    private bool meshDirty = true;

    
    // Ensure the CPU texture & buffer exist
    private void EnsureTextureAndBuffer()
    {
        if (tex == null)
            tex = new Texture2D(textureSize, textureSize, TextureFormat.Alpha8, false);

        if (!pixelBuffer.IsCreated)
            pixelBuffer = new NativeArray<Color32>(textureSize * textureSize, Allocator.Persistent,
                                                   NativeArrayOptions.ClearMemory);
    }

    // Build / rebuild the mesh outlining the polygon – runs only when the spline changes.
    // Build / rebuild the mesh outlining the polygon – handles concave shapes via ear‑clipping.
    private void TriangulateMaskMesh()
    {
        Vector2[] pts = poly.points;
        if (pts.Length < 3) return;

        if (maskMesh == null)
            maskMesh = new Mesh();
        else
            maskMesh.Clear();

        // Ear‑clipping triangulation
        List<int> indices = new List<int>();
        List<int> verts = Enumerable.Range(0, pts.Length).ToList();

        // Determine winding (positive = CCW)
        float area = 0f;
        for (int i = 0, j = pts.Length - 1; i < pts.Length; j = i++)
            area += (pts[j].x * pts[i].y) - (pts[i].x * pts[j].y);
        bool isCCW = area > 0f;

        int guard = 0;
        while (verts.Count > 3 && guard < 5000)
        {
            guard++;
            bool earFound = false;

            for (int vi = 0; vi < verts.Count; vi++)
            {
                int a = verts[(vi + verts.Count - 1) % verts.Count];
                int b = verts[vi];
                int c = verts[(vi + 1) % verts.Count];

                Vector2 pa = pts[a];
                Vector2 pb = pts[b];
                Vector2 pc = pts[c];

                // Check orientation
                float cross = (pb.x - pa.x) * (pc.y - pa.y) - (pb.y - pa.y) * (pc.x - pa.x);
                if (isCCW ? cross <= 0f : cross >= 0f) continue; // reflex, not an ear

                // Check no other point inside the ear
                bool hasPointInside = false;
                for (int k = 0; k < verts.Count && !hasPointInside; k++)
                {
                    int v = verts[k];
                    if (v == a || v == b || v == c) continue;
                    if (PointInTriangle(pts[v], pa, pb, pc)) hasPointInside = true;
                }
                if (hasPointInside) continue;

                // It's an ear – add triangle
                if (isCCW)
                {
                    indices.Add(a);
                    indices.Add(b);
                    indices.Add(c);
                }
                else
                {
                    indices.Add(a);
                    indices.Add(c);
                    indices.Add(b);
                }

                verts.RemoveAt(vi);
                earFound = true;
                break;
            }

            if (!earFound) break; // fallback to avoid infinite loop
        }

        // Last triangle
        if (verts.Count == 3)
        {
            if (isCCW)
            {
                indices.Add(verts[0]);
                indices.Add(verts[1]);
                indices.Add(verts[2]);
            }
            else
            {
                indices.Add(verts[0]);
                indices.Add(verts[2]);
                indices.Add(verts[1]);
            }
        }

        // Assign to mesh
        var meshVerts = pts.Select(v => (Vector3)v).ToArray();
        maskMesh.vertices  = meshVerts;
        maskMesh.triangles = indices.ToArray();
    }

    public void GenerateSpriteFromPoly()
    {
        if (awaitingReadback) return;   // Skip if a previous request hasn't returned yet
        awaitingReadback = true;
        // Combined GPU + NativeArray upload path
        EnsureTextureAndBuffer();
        UpdatePolyFromLR();

        // ---------- GPU render ----------
        if (maskMesh == null || meshDirty)
        {
            TriangulateMaskMesh();
            meshDirty = false;
        }

        if (maskRT == null)
        {
            maskRT = new RenderTexture(textureSize, textureSize, 0, RenderTextureFormat.R8);
            maskRT.filterMode = FilterMode.Point;
            maskRT.Create();
        }

        var prevRT = RenderTexture.active;

        // ----- Render spline mask into RT in pixel space -----
        Graphics.SetRenderTarget(maskRT);
        GL.PushMatrix();
        GL.LoadPixelMatrix(0, textureSize, 0, textureSize);   // 1 unit == 1 pixel

        GL.Clear(false, true, Color.clear);

        // Matrix that maps world‑space spline (‑scale/2 … +scale/2) to pixel coords (0 … textureSize)
        float pxPerWU = textureSize / scale;
        var xform = Matrix4x4.TRS(
                        new Vector3(textureSize * 0.5f, textureSize * 0.5f, 0f),  // move origin to centre
                        Quaternion.identity,
                        new Vector3(pxPerWU, pxPerWU, 1f));                       // scale world→pixel

        gpuMaskMat.SetPass(0);      // material outputs solid white
        Graphics.DrawMeshNow(maskMesh, xform);

        GL.PopMatrix();
        Graphics.SetRenderTarget(prevRT);

        // ---------- CPU texture update using SetPixelData ----------
        AsyncGPUReadback.Request(maskRT, 0, request =>
        {
            // Always release the latch when the callback fires
            try
            {
                if (request.hasError)
                {
                    Debug.LogWarning("[MapManager] GPU read-back failed – will retry next frame");
                    return;   // keep sprite as-is, but we’re free to try again
                }

                var src = request.GetData<Color32>();
                if (!pixelBuffer.IsCreated || pixelBuffer.Length != src.Length)
                {
                    if (pixelBuffer.IsCreated) pixelBuffer.Dispose();
                    pixelBuffer = new NativeArray<Color32>(src.Length, Allocator.Persistent);
                }

                src.CopyTo(pixelBuffer);
                tex.SetPixelData(pixelBuffer, 0);
                tex.Apply(false, false);

                var sprite = Sprite.Create(
                    tex, new Rect(0, 0, textureSize, textureSize),
                    new Vector2(0.5f, 0.5f),
                    textureSize / scale);

                sr.sprite = sprite;
            }
            finally
            {
                // Whether it succeeded or not, we’re no longer “waiting”
                awaitingReadback = false;
            }
        });
    }


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
        //InitializeSegmentation(); // Add this line
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
        // Capture current area before we make any structural change
        UpdatePolyFromLR();                // make sure poly is up‑to‑date
        float originalArea = PolygonArea(poly.points);

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

        // ----- apply & validate change -----
        UpdateLRFromSpline();
        UpdatePolyFromLR();
        float newArea = PolygonArea(poly.points);

        // Expansion must never shrink the playable area
        if (newArea <= originalArea + areaEpsilon)
        {
            // Undo the attempted change and exit
            UndoChange((ind, returnV));
            UpdateLRFromSpline();
            UpdatePolyFromLR();
            return (-1, returnV);   // ‑1 signals “no change was kept”
        }

        // Neighbour smoothing is only required for committed edits
        if (updateMask)
        {
            SmoothLocalAngles(ind);
            sc.Spline.SetTangentMode(TangentMode.AutoSmooth);
        }

        // Refresh visuals after smoothing
        UpdateLRFromSpline();
        UpdatePolyFromLR();

        if (updateMask)
        {
            GenerateSpriteFromPoly();
            PushBackEE();
            CheckExtras();
        }
        return (ind, returnV);
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
        Vector3[] vs = new Vector3[splineSampleCount];
        for (int i = 0; i < splineSampleCount; i++)
        {
            vs[i] = sc.Spline.EvaluatePosition(i / (float)splineSampleCount);
        }
        lr.SetPositions(vs);
        meshDirty = true;   // spline changed – rebuild GPU mesh next update
    }

    private void UpdatePolyFromLR()
    {
        // Sample the spline directly so polygon updates immediately,
        // even before the LineRenderer has refreshed.
        Vector2[] vs = new Vector2[splineSampleCount];
        for (int i = 0; i < splineSampleCount; i++)
        {
            vs[i] = (Vector2)(Vector3)sc.Spline.EvaluatePosition(i / (float)splineSampleCount);
        }
        poly.points = vs;
    }

    // === Geometry helpers =====================================================

    // Signed polygon area (positive = CCW winding, negative = CW)
    private float PolygonSignedArea(Vector2[] pts)
    {
        float a = 0f;
        for (int i = 0, j = pts.Length - 1; i < pts.Length; j = i++)
            a += (pts[j].x * pts[i].y) - (pts[i].x * pts[j].y);
        return 0.5f * a;
    }

    // Absolute polygon area
    private float PolygonArea(Vector2[] pts) => Mathf.Abs(PolygonSignedArea(pts));

    // Centroid (centre of mass) of a simple polygon
    private Vector2 PolygonCentroid(Vector2[] pts)
    {
        float signedA = PolygonSignedArea(pts);
        float cx = 0f, cy = 0f;

        for (int i = 0, j = pts.Length - 1; i < pts.Length; j = i++)
        {
            float cross = (pts[j].x * pts[i].y) - (pts[i].x * pts[j].y);
            cx += (pts[j].x + pts[i].x) * cross;
            cy += (pts[j].y + pts[i].y) * cross;
        }
        float k = 1f / (6f * signedA);
        return new Vector2(cx * k, cy * k);
    }

    /// <summary>
    /// Softens very sharp corners by pushing the two neighbouring knots
    /// slightly outward along the centroid direction, creating a gentler curve.
    /// </summary>
    private void SmoothLocalAngles(int centreIndex)
    {
        if (sc.Spline.Count < 3) return;

        int prev = GetNextIndex(centreIndex - 1);
        int next = GetNextIndex(centreIndex + 1);

        Vector2 pPrev = (Vector3)sc.Spline.Knots.ElementAt(prev).Position;
        Vector2 pCurr = (Vector3)sc.Spline.Knots.ElementAt(centreIndex).Position;
        Vector2 pNext = (Vector3)sc.Spline.Knots.ElementAt(next).Position;

        float interior = Vector2.Angle(pPrev - pCurr, pNext - pCurr);
        if (interior > minSmoothAngle) return;   // Already smooth enough

        Vector2 centroid = PolygonCentroid(poly.points);

        Vector2 dirPrev = (pPrev - centroid).normalized;
        Vector2 dirNext = (pNext - centroid).normalized;

        pPrev += dirPrev * smoothDisplacement;
        pNext += dirNext * smoothDisplacement;

        sc.Spline.SetKnot(prev,  new BezierKnot((Vector3)pPrev));
        sc.Spline.SetKnot(next,  new BezierKnot((Vector3)pNext));
    }

    private void OnDestroy()
    {
        if (tex != null)
        {
            Destroy(tex);
        }
        if (pixelBuffer.IsCreated) pixelBuffer.Dispose();
        if (maskRT != null)        maskRT.Release();
        awaitingReadback = false;
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
            if (mapchangedata.Item1 != -1)   // skip preview+undo when MapChange was rejected
            {
                UpdateLRFromSpline();
                UndoChange(mapchangedata);
            }

            vEE = ProximityData(v,2f,true).Item1;
            vprev = vEE - vprev;
            for (int i = 0; i < EE.lr.positionCount; i++)
            {
                EE.positions[i] = EE.positions[i] + (Vector3)vprev;
            }
            vprev = vEE;
            EE.transform.position = vEE;
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
    
        
     // Add these fields to MapManager class at the top with other fields:
private Dictionary<int, KnotAnimation> activeAnimations = new Dictionary<int, KnotAnimation>();
private int animationIdCounter = 0;
private Coroutine updateCoroutine;

// Helper class for tracking knot animations
private class KnotAnimation
{
    public int knotIndex;
    public Vector3 startPosition;
    public Vector3 targetPosition;
    public float progress;
    public bool isNewKnot;
    public int insertIndex;
    
    public KnotAnimation(int index, Vector3 start, Vector3 target, bool isNew = false, int insertAt = -1)
    {
        knotIndex = index;
        startPosition = start;
        targetPosition = target;
        progress = 0f;
        isNewKnot = isNew;
        insertIndex = insertAt;
    }
}

// Main async function
public void ChangeMapAsync(Vector3 pos, bool updateMask)
{
    
    // Initialize if needed
    if (activeAnimations == null)
    {
        activeAnimations = new Dictionary<int, KnotAnimation>();
    }
    
    // Calculate what would happen with a normal MapChange
    var changeData = CalculateMapChange(pos);
    
    if (changeData.isReplacement)
    {
        // Check if this knot is already being animated
        KnotAnimation existingAnim = null;
        foreach (var anim in activeAnimations.Values)
        {
            if (anim.knotIndex == changeData.knotIndex && !anim.isNewKnot)
            {
                existingAnim = anim;
                break;
            }
        }
        
        if (existingAnim != null)
        {
            // Update the target position of existing animation
            existingAnim.targetPosition = pos;
            existingAnim.progress = 0f; // Reset progress to start new interpolation
        }
        else
        {
            // Create new animation for knot replacement
            var knot = sc.Spline.Knots.ElementAt(changeData.knotIndex);
            var newAnim = new KnotAnimation(changeData.knotIndex, knot.Position, pos);
            activeAnimations[animationIdCounter++] = newAnim;
        }
    }
    else
    {
        // Handle knot insertion - more complex due to index shifting
        // First, update all animation indices that would be affected by insertion
        foreach (var anim in activeAnimations.Values)
        {
            if (!anim.isNewKnot && anim.knotIndex >= changeData.insertIndex)
            {
                anim.knotIndex++;
            }
            if (anim.isNewKnot && anim.insertIndex >= changeData.insertIndex)
            {
                anim.insertIndex++;
            }
        }
        
        // Create animation for new knot
        var nearestPoint = ProximityData(pos, 0f).Item1;
        var newAnim = new KnotAnimation(-1, nearestPoint, pos, true, changeData.insertIndex);
        activeAnimations[animationIdCounter++] = newAnim;
    }
    
    // Start update coroutine if not already running
    if (updateCoroutine == null)
    {
        updateCoroutine = StartCoroutine(UpdateAnimationsCoroutine(updateMask));
    }
}

// Helper structure for change calculation
private struct MapChangeData
{
    public bool isReplacement;
    public int knotIndex;
    public int insertIndex;
}

// Calculate what MapChange would do without actually changing anything
private MapChangeData CalculateMapChange(Vector3 EEpos)
{
    MapChangeData result = new MapChangeData();
    
    // Find three closest knots (same logic as MapChange)
    (Vector2, float)[] knots = new (Vector2, float)[sc.Spline.Count];
    int i = 0;
    float minDist = 99999f;
    int minindex = -1;
    
    foreach (BezierKnot k in sc.Spline.Knots)
    {
        knots[i] = ((Vector2)(Vector3)k.Position, ((Vector2)(EEpos - (Vector3)k.Position)).sqrMagnitude);
        if (knots[i].Item2 < minDist)
        {
            minDist = knots[i].Item2;
            minindex = i;
        }
        i++;
    }
    
    int[] indexs = new int[2] { GetNextIndex(minindex - 1), GetNextIndex(minindex + 1) };
    
    if (PointInTriangle(knots[minindex].Item1, knots[indexs[0]].Item1, EEpos, knots[indexs[1]].Item1))
    {
        result.isReplacement = true;
        result.knotIndex = minindex;
    }
    else
    {
        result.isReplacement = false;
        
        // Calculate insertion index
        SplineUtility.GetNearestPoint(sc.Spline, EEpos, out _, out float t);
        SplineUtility.GetNearestPoint(sc.Spline, (Vector3)knots[minindex].Item1, out _, out float torg);
        
        int ind = minindex;
        if (t > torg)
        {
            if (ind == 0)
            {
                ind = (t > 0.5f) ? 0 : 1;
            }
            else
            {
                ind = minindex + 1;
            }
        }
        result.insertIndex = ind;
    }
    
    return result;
}

// Coroutine that handles all active animations
private IEnumerator UpdateAnimationsCoroutine(bool updateMask)
{
    float animationDuration = 5f;
    
    while (activeAnimations.Count > 0)
    {
        float deltaTime = Time.deltaTime;
        List<int> completedAnimations = new List<int>();
        
        // First pass: insert any new knots that are ready
        foreach (var kvp in activeAnimations)
        {
            var anim = kvp.Value;
            if (anim.isNewKnot && anim.knotIndex == -1)
            {
                // Insert the knot at its start position
                sc.Spline.Insert(anim.insertIndex, new BezierKnot(anim.startPosition), TangentMode.AutoSmooth);
                anim.knotIndex = anim.insertIndex;
                
                // Update other animations' indices
                foreach (var otherAnim in activeAnimations.Values)
                {
                    if (otherAnim != anim && !otherAnim.isNewKnot && otherAnim.knotIndex >= anim.insertIndex)
                    {
                        otherAnim.knotIndex++;
                    }
                }
            }
        }
        
        // Second pass: update positions
        foreach (var kvp in activeAnimations)
        {
            var anim = kvp.Value;
            anim.progress += deltaTime / animationDuration;

            // Safety‑check – the spline may have changed unexpectedly
            if (anim.knotIndex < 0 || anim.knotIndex >= sc.Spline.Count)
            {
                // Index became invalid – abort this animation gracefully
                Debug.LogWarning($"[MapManager] Skipping animation id {kvp.Key}: invalid knot index {anim.knotIndex}");
                completedAnimations.Add(kvp.Key);
                continue;
            }

            if (anim.progress >= 1f)
            {
                // Snap to final position
                sc.Spline.SetKnot(anim.knotIndex, new BezierKnot(anim.targetPosition));
                completedAnimations.Add(kvp.Key);
            }
            else
            {
                // Interpolate position
                Vector3 currentPos = Vector3.Lerp(anim.startPosition, anim.targetPosition, anim.progress);
                sc.Spline.SetKnot(anim.knotIndex, new BezierKnot(currentPos));
            }
        }
        
        // Update tangent mode for smooth curves
        sc.Spline.SetTangentMode(TangentMode.AutoSmooth);
        
        // Update visual representations - call the existing method via reflection or make it public
        UpdateSplineVisuals();
        
        if (updateMask)
        {
            // Calculate center of all changing positions for optimized mask update
            Vector3 centerPos = Vector3.zero;
            int count = 0;
            foreach (var anim in activeAnimations.Values)
            {
                centerPos += Vector3.Lerp(anim.startPosition, anim.targetPosition, anim.progress);
                count++;
            }
            if (count > 0)
            {
                centerPos /= count;
                GenerateSpriteFromPoly();
            }
        }
        
        // Remove completed animations
        foreach (int id in completedAnimations)
        {
            activeAnimations.Remove(id);
        }
        
        yield return null;
    }
    
    // Final updates
    if (updateMask)
    {
        PushBackEE();
        CheckExtras();
        OnUpdateMap?.Invoke();
    }
    
    updateCoroutine = null;
}

// Add this public method to update spline visuals
private void UpdateSplineVisuals()
{
    Vector3[] vs = new Vector3[splineSampleCount];
    for (int i = 0; i < splineSampleCount; i++)
    {
        vs[i] = sc.Spline.EvaluatePosition(i / (float)splineSampleCount);
    }
    lr.SetPositions(vs);
    meshDirty = true;   // spline changed – force mask mesh rebuild
}
}