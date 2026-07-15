using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// TEMPORARY diagnostic for the "placed buildings stay purple / never receive orbs" bug.
/// Self-installs on play (editor only). Every 3s — and instantly on F9 — dumps one [OrbFlow]
/// block showing each link of the delivery chain:
///   pool counters -> registered task magnets -> each pylon's view of them -> physical rings.
/// Read the dump top to bottom; the first line that looks wrong IS the broken link.
/// Delete this file when done.
/// </summary>
public class OrbFlowDebug : MonoBehaviour
{
#if UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        var go = new GameObject("~OrbFlowDebug");
        DontDestroyOnLoad(go);
        go.AddComponent<OrbFlowDebug>();
    }
#endif

    float t;

    void Update()
    {
        t -= Time.unscaledDeltaTime;
        bool force = Keyboard.current != null && Keyboard.current.f9Key.wasPressedThisFrame;
        if (t > 0f && !force) return;
        t = 3f;
        Dump(force);
    }

    void Dump(bool forced)
    {
        var rm = ResourceManager.instance;
        var sb = new StringBuilder();
        sb.AppendLine($"[OrbFlow]{(forced ? " (F9)" : "")} t={Time.time:0.0} timeScale={Time.timeScale}");
        if (rm == null) { sb.AppendLine("  ResourceManager.instance is NULL"); Debug.Log(sb.ToString()); return; }

        sb.AppendLine($"  pool orbs=[{string.Join(",", rm.orbs)}] caps=[{string.Join(",", rm.orbCaps)}] held=[{string.Join(",", rm.held)}]");
        sb.AppendLine($"  allOrbs={(OrbManager.allOrbs == null ? -1 : OrbManager.allOrbs.Count)} OrbScript.tot={OrbScript.tot} refundPending={BM.PlacementRefundPending}");

        // Every registered magnet, task magnets called out with their building + fill state
        int tasks = 0;
        sb.AppendLine($"  registered magnets={rm.magnets.Count}:");
        foreach (var m in rm.magnets)
        {
            if (m == null) { sb.AppendLine("    (destroyed entry)"); continue; }
            string owner = m.gameObject.name;
            string flags = $"{(m.enabled ? "" : " DISABLED")}{(m.gameObject.activeInHierarchy ? "" : " GO-INACTIVE")}";
            sb.AppendLine($"    {m.typ} type={m.orbType} n={m.n} ring={m.orbs.Count} transient={m.transientOrbs} cap={m.capacity} on '{owner}'{flags}");
            if (m.typ == OrbMagnet.OrbType.Task) tasks++;
        }
        if (tasks == 0) sb.AppendLine("    (no TASK magnets registered — placed buildings aren't registering)");
        // Each pylon: is its coroutine-facing list seeing the tasks, does it physically hold orbs
        sb.AppendLine($"  pylons={rm.pylons.Count}:");
        foreach (var p in rm.pylons)
        {
            if (p == null || p.mag == null) { sb.AppendLine("    (null pylon/mag)"); continue; }
            sb.AppendLine($"    pylon type={p.orbType} ring={p.mag.orbs.Count}/{p.mag.capacity} n={p.mag.n} official={p.magnetsOfficial.Count} clonePending={p.cloneNecessary} demand={p.mag.demand:0.0}"
                + $"{(p.enabled ? "" : " DISABLED")}{(p.gameObject.activeInHierarchy ? "" : " GO-INACTIVE")}");
        }
        Debug.Log(sb.ToString());
    }
}
