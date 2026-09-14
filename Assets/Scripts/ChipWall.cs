using UnityEngine;

/// <summary>
/// The Wall. Raised from ore like every building (GhostIntake), and a DESTROYED wall rebuilds
/// through the generic ore rebuild (Building.OnDeath → GhostIntake.BeginRebuild at
/// BM.rebuildOreFraction of its cost). LIVE damage is medic work: repair drones heal a standing
/// wall between rounds like any other building (user rule 2026-09-14 — "just a drone is good
/// enough for healing"; the chip-fed repair intake this class used to run, which also kept
/// medics off walls, is gone). The class keeps its name for the prefab's sake.
/// </summary>
public class ChipWall : Building
{
}
