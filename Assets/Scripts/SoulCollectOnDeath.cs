using UnityEngine;

/// <summary>
/// Added to enemies (see <c>EmbersEdge.SpawnEnemy</c> and <c>SoulGenerator.BEnable</c>). On death it
/// drops a <see cref="Soul"/> at the corpse — but only if a non-full <see cref="SoulGenerator"/> is in
/// range, so souls never spawn where nothing can eat them. A hungry generator then claims and syphons it.
/// </summary>
public class SoulCollectOnDeath : MonoBehaviour, IOnDeath
{
    public void OnDeath()
    {
        Vector3 p = transform.position;
        foreach (SoulGenerator s in SoulGenerator.gs)
        {
            if (s == null || s.Full) continue;
            if (Vector2.SqrMagnitude(s.transform.position - p) < s.range * s.range)
            {
                Soul.Spawn(p);
                return;                      // one soul per corpse
            }
        }
    }
}
