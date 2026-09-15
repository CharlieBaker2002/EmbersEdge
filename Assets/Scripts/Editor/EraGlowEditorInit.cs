using UnityEditor;

/// <summary>Keeps the global era colour set in EDIT mode — an unset global is black, so every
/// Glow material would render invisible in the scene view — and snaps it back to era 0 when
/// play mode ends (play may have left it on a later era).</summary>
[InitializeOnLoad]
static class EraGlowEditorInit
{
    static EraGlowEditorInit()
    {
        EditorApplication.delayCall += () => EraGlow.Apply(0);   // after the asset database is ready
        EditorApplication.playModeStateChanged += s =>
        {
            if (s == PlayModeStateChange.EnteredEditMode) EraGlow.Apply(0);
        };
    }
}
