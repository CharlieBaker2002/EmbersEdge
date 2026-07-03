using UnityEngine;
using UnityEngine.InputSystem;

// Firestorm: hold to whip a swirling storm of fire up around yourself (which ROOTS you once
// you've gripped it past ~0.35s), release to unleash a trailing firestorm aura + a stim.
//   - tap       -> a long, mild aura (~4s) + a mild, long stim + chip damage.
//   - full hold -> a short (~1.5s) but ferocious aura, up to 50% wider, + a huge stim and
//                  heavy damage.
// All visuals/feel live on FirestormCore (built entirely in code on the unlit-red glow
// material with the SignatureFX sprite sheet) — only the material is wired here. Magnitudes
// (radius / damage / stim) grow 50% per spell level. See nova-ability-vfx.
public class FirestormSpell : Spell
{
    [Tooltip("Deployed Firestorm effect prefab (Prefabs/AbilityProj/Firestorm) — feel authored on its FirestormCore.")]
    [SerializeField] private FirestormCore firestormPrefab;
    [SerializeField] private Material particleMat;

    // Per-level { tap, full } numbers, selected by spell level (index level-1). The core
    // interpolates tap→full with ease-in-cubic on the charge. (Radius still scales 50%/level
    // inside the core.)
    static readonly float[][] DPS_LVL     = { new[] { 1f, 1.5f }, new[] { 4f, 6f },   new[] { 10f, 15f }  };
    static readonly float[][] DOT_LVL     = { new[] { 2f, 6f },   new[] { 4f, 12f },  new[] { 10f, 25f }  };
    // stim as a PERCENT haste (100% = act-rate ×2 = 5 movement "wheels"); → value2 multiplier below.
    static readonly float[][] STIMPCT_LVL = { new[] { 10f, 40f }, new[] { 25f, 80f }, new[] { 50f, 120f } };

    private FirestormCore active;
    private float atr;

    private int x = 0;

    public override void Started(InputAction.CallbackContext ctx)
    {
        base.Started(ctx);
        active = Instantiate(firestormPrefab, CharacterScript.CS.transform.position, Quaternion.identity,
                             GS.FindParent(GS.Parent.fx));

        int li = Mathf.Clamp(level, 1, 3) - 1;                 // per-level number row (radius still scales in the core)
        active.Begin(particleMat, level, atr, tag,
                     DPS_LVL[li][0], DPS_LVL[li][1],
                     DOT_LVL[li][0], DOT_LVL[li][1],
                     1f + STIMPCT_LVL[li][0] * 0.01f, 1f + STIMPCT_LVL[li][1] * 0.01f); // stim% → act-rate multiplier
    }

    public override void Performed(InputAction.CallbackContext ctx)
    {
        base.Performed(ctx);
        if (active != null)
        {
            active.Release((float)ctx.duration);
            active = null;
        }
    }

    public override Vector2 GetManaAndCd()
    {
        return new Vector2(2 + level, 6 - level);
    }

    public override void LevelUp()
    {
        x++;
        if (x > 2) x = 0; else return;
        if (level < 3) level++;
    }

    public override void Intellect(float a)
    {
        atr = a;
    }

    public override void StopPart(MechaSuit m)
    {
        base.StopPart(m);
        if (active != null) Destroy(active.gameObject);
    }
}
