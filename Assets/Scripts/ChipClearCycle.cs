using UnityEngine;

/// <summary>
/// The two clear cycles that eat loose ore chips: the teleport HOME (DUNGEON debris only —
/// base chip never fades on a teleport in either direction, user rule 2026-09-13) and a WAVE
/// clearing (base-side chips when DroneManager.clearBaseChipsOnWaveClear). Chips vanish with OreChip.FadeOut — a
/// tiny lift-flash-shrink, lightly staggered so a pile shimmers away rather than blinking.
/// Spared: a chip already inside a consumer's intake ring (the building's own suction owns it —
/// a wall's ring stock, a construction site's delivery) and everything on a Tube shelf,
/// whose cluster starts its SHIELD instead (TubeCluster.BeginShield: paid, it keeps the
/// ore; dry, it loses it). Wired from DroneManager's teleport / wave-complete hooks.
/// </summary>
public static class ChipClearCycle
{
    /// <summary>When the last wave-clear wipe ran (Time.time; -∞ before any). The wipe fires the
    /// instant a wave clears (SpawnManager.NextDayFR); producers that spawn chip on the new day
    /// (the Cell) wait until its fade is over — see <see cref="WaveClearSettledAt"/>.</summary>
    public static float LastWaveClearTime { get; private set; } = float.NegativeInfinity;

    /// <summary>The moment the last wave-clear wipe is fully gone (fade + the widest stagger).</summary>
    public static float WaveClearSettledAt => LastWaveClearTime + DroneManager.ChipFadeSeconds + 0.3f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => LastWaveClearTime = float.NegativeInfinity;   // no-domain-reload: statics survive play-stop

    /// <summary>Teleport home: the dungeon's loose debris fades; base chip is untouched (so
    /// the shelves' shield doesn't fire either — nothing threatens them).</summary>
    public static void OnReturnHome() => Run(dungeon: true, baseSide: false);

    /// <summary>The wave has cleared — THIS frame (no delay): base-side loose chips fade.</summary>
    public static void OnWaveClear()
    {
        LastWaveClearTime = Time.time;
        Run(dungeon: false, baseSide: DroneManager.ClearBaseChipsOnWaveClear);
    }

    static void Run(bool dungeon, bool baseSide)
    {
        float fade = DroneManager.ChipFadeSeconds;
        for (int k = OreChip.all.Count - 1; k >= 0; k--)
        {
            var chip = OreChip.all[k];
            if (chip == null || chip.Absorbing || chip.Fading || chip.Stored) continue;
            bool inDungeon = chip.transform.InDungeon();
            if (inDungeon ? !dungeon : !baseSide) continue;
            if (!inDungeon && ChipConsumers.AtAnIntake(chip)) continue;   // a building's intake has it
            chip.FadeOut(fade, Random.Range(0f, 0.3f));
        }
        if (baseSide) Tube.BeginShieldAll();
    }
}
