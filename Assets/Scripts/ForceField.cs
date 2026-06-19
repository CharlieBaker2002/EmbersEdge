using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Force Field tower. On build it weaves an EnergyWall between two endpoints and then
/// trickle-charges it from the grid at <see cref="chargeRate"/> energy/sec; each energy is worth
/// <see cref="hpPerCharge"/> wall hp. The wall's MAX hp scales with its width (span) — a wider field
/// is worth more hp but costs proportionally more energy to keep charged. The wall IS the charge
/// store: damage to it is charge lost. If the wall falls entirely, the tower re-weaves a fresh one
/// once it has banked 1 energy again.
///
/// Shaping ("Move Field" mode): click the tower to arm, then left-click-and-DRAG out in the world to
/// draw where the boundary goes (a live ghost shows the result, clamped to reach and to width
/// <see cref="minWidth"/>..<see cref="maxWidth"/>). Release commits; Esc / a stray click cancels.
/// Re-weaving takes <see cref="reshapeTime"/> seconds during which the wall is down (collider off,
/// flickering), so reshaping mid-fight is a real commitment.
/// </summary>
public class ForceField : Building
{
    [Header("Force Field")]
    [SerializeField] private Sprite[] chargeSprs;     // gem fill by stored charge, empty -> full
    [SerializeField] private GameObject wallPrefab;
    [SerializeField] private Connectable connectable; // kept for the prefab wiring; no longer used to aim
    [SerializeField] private float chargeRate = 0.25f;
    [SerializeField] private float hpPerCharge = 10f;
    [SerializeField] private float maxReach = 4.5f;
    [SerializeField] private float reshapeTime = 2.5f;

    [Header("Field width (span) -> hp scales with it")]
    [SerializeField] private float minWidth = 2f;
    [SerializeField] private float maxWidth = 4f;

    [SerializeField] private Vector2 endA = new Vector2(-1.2f, 1.4f); // local offsets from the tower
    [SerializeField] private Vector2 endB = new Vector2(1.2f, 1.4f);

    private EnergyWall wall;
    private Coroutine loop;
    private float rebuildCharge;

    // ---- Move Field mode ----
    private bool moveMode, armed, dragging;
    private Vector2 dragStart;
    private LineRenderer reachLR, ghostLR;
    private TextMeshPro moveText;
    private Action<InputAction.CallbackContext> pressDel, releaseDel;
    private Action escDel;

    public override void Start()
    {
        base.Start();
    }

    protected override void BEnable()
    {
        if (wall == null) SpawnWall(2f); // a thin seed wall comes up with the tower
        if (loop == null) loop = StartCoroutine(Run());
    }

    protected override void BDisable()
    {
        ExitMoveMode(false);
        if (loop != null) { StopCoroutine(loop); loop = null; }
        if (wall != null) { Destroy(wall.gameObject); wall = null; }
        rebuildCharge = 0f;
        ClearEnergyStatusImmediate();
    }

    public override void OnClick()
    {
        // FocusRouter calls this on press. Clicking the tower arms Move Field mode; the actual
        // placement is a separate left-click-drag (so there's no awkward press-and-drag timing).
        if (!enabled) return;
        if (!moveMode) EnterMoveMode();
    }

    // Wider field = LESS hp: the same energy is spread thinner, so a narrow wall is tanky and a wide
    // one is fragile. width minWidth -> max hp, width maxWidth -> min hp.
    private float MaxHpForWidth(float width)
        => hpPerCharge * ((minWidth + maxWidth) - Mathf.Clamp(width, minWidth, maxWidth));

    private void SpawnWall(float hp)
    {
        var g = Instantiate(wallPrefab, transform.position, Quaternion.identity, transform);
        wall = g.GetComponent<EnergyWall>();
        float width = Mathf.Clamp(Vector2.Distance(World(endA), World(endB)), minWidth, maxWidth);
        wall.Init(World(endA), World(endB), hp, MaxHpForWidth(width));
    }

    private Vector2 World(Vector2 local) => (Vector2)transform.position + local;

