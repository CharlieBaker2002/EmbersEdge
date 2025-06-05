using System.Collections;
using UnityEngine;

public class SwordTower : Building
{
    [SerializeField] GameObject[] swordSprites;
    private bool upgraded = false;
    [SerializeField] Animator anim;
    private float refresh;
    
    private int rot = 0; //0-3 incl
    [SerializeField] private GameObject baseSR;
    [SerializeField] private Sprite morphSprite;
    [SerializeField] private Sprite tileSprite;

    [SerializeField] ProjectileScript swordPrefab;
    [SerializeField] private Rotator rotator;

    private int ammo = 4;
    bool waitingForAmmo = false;
    bool waitingForAmmoUpgrade = false;
    
    private static readonly int Make = Animator.StringToHash("Make");
    private static readonly int MakeDiagonal = Animator.StringToHash("MakeDiagonal");
    private static readonly int Ammo = Animator.StringToHash("Ammo");
    private Collider2D[] cols = new Collider2D[10];

    public override void Start()
    {
        base.Start();
        transform.parent.GetComponent<SpriteRenderer>().color = Color.white;
        AddSlot(new int[] {100, 0, 2,0}, "Sword Turret Upgrade", tileSprite, true, LevelUp, true, Morph);
    }

    protected override void BEnable()
    {
        StartCoroutine(AttackLoop());
        StartCoroutine(BuildLoop());
        waitingForAmmo = false;
        waitingForAmmoUpgrade = false;
    }

    private IEnumerator AttackLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(1f);
            foreach (Transform t in GS.FindEnemies(tag, transform.position, 7f, false, false, cols))
            {
                if(t== null) continue;
                var sword = FindNearestSword(t);
                if (sword == null)
                {
                    yield return new WaitForSeconds(1f);
                    break;
                }
                GS.NewP(swordPrefab.gameObject, sword.transform, tag,sword.transform.position - transform.position).GetComponent<Seeking>().target = t;
                sword.SetActive(false);
                yield return new WaitForSeconds(upgraded ? 0.4f : 2f);
            }
        }
    }

    private IEnumerator BuildLoop()
    {
        while (true)
        {
            rotator.offset = -90f * rot;
            if (upgraded && ammo>0)
            {
                if (!swordSprites[4 + rot].activeSelf)
                {
                    ammo--;
                    anim.SetBool(MakeDiagonal,true);
                    anim.SetBool(Ammo,ammo > 0);
                }
                yield return new WaitForSeconds(upgraded ? 2.5f : 5f);
            }
            if (!swordSprites[rot].activeSelf && ammo>0)
            {
                ammo--;
                anim.SetBool(Make,true);
                anim.SetBool(Ammo,ammo > 0);
            }
            yield return new WaitForSeconds(upgraded ? 2.5f : 5f);
            if (ammo <= 0 && !waitingForAmmo)
            {
                waitingForAmmo = true;
                ResourceManager.instance.NewTask(transform.parent.gameObject, new int[] { 0, 0, 1, 0 }, delegate
                {
                    anim.SetBool(Ammo, true);
                    ammo += 4;
                    waitingForAmmo = false;
                });
            }
            if (upgraded && ammo < 4 && !waitingForAmmoUpgrade)
            {
                waitingForAmmoUpgrade = true;
                ResourceManager.instance.NewTask(transform.parent.gameObject, new int[] { 0, 0, 1, 0 }, delegate
                {
                    anim.SetBool(Ammo, true);
                    ammo += 4;
                    waitingForAmmoUpgrade = false;
                });
            }
            rot = rot.Cycle(1, 3);
        }
    }

    private GameObject FindNearestSword(Transform t)
    {
        float dist = Mathf.Infinity;
        GameObject sword = null;
        foreach (GameObject g in swordSprites)
        {
            if(!g.activeSelf) continue;
            float d = (g.transform.position - t.position).sqrMagnitude;
            if(d < dist)
            {
                dist = d;
                sword = g;
            }
        }
        return sword;
    }

    public void CreateSword()
    {
        swordSprites[rot].SetActive(true);
        anim.SetBool(Make,false);
    }

    public void CreateDiagonalSword()
    {
        swordSprites[4+rot].SetActive(true);
        anim.SetBool(MakeDiagonal,false);
    }
    
    void LevelUp()
    {
        upgraded = true;
        ammo = 8;
    }

    void Morph()
    {
        GS.QuickMorphWithOrbs(baseSR,morphSprite,transform.parent);
    }
 
}
