using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TurretScript : Building
{
    private static readonly int Trigger = Animator.StringToHash("Trigger");
    private static readonly int Level = Animator.StringToHash("Level");
    public GameObject[] pPrefabs;
    public Sprite[] baseSprites;
    public float resetT = 3f;
    private float timer = 0f;
    public float dRadius = 3f; //detection rad
    private Transform T; // target
    private Animator anim;
    public float turniness = 0.035f;
    private GS.Parent s;
    private float waitT = 0f; //stops spamming of expensive op. FindNearestEnemy()
    public float inaccuracy = 0.1f;
    public Transform shootPoint;
    private int level = 0;
    private int ammo = 20;
    [SerializeField]
    private Sprite[] upgradeSprites;

    [SerializeField] private GameObject upgradeGizmo;
    
    private bool aimAssist = false;

    private void Awake()
    {
        s = GS.ProjParent(transform);
        anim = GetComponent<Animator>();
    }

    public override void Start()
    {
        base.Start();
        this.QA(() => transform.parent.GetComponent<SpriteRenderer>().sprite = baseSprites[0],1f);
        AddSlot(new int[] { 45, 10, 0, 0 }, "Medium Turret", upgradeSprites[0], true, LevelUp,true,MorphUpgrade );
        AddSlot(new int[] { 100, 0, 5, 0 }, "Mega Turret", upgradeSprites[1], true, LevelUp, true,MorphUpgrade , null, CanShow);
        AddSlot(new int[] {0,0,0,1}, "Aim Assist", upgradeSprites[2], true, delegate { aimAssist = true; upgradeGizmo.SetActive(true); }, true);
    }

    private void MorphUpgrade()
    {
        GS.QuickMorphWithOrbs(transform.parent.gameObject, baseSprites[level+1]);
    }

    void Update()
    {
        if (ammo == 0)
        {
            anim.speed = 0;
        }
        else
        {
            anim.speed = 1;
        }
        timer -= Time.deltaTime;
        waitT -= Time.deltaTime;
        if (waitT <= 0f || T == null)
        {
             T = GS.FindNearestEnemy(tag, transform.position, dRadius,false);
            waitT = resetT;
            if(T == null)
            {
                waitT = resetT/5;
            }
            return;
        }
        if(Vector2.Distance(T.position, transform.position) > dRadius)
        {
            T = null;
            return;
        }

        if (!aimAssist)
        {
            transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.Euler(0f, 0f, -Vector2.SignedAngle(T.position - transform.position, Vector2.up)), turniness * Time.deltaTime * 40f);
        }
        else
        {
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.Euler(0f, 0f, -Vector2.SignedAngle(T.position - transform.position, Vector2.up)), Time.deltaTime * turniness * 25f * 180f / Mathf.PI);
        }
        if (timer <= 0f)
        {
            timer = resetT;
            anim.SetBool(Trigger, true);
        }
    }

    public void Shoot() //called from animation
    {
        if(ammo <= 0)
        {
            return;
        }
        var p = Instantiate(pPrefabs[level], shootPoint.position, transform.rotation, GS.FindParent(s));
        p.GetComponent<ProjectileScript>().SetValues((Vector2) transform.up + Random.insideUnitCircle * inaccuracy, tag);
        p.GetComponent<ProjectileScript>().speed *= 1 + level * 0.35f;
        AmmoCheck();
    }

    private void AmmoCheck()
    {
        ammo--;
        if(ammo == 10 * (1 + level))
        {
            ResourceManager.instance.NewTask(transform.parent.gameObject, new int[] { (1 + level) * 2, 0, 0, 0 }, delegate { ammo += 1 + (1 + level) * 10; },false);
        }
    }

    private bool CanShow()
    {
        if(level == 1)
        {
            return true;
        }
        return false;
    }

    public void SetBoolFalse()
    {
        anim.SetBool(Trigger, false);
    }

  

    public void LevelUp()
    {
        level++;
        anim.SetInteger(Level, level);
        transform.parent.GetComponent<SpriteRenderer>().sprite = baseSprites[level];
        resetT -= 0.5f;
        dRadius *= 1.25f;
        ammo = Mathf.Max(ammo, 10);
        turniness *= 1.6f;
        inaccuracy /= 2f;
        physic.maxHp += 12; //16,28,40
        physic.Change(physic.maxHp,1);
        shootPoint.transform.localScale += new Vector3(0.25f, 0.25f, 0);
        if (level == 1)
        {
            canOpen = true;
        }
    }
}