    // ---- trickle charge: charge tracks the wall's (width-scaled) max hp ----
    private IEnumerator Run()
    {
        while (true)
        {
            if (wall == null)
            {
                // Wall fell: bank charge from zero, re-weave once one full energy is stored.
                float step = DrawStep(chargeRate, 1f - rebuildCharge);
                rebuildCharge += step;
                ReportEnergyDraw(chargeRate);
                SetChargeSprite(rebuildCharge);
                if (rebuildCharge >= 1f - 1e-4f)
                {
                    SpawnWall(rebuildCharge * hpPerCharge);
                    rebuildCharge = 0f;
                }
            }
            else
            {
                float deficit = wall.MaxHp - wall.Hp;
                if (deficit > 0.01f && Power.Energy > 1e-3f)
                {
                    float drawn = DrawStep(chargeRate, deficit / hpPerCharge);
                    if (drawn > 0f) wall.Heal(drawn * hpPerCharge);
                    ReportEnergyDraw(chargeRate);
                }
                else if (deficit > 0.01f)
                {
                    ReportEnergyDraw(chargeRate); // wants charge, grid is dry -> overlay
                }
                else
                {
                    ClearEnergyStatus();
                }
                SetChargeSprite(wall.MaxHp > 0f ? wall.Hp / wall.MaxHp : 0f);
            }
            yield return null;
        }
    }

    private void SetChargeSprite(float frac)
    {
        if (chargeSprs == null || chargeSprs.Length == 0 || sr == null) return;
        sr.sprite = GS.PercentParameter(chargeSprs, Mathf.Clamp01(frac));
    }

    /// <summary>Draws up to maxRate/sec this frame, capped by grid rate, stored energy, and remaining capacity.</summary>
    private float DrawStep(float maxRate, float remaining)
    {
        if (remaining <= 1e-5f) return 0f;
        float step = Mathf.Min(maxRate * Time.deltaTime, Power.DrawRate * Time.deltaTime, Power.Energy, remaining);
        return (step > 0f && Power.Use(step)) ? step : 0f;
    }

    // =========================================================================================
    //  Move Field mode
    // =========================================================================================

    private void EnterMoveMode()
    {
        if (moveMode) return;
        moveMode = true;
        armed = false;
        dragging = false;

        FocusRouter.Suppressed = true;                 // own the cursor while aiming
        if (UIParent != null && UIParent.activeInHierarchy) OnClose?.Invoke();

        if (IM.i != null && IM.i.pi != null)
        {
            pressDel = _ => OnPress();
            releaseDel = _ => OnRelease();
            IM.i.pi.Player.Interact.started += pressDel;
            IM.i.pi.Player.Interact.canceled += releaseDel;
        }
        escDel = () => ExitMoveMode(false);
        EscapeRouter.i?.Push(escDel);

        BuildPreview();
    }

    private void ExitMoveMode(bool committed)
    {
        if (!moveMode) return;
        moveMode = false;
        dragging = false;
        armed = false;

        FocusRouter.Suppressed = false;
        if (IM.i != null && IM.i.pi != null)
        {
            if (pressDel != null) IM.i.pi.Player.Interact.started -= pressDel;
            if (releaseDel != null) IM.i.pi.Player.Interact.canceled -= releaseDel;
        }
        pressDel = null;
        releaseDel = null;
        if (escDel != null) { EscapeRouter.i?.Remove(escDel); escDel = null; }

        DestroyPreview();
    }

    private void OnPress()
    {
        if (!moveMode || !armed) return;        // the opening click hasn't been released yet
        dragging = true;
        dragStart = Cursor();
    }

    private void OnRelease()
    {
        if (!moveMode) return;
        if (!armed) { armed = true; return; }   // release of the tower-click arms the drag
        if (!dragging) return;
        dragging = false;

        Vector2 p0 = dragStart, p1 = Cursor();
        if ((p1 - p0).magnitude < 0.15f) { ExitMoveMode(false); return; }   // a click, not a drag -> cancel

        ComputeEndpoints(p0, p1, out Vector2 na, out Vector2 nb);
        PlaceWall(na, nb);
        ExitMoveMode(true);
    }

    private Vector2 Cursor() => IM.controller ? (Vector2)IM.i.CWorldPoint() : IM.i.MousePosition();

    // Drag a->b defines the field directly: clamp the span to [minWidth, maxWidth] and keep the whole
    // wall within reach of the tower.
    private void ComputeEndpoints(Vector2 p0, Vector2 p1, out Vector2 a, out Vector2 b)
    {
        Vector2 dir = p1 - p0;
        float len = Mathf.Clamp(dir.magnitude, minWidth, maxWidth);
        Vector2 dn = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.right;

        Vector2 tower = transform.position;
        Vector2 c = (p0 + p1) * 0.5f;
        float maxC = Mathf.Max(0f, maxReach - len * 0.5f);
        Vector2 oc = c - tower;
        if (oc.magnitude > maxC) c = tower + (oc.sqrMagnitude > 1e-4f ? oc.normalized : Vector2.up) * maxC;

        a = c - dn * len * 0.5f;
        b = c + dn * len * 0.5f;
    }

