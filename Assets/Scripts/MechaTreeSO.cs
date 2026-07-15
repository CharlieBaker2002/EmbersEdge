using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// The authored mecha-suit tech tree. Author it in Tools > Mecha Tree (drag MechanismSOs or Part
/// prefabs in, drag lines between nodes); MechaTreeUI reads it at runtime (N opens the tree).
/// Lives at Resources/MechaTree so runtime code can load it without any scene wiring.
///
/// Dependency model: every edge into a node belongs to an AND-group. The node is reachable when
/// ALL edges of ANY single group are unlocked — i.e. requirements are an OR of AND-groups
/// ((A AND B) OR C). Group 0 edges on a fresh node = a plain "everything required" AND.
/// </summary>
public class MechaTreeSO : ScriptableObject
{
    [Serializable]
    public class Node
    {
        public int id;
        public MechanismSO mech;
        public Vector2 pos;      // graph-space position authored in the editor window
        public int maxLevel = 1; // >1 = clicking the unlocked node again upgrades it, up to this
        public Sprite iconOverride; // optional; otherwise the Part prefab's own sprite is shown
    }

    [Serializable]
    public class Dep
    {
        public int from;  // prerequisite node id
        public int to;    // dependent node id
        public int group; // AND-group index within the target node
    }

    public List<Node> nodes = new List<Node>();
    public List<Dep> deps = new List<Dep>();
    public int nextId = 1;

    // Shared AND-group palette so the editor lines and the in-game lines speak the same colours.
    public static readonly Color[] GroupColors =
    {
        new Color(0.95f, 0.75f, 0.25f), // amber
        new Color(0.35f, 0.75f, 0.95f), // sky
        new Color(0.55f, 0.9f,  0.4f),  // green
        new Color(0.9f,  0.45f, 0.75f), // pink
        new Color(0.6f,  0.55f, 0.95f), // violet
        new Color(0.95f, 0.5f,  0.35f), // ember
    };

    public static Color GroupColor(int g) => GroupColors[Mathf.Abs(g) % GroupColors.Length];

    public Node ById(int id) => nodes.Find(n => n.id == id);

    public List<Dep> DepsInto(int id) => deps.FindAll(d => d.to == id);

    public List<int> GroupsInto(int id)
    {
        var gs = new List<int>();
        foreach (Dep d in deps)
        {
            if (d.to == id && !gs.Contains(d.group)) gs.Add(d.group);
        }
        gs.Sort();
        return gs;
    }

    /// <summary>True when some AND-group of the node's incoming edges is fully unlocked (no edges = root, always true).</summary>
    public bool Satisfied(int id, Func<int, bool> unlocked)
    {
        var into = DepsInto(id);
        if (into.Count == 0) return true;
        foreach (int g in GroupsInto(id))
        {
            bool all = true;
            foreach (Dep d in into)
            {
                if (d.group == g && !unlocked(d.from)) { all = false; break; }
            }
            if (all) return true;
        }
        return false;
    }

    /// <summary>Human-readable requirement expression, e.g. "(Jet AND Wheel) OR Copter".</summary>
    public string RequirementText(int id)
    {
        var into = DepsInto(id);
        if (into.Count == 0) return "None";
        var groups = GroupsInto(id);
        var parts = new List<string>();
        foreach (int g in groups)
        {
            var names = into.Where(d => d.group == g)
                            .Select(d => ById(d.from)?.mech != null ? ById(d.from).mech.name : "?")
                            .ToList();
            string s = string.Join(" AND ", names);
            parts.Add(names.Count > 1 && groups.Count > 1 ? "(" + s + ")" : s);
        }
        return string.Join(" OR ", parts);
    }

    /// <summary>Node icon: override if set, else the Part prefab's own SpriteRenderer sprite, else the blueprint sprite.</summary>
    public static Sprite IconOf(Node n)
    {
        if (n == null || n.mech == null) return null;
        if (n.iconOverride != null) return n.iconOverride;
        if (n.mech.p != null && n.mech.p.sr != null && n.mech.p.sr.sprite != null) return n.mech.p.sr.sprite;
        return n.mech.s;
    }

    /// <summary>Tint the icon the way the prefab renders it (overrides display untinted).</summary>
    public static Color IconTint(Node n)
    {
        if (n == null || n.mech == null || n.iconOverride != null) return Color.white;
        if (n.mech.p != null && n.mech.p.sr != null && n.mech.p.sr.sprite != null) return n.mech.p.sr.color;
        return Color.white;
    }

    /// <summary>Boosts and standalones never live in the tree — they're granted by other systems.</summary>
    public static bool AllowedInTree(MechanismSO m)
    {
        return m != null && m.p != null &&
               m.p.taip != Part.PartType.Boost && m.p.taip != Part.PartType.Standalone;
    }
}
