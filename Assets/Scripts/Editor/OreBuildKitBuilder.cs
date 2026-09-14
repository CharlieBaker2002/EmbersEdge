using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Stamps <see cref="Building.oreRequired"/> — the chips a placed building must swallow before
/// it comes alive — onto EVERY prefab that carries a Building, from the table below (by prefab
/// name) with a footprint-sized fallback for anything unlisted. Explicit overwrite, logs a table.
///   Tools/Ore Build Kit/Apply Ore Requirement Table
/// Headless: Unity -batchmode -quit -projectPath . -executeMethod OreBuildKitBuilder.Apply
/// </summary>
public static class OreBuildKitBuilder
{
    static readonly Dictionary<string, int> Table = new Dictionary<string, int>
    {
        // walls / nodes
        { "Wall", 1 }, { "Energy Wall", 2 }, { "EnergyPad", 1 }, { "EnergyPylon", 1 },
        { "LongRangePylon", 2 }, { "MultiPylon", 2 }, { "EnergyHub", 2 }, { "CapacitorNode", 1 },
        // storage
        { "Small Ember Store", 2 }, { "Ember Store", 3 }, { "Large Ember Store", 4 }, { "TinyStore", 1 },
        // generation
        { "Small Ember Generator", 3 }, { "Ember Generator", 4 }, { "Crush Generator", 30 }, { "Solo Generator", 2 },
        { "Blue Generator", 3 }, { "White Generator", 3 }, { "Solar Generator", 3 },
        { "Soul Generator", 3 }, { "Large Soul Generator", 5 },
        // defence
        { "SwordTurret", 3 }, { "PelterTurret", 3 }, { "Push Tower", 3 }, { "Mine Sprayer", 3 },
        { "EmitterTurret", 3 }, { "Base Turret", 3 }, { "ClawBot Factory", 5 },
        { "Fighter Ship Factory", 5 }, { "Drill Forge", 4 }, { "Force Field", 4 },
        { "Cargoloft", 4 }, { "DroneDock", 4 },
        // infrastructure
        { "Constructor", 4 }, { "Refiner", 5 }, { "EmberCannon", 4 }, { "Chip Charger", 3 },
        { "Chip Factory", 5 }, { "Cell", 4 }, { "Collector", 3 }, { "Tube", 2 }, { "Belt", 1 }, { "Healing Platform", 3 }, { "Old Research Facility", 4 }, { "ConverterNew", 3 },
        // granted / never built by hand
        { "Vessel", 0 }, { "Expander", 0 }, { "Telepad", 0 },
    };

    [MenuItem("Tools/Ore Build Kit/Apply Ore Requirement Table")]
    public static void Apply()
    {
        var guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs", "Assets/Resources" });
        var rows = new List<string>();
        int stamped = 0;
        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null) continue;
            var b = go.GetComponentInChildren<Building>(true);
            if (b == null) continue;
            int value = Table.TryGetValue(go.name, out int v) ? v : Fallback(b);
            var so = new SerializedObject(b);
            var prop = so.FindProperty("oreRequired");
            if (prop == null) continue;
            if (prop.intValue != value)
            {
                prop.intValue = value;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(b);
                stamped++;
            }
            rows.Add($"{go.name,-28} {value,2} {(Table.ContainsKey(go.name) ? "" : "(fallback)")}  {path}");
        }
        AssetDatabase.SaveAssets();
        rows.Sort();
        Debug.Log($"[OreBuildKit] oreRequired stamped on {stamped} prefab(s) ({rows.Count} buildings):\n" + string.Join("\n", rows));
    }

    /// <summary>Unlisted buildings pay by footprint: 2 chips per cell, 1..8.</summary>
    static int Fallback(Building b)
        => Mathf.Clamp(Mathf.RoundToInt(Mathf.Max(0.25f, b.size.x) * Mathf.Max(0.25f, b.size.y) * 2f), 1, 8);
}