    private void PlaceWall(Vector2 na, Vector2 nb)
    {
        endA = na - (Vector2)transform.position;
        endB = nb - (Vector2)transform.position;
        float width = Mathf.Clamp(Vector2.Distance(na, nb), minWidth, maxWidth);
        if (wall != null)
        {
            wall.SetMaxHp(MaxHpForWidth(width));
            wall.Reweave(na, nb, reshapeTime);
        }
    }

    private void Update()
    {
        if (!moveMode) return;
        Vector2 cur = Cursor();
        if (dragging)
        {
            ComputeEndpoints(dragStart, cur, out Vector2 na, out Vector2 nb);
            UpdateGhost(na, nb, true);
            UpdateText(na, nb, true);
        }
        else
        {
            UpdateGhost(cur, cur, false);
            UpdateText(cur, cur, false);
        }
    }

    // ---- preview visuals ----

    private void BuildPreview()
    {
        reachLR = MakeLR(0.05f, new Color(0.55f, 0.95f, 1f, 0.22f), 4);
        DrawReach();
        ghostLR = MakeLR(0.16f, new Color(0.6f, 1f, 0.95f, 0.85f), 6);
        ghostLR.enabled = false;

        if (UIManager.i != null && UIManager.i.numText != null)
        {
            moveText = Instantiate(UIManager.i.numText, transform.position, Quaternion.identity);
            moveText.gameObject.SetActive(true);
            moveText.alignment = TextAlignmentOptions.Center;
            moveText.fontSize = Mathf.Max(2f, moveText.fontSize * 0.7f);
            moveText.color = new Color(0.75f, 1f, 0.95f, 0.95f);
            moveText.transform.localScale = Vector3.one * 0.6f;
            var tr = moveText.GetComponent<Renderer>();
            if (tr != null) { tr.sortingLayerName = "Power Ups"; tr.sortingOrder = 8; }
        }
    }

    private void DestroyPreview()
    {
        if (reachLR != null) Destroy(reachLR.gameObject);
        if (ghostLR != null) Destroy(ghostLR.gameObject);
        if (moveText != null) Destroy(moveText.gameObject);
        reachLR = ghostLR = null;
        moveText = null;
    }

    private LineRenderer MakeLR(float width, Color col, int order)
    {
        var go = new GameObject("FieldPreview");
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.widthMultiplier = width;
        lr.numCapVertices = 4;
        lr.numCornerVertices = 4;
        lr.textureMode = LineTextureMode.Stretch;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = lr.endColor = col;
        lr.sortingLayerName = "Power Ups";
        lr.sortingOrder = order;
        return lr;
    }

    private void DrawReach()
    {
        if (reachLR == null) return;
        const int n = 48;
        reachLR.loop = true;
        reachLR.positionCount = n;
        Vector2 t = transform.position;
        for (int i = 0; i < n; i++)
        {
            float ang = i / (float)n * Mathf.PI * 2f;
            reachLR.SetPosition(i, t + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * maxReach);
        }
    }

    private void UpdateGhost(Vector2 na, Vector2 nb, bool show)
    {
        if (ghostLR == null) return;
        ghostLR.enabled = show;
        if (!show) return;
        const int n = 20;
        var pts = EnergyWall.BuildBezier(na, nb, (Vector2)transform.position, n, out _);
        ghostLR.positionCount = n;
        ghostLR.SetPositions(pts);
        float width = Vector2.Distance(na, nb);
        Color c = width <= minWidth + 0.05f
            ? new Color(1f, 0.82f, 0.45f, 0.85f)     // at the minimum span -> amber
            : new Color(0.6f, 1f, 0.95f, 0.9f);
        ghostLR.startColor = ghostLR.endColor = c;
    }

    private void UpdateText(Vector2 na, Vector2 nb, bool dragingNow)
    {
        if (moveText == null) return;
        if (dragingNow)
        {
            float w = Vector2.Distance(na, nb);
            moveText.text = $"Width {w:0.0}   HP {MaxHpForWidth(w):0}";
            moveText.transform.position = (na + nb) * 0.5f + Vector2.up * 0.45f;
        }
        else
        {
            moveText.text = "Drag to place the Force Field\n(Esc to cancel)";
            moveText.transform.position = (Vector2)transform.position + Vector2.up * 0.95f;
        }
    }
}
