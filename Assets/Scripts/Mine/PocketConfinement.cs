using UnityEngine;

/// <summary>
/// Traps the player inside a core/boss pocket the moment it's discovered: a base-style boundary that
/// snaps in and pushes the player back if they cross it. Mirrors MapManager's boundary push (GS.AS +
/// AddPush toward centre) and uses the build-safe code-based MapManager.PointInPoly inside-test rather
/// than a collider query. Fades out (disables) when the pocket is cleared.
/// </summary>
public class PocketConfinement : MonoBehaviour
{
    Vector2[] poly;
    Vector2 center;
    bool active;
    LineRenderer ring;

    public void Begin(Vector2[] worldPoly, Vector2 centerP)
    {
        poly = worldPoly;
        center = centerP;
        active = true;
        BuildRing();
    }

    public void End()
    {
        active = false;
        if (ring != null) ring.enabled = false;
    }

    void FixedUpdate()
    {
        if (!active || poly == null) return;
        ActionScript AS = GS.AS;
        if (AS == null) return;
        Vector2 pos = AS.transform.position;
        if (!MapManager.PointInPoly(pos, poly))
            AS.AddPush(0.3f * Time.fixedDeltaTime, false, center - pos); // push back toward the arena centre
    }

    void BuildRing()
    {
        ring = GetComponent<LineRenderer>();
        if (ring == null) ring = gameObject.AddComponent<LineRenderer>();
        ring.useWorldSpace = true;
        ring.loop = true;
        ring.widthMultiplier = 0.12f;
        ring.numCornerVertices = 2;
        // Sprites/Default is already an Always-Included shader in this project (used by MapManager).
        ring.material = new Material(Shader.Find("Sprites/Default"));
        Color c = new Color(0.95f, 0.55f, 0.22f, 0.9f); // ember accent — on-palette
        ring.startColor = ring.endColor = c;
        ring.sortingOrder = 50;
        ring.positionCount = poly.Length;
        for (int k = 0; k < poly.Length; k++)
            ring.SetPosition(k, new Vector3(poly[k].x, poly[k].y, 0f));
        ring.enabled = true;
    }
}
