using UnityEngine;

public class SpreadTower : Building
{
    private static readonly int Attack = Animator.StringToHash("Attack");
    private static readonly int Level = Animator.StringToHash("Level");
    [SerializeField] Finder find;
    [Range(1, 3)] float mode;
    int level = 1;
    [SerializeField] Animator anim;
    [SerializeField] GameObject proj;
    float strength = 1;
  //  float inaccuracy;
    float spread;
    [SerializeField] Transform[] sp;
    [SerializeField] Sprite[] spr;
    [SerializeField] Sprite[] tileSprites;
    [SerializeField] private SpriteRenderer bar;
    [SerializeField] Sprite morphSprite;
    [SerializeField] private Sprite baseUpgradeSprite;
    float energyCost = 0.05f;
    [SerializeField] private Battery b;
    
    void SetMode(float x)
    {
        mode = x;
        find.refresh = (4 - level) * 0.25f * (1 + mode) + 0.25f;
        strength = level * mode;
        find.radius = (level + 0.5f) * (mode + 0.5f) + 0.5f;
       // inaccuracy = Mathf.Lerp(0.5f, 0f, (3f * level - 4f + mode) / 5f);
        spread = (((4 - mode) - level + 1) / 3.2f) + 0.0625f; 
        if(level == 2) { spread *= 0.66f; spread += 0.1f; }
        sr.sprite = spr[(int)((level - 1) * 3 + mode - 1)];
    }

    void LevelUp()
    {
        level = 2;
        anim.SetFloat(Level, 1);
        SetMode(mode);
        anim.speed = 0;
        sr.sprite = spr[(int)((level - 1) * 3 + mode - 1)];
        anim.speed = 1;
        find.refresh /= 2f;
        energyCost = 0.1f;
        transform.parent.GetComponent<SpriteRenderer>().sprite = baseUpgradeSprite;
        
    }
    
    protected override void BEnable()
    {
        find.engaged = true;
    }

    protected override void BDisable()
    {
        find.engaged = false;
    }

    public override void Start()
    {
        base.Start();
        bar.enabled = true;
        SetMode(1);
        AddSlot(new int[] { 0, 0, 0, 0 }, "Change Mode", tileSprites[0], false, SwapMode);
        AddUpgradeSlot(new int[] { 55, 0, 3, 0 }, "Spread Tower Upgrade", tileSprites[1], true, LevelUp,7,true,StartMorph);
        transform.parent.GetComponent<SpriteRenderer>().color = Color.white;
        find.OnFound += StartAttack;
    }

    private void StartMorph()
    {
        GS.QuickMorphWithOrbs(gameObject,morphSprite,transform.parent);
    }

    public override void OnDeath()
    {
        transform.parent.GetComponent<SpriteRenderer>().color = new Color(200, 200, 200, 1);
        base.OnDeath();
    }

    private void StartAttack(Transform t)
    {
        if(!enabled) return;
        if(b.energy < energyCost)
        {
            return;
        }
        this.TurnTowards(t, 0.35f, 5f * level);
        anim.SetBool(Attack, true);
    }

    private void SwapMode()
    {
        mode += 1;
        if(mode == 4)
        {
            mode = 1;
        }
        SetMode(mode);
    }

    public void Shoot()
    {
        int i = 0;
        for (float ang = -60f; ang <= 60f; ang+= 20f)
        {
            GS.NewP(proj, sp[i], tag, GS.Rotated(transform.up, -ang * spread), 0.05f, strength);
            i++;
        }
        b.Use(energyCost);
    }
}
