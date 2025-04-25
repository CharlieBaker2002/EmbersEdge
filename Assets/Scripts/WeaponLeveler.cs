using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WeaponLeveler : MonoBehaviour
{
    private WeaponScript w;
    public string[] names;
    public Sprite[] sprites;
    public int[][] costs;
    public float[] woptions = new float[] { 0, 0, 0 };
    public WeaponScript.SpecialAttack[] specials = new WeaponScript.SpecialAttack[] { new WeaponScript.SpecialAttack(false, false), new WeaponScript.SpecialAttack(false, false), new WeaponScript.SpecialAttack(false, false) };
    [Header("n | regSpr | fail | msBtw | randSpr | msStart | x | y | strength | randStrength")]
    [Header("ammoPerClip | totalAmmo | max reload | attack reset")]

    [Header("Upgrade 1")]
    [SerializeField]
    GameObject[] projectiles1 = new GameObject[3];
    [SerializeField]
    int[] p1modifier1 = new int[10];
    [SerializeField]
    int[] p2modifier1 = new int[10];
    [SerializeField]
    int[] p3modifier1 = new int[10];
    [SerializeField]
    int[] cost1 = new int[4];
    [SerializeField]
    float[] a1 = new float[] { };
    [SerializeField]private float knockback1 = 1f;

    [Header("Upgrade 2")]
    [SerializeField]
    GameObject[] projectiles2 = new GameObject[3];
    [SerializeField]
    int[] p1modifier2 = new int[10];
    [SerializeField]
    int[] p2modifier2 = new int[10];
    [SerializeField]
    int[] p3modifier2 = new int[10];
    [SerializeField]
    int[] cost2 = new int[4];
    [SerializeField]
    float[] a2 = new float[] { };
   [SerializeField] private float knockback2 = 1f;

    [Header("Upgrade 3")]
    [SerializeField]
    GameObject[] projectile3 = new GameObject[3];
    [SerializeField]
    int[] p1modifier3 = new int[10];
    [SerializeField]
    int[] p2modifier3 = new int[10];
    [SerializeField]
    int[] p3modifier3 = new int[10];
    [SerializeField]
    int[] cost3 = new int[4];
    [SerializeField]
    float[] a3 = new float[] {  };
    [SerializeField] private float knockback3 = 1f;

    float[][] attributes;
    private GameObject[][] projectiles;
    private int[][] p1;
    private int[][] p2;
    private int[][] p3;

    private void Awake()
    {
        w = GetComponent<WeaponScript>();
        projectiles = new GameObject[][] { projectiles1, projectiles2, projectile3 };
        p1 = new int[][] { p1modifier1, p1modifier2, p1modifier3 };
        p2 = new int[][] { p2modifier1, p2modifier2, p2modifier3 };
        p3 = new int[][] { p3modifier1, p3modifier2, p3modifier3 };
        costs = new int[][] { cost1, cost2, cost3 };
        attributes = new float[][] { a1, a2, a3 };
    }

    public void LevelUp(int level) //0,1,2
    {
        Awake();
        var knockbacks = new float[] { knockback1, knockback2, knockback3 };
        w.knockback = knockbacks[level];
        w.option = woptions[level];
        w.sp = specials[level];
        for (int i = 0; i < 3; i++)
        {
            if (w.projectiles[i] != null)
            {
                Destroy(w.projectiles[i]);
            }
            if (projectiles[level][i] != null)
            {
                w.projectiles[i] = Instantiate(projectiles[level][i], transform.position, transform.rotation, transform);
                w.projectiles[i].SetActive(false);
            }
            else
            {
                w.projectiles[i] = null;
            }
        }
        w.p1modifier = p1[level];
        w.p2modifier = p2[level];
        w.p3modifier = p3[level];
        if(attributes[level].Length > 0) //if empty list, no changes
        {
            w.ammoPerClip = (int)attributes[level][0];
            w.totalAmmo = (int)attributes[level][1];
            w.maxReload = attributes[level][2];
            w.attackReset = attributes[level][3];
        }
        w.totalAmmo = w.maximumAmmo;
        w.ammoInClip = w.ammoPerClip;
        w.GetComponent<SpriteRenderer>().sprite = sprites[level];
        name = names[level];
        Destroy(this);
    }
}
