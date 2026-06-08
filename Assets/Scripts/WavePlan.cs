using System.Collections.Generic;

// Pre-rolled description of an upcoming base wave. Built when the player returns
// from the dungeon (SpawnManager.ArmWave), previewed by Directors while peaceful,
// then executed verbatim when the wave is summoned. This is the seam the future
// procedural authoring GUI plugs into: it only needs to produce CorePlans.
[System.Serializable]
public struct PlannedSpawn
{
    public MarauderSO so;
    // Spline-t offset RELATIVE to the owning core's boundary t at plan time, wrapped
    // into (-0.5, 0.5]. Resolved against the core's CURRENT t at spawn time so the
    // formation follows the core if it moves. ("relative x along the boundary")
    public float tOffset;
    // Absolute spawn time in seconds measured from the end of the warm-up (start of the spawn phase).
    // Acco accumulates real elapsed time and fires each enemy when its time arrives, so the schedule
    // doesn't drift the way chained WaitForSeconds would. plannedSpawns are kept sorted by this.
    public float time;

    public PlannedSpawn(MarauderSO so, float tOffset, float time)
    {
        this.so = so;
        this.tOffset = tOffset;
        this.time = time;
    }
}

public class CorePlan
{
    public EmbersEdge core;
    public List<PlannedSpawn> spawns = new List<PlannedSpawn>();

    public CorePlan(EmbersEdge core)
    {
        this.core = core;
    }
}

public class WavePlan
{
    public float activity;
    public List<CorePlan> cores = new List<CorePlan>();
}
