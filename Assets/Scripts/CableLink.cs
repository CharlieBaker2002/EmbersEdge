using System;
using UnityEngine;

/// <summary>
/// Lives on a finished pylon cable (the LineRenderer GameObject). Makes the cable
/// clickable: selecting it pops a single "Delete Cable" action tile (reusing the building
/// action-tile UI at <see cref="BM.UIspots"/>[0]); confirming runs the delete callback the
/// owning <see cref="EnergyPylon"/> supplied — which tears the connection down and retracts
/// the cable back into the pylon.
///
/// Implements <see cref="ISelectable"/> so FocusRouter's selection drives deselect for free:
/// clicking elsewhere or pressing Esc clears the tile. <see cref="CloseActive"/> lets
/// UIManager.CloseAllUIs tear the tile down too, so opening a building UI never leaves an
/// orphaned "Delete Cable" tile on screen.
/// </summary>
public class CableLink : MonoBehaviour, IClickable, ISelectable
{
    private Action onDelete;
    private Sprite icon;

    // Only one cable tile is ever shown at a time; tracked statically so CloseAllUIs (and a
    // freshly-selected cable) can tear down whichever one is currently up.
    private static CableLink activeTile;
    private BaseTile tile;

    /// <summary>Wire the cable for clicking. a/b are the cable endpoints in world space.</summary>
    public void Init(Action deleteCallback, Sprite tileIcon, Vector3 aWorld, Vector3 bWorld, float clickRadius)
    {
        onDelete = deleteCallback;
        icon = tileIcon;

        int layer = LayerMask.NameToLayer("Ally Buildings");
        if (layer >= 0) gameObject.layer = layer;

        var edge = GetComponent<EdgeCollider2D>();
        if (edge == null) edge = gameObject.AddComponent<EdgeCollider2D>();
        edge.edgeRadius = clickRadius;
        edge.points = new[]
        {
            (Vector2)transform.InverseTransformPoint(aWorld),
            (Vector2)transform.InverseTransformPoint(bWorld),
        };
        edge.enabled = true;
    }

    // FocusRouter auto-selects ISelectables on click, so the tile is driven by
    // OnSelected/OnDeselected — OnClick itself has nothing to do.
    public void OnClick() { }

    public void OnSelected()
    {
        // Close any open building UI (and any prior cable tile) before showing ours.
        UIManager.CloseAllUIs();
        ShowTile();
    }

    public void OnDeselected() => HideTile();

    void ShowTile()
    {
        HideTile();
        if (UIManager.i == null || UIManager.i.baseTile == null || BM.i == null
            || BM.i.UIspots == null || BM.i.UIspots.Length == 0) return;

        activeTile = this;
        tile = Instantiate(UIManager.i.baseTile, UIManager.i.buildingsUI).GetComponent<BaseTile>();
        tile.transform.position = BM.i.UIspots[0].position;
        // Zero cost → BaseTile.OnClick runs the action immediately (no orb task).
        tile.Init(gameObject, new int[4], "Delete Cable", icon, true, () =>
        {
            onDelete?.Invoke();
            FocusRouter.i?.Deselect(this);
        });
        tile.gameObject.SetActive(true);
    }

    void HideTile()
    {
        if (activeTile == this) activeTile = null;
        if (tile != null)
        {
            Destroy(tile.gameObject);
            tile = null;
        }
    }

    /// <summary>Tear down whichever cable tile is currently showing — called by UIManager.CloseAllUIs.</summary>
    public static void CloseActive()
    {
        if (activeTile != null) activeTile.HideTile();
    }

    void OnDisable() => HideTile();

    void OnDestroy()
    {
        HideTile();
        FocusRouter.i?.Deselect(this);
    }
}
