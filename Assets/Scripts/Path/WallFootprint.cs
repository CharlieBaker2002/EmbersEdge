using UnityEngine;

/// <summary>
/// Fallback wall registration for wall-like objects that DON'T go through the Building lifecycle
/// (Building.isWall handles those). Drop on any object with a LifeScript (or point <see cref="ls"/>
/// at one): its collider bounds register as chewable wall cells while enabled. Dead walls read as
/// open automatically (live-wall lookup); destruction/disable unregisters.
/// </summary>
public class WallFootprint : MonoBehaviour
{
    [Tooltip("The HP that prices chewing through. Auto-found in children if left empty.")]
    public LifeScript ls;

    void OnEnable()
    {
        if (ls == null) ls = GetComponentInChildren<LifeScript>();
        if (ls == null || !PathZone.AtBase(transform.position)) return;
        var col = ls.GetComponent<Collider2D>() != null ? ls.GetComponent<Collider2D>() : GetComponentInChildren<Collider2D>();
        Bounds b = col != null ? col.bounds : new Bounds(transform.position, Vector3.one);
        Vector2Int min = BaseBlockMap.Cell(b.min);
        Vector2Int max = BaseBlockMap.Cell(b.max);
        BaseBlockMap.RegisterWallRect(ls, min, new Vector2Int(max.x - min.x + 1, max.y - min.y + 1));
    }

    void OnDisable() => BaseBlockMap.UnregisterWall(ls);
    void OnDestroy() => BaseBlockMap.UnregisterWall(ls);
}
