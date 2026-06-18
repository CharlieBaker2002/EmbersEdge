using UnityEngine;
using UnityEngine.InputSystem;

// Nova: hold to gather a single-element (red) singularity at the cursor, release
// to unleash a supernova — decent damage in the centre, a tiny bit out to a much
// bigger rim. The longer it is charged the bigger and hotter the blast; level 3
// adds a delayed echo. All visuals/effect live in NovaCore, built entirely in code
// (no second prefab) — only the unlit sprite material is wired below.
public class NovaSpell : Spell
{
    [Tooltip("The deployed Nova effect prefab (Prefabs/AbilityProj/Nova) — all its feel/VFX values are authored on its NovaCore.")]
    [SerializeField] private NovaCore novaPrefab;
    [SerializeField] private Material novaParticleMat;
    [SerializeField] private float maxRange = 4.5f;
    [SerializeField] private float baseRadius = 3.5f;
    [SerializeField] private float baseDamage = 6f;

    private NovaCore active;
    private float atr;

    public override void Started(InputAction.CallbackContext ctx)
    {
        base.Started(ctx);
        // spawn the authored Nova prefab at the player; Begin() snaps it to the cursor
        active = Instantiate(novaPrefab, CharacterScript.CS.transform.position, Quaternion.identity,
                             GS.FindParent(GS.Parent.fx));

        float radius = baseRadius * (1f + 0.5f * (level - 1)); // size grows 50% per level
        int li = Mathf.Clamp(level, 1, 3) - 1;
        float coreDmg = new[] { 8f, 22f, 50f }[li];   // full-charge core (centre) damage per level
        float chanDps = new[] { 4f, 7f, 12f }[li]; // channel DPS at full charge per level
        // NovaCore applies the charge curve, centre→rim falloff and intellect scaling.
        active.Begin(novaParticleMat, level, atr, tag, radius, coreDmg, chanDps, maxRange);
    }

    public override void Performed(InputAction.CallbackContext ctx)
    {
        base.Performed(ctx);
        if (active != null)
        {
            active.Detonate((float)ctx.duration);
            active = null;
        }
        LevelUp();
    }

    public override Vector2 GetManaAndCd()
    {
        return new Vector2(3 + level, 4 - 2 * level);
    }

    public override void LevelUp()
    {
        if (level < 3) level++;
    }

    public override void Intellect(float a)
    {
        atr = a;
    }
}
