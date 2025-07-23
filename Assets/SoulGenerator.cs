using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SoulGenerator : Building, IOnDeath
{
   [SerializeField] Battery b;
   [SerializeField] Sprite[] sprs;
   [SerializeField] private SpriteRenderer[] arms;
   [SerializeField] private Sprite offSpr;
   [SerializeField] private GameObject FX;
   public static List<SoulGenerator> gs;
   private Dictionary<Collider2D, SoulCollectOnDeath> map = new();
   public float range = 2f;
   [SerializeField] private SpriteRenderer blanksr;
   private System.Action act;
   public bool busy;

   public void Animate()
   {
      arms[0].LeanAnimateFPS(sprs, 1, true);
      arms[1].LeanAnimateFPS(sprs, 1, true);
      this.QA(Generate,sprs.Length*0.5f / 12f);
   }

   public void Activate()
   {
      arms[0].sprite = sprs[0];
      arms[1].sprite = sprs[0];
   }
   
   private void Generate()
   {
      Instantiate(FX, transform.position, Quaternion.identity, GS.FindParent(GS.Parent.fx));
      b.energy++;
   }

   public override void Start()
   {
      act = () =>
      {
         this.QA(() =>
         {
            arms[0].sprite = offSpr;
            arms[1].sprite = offSpr;
         },1.5f);
      };
      base.Start();
   }

   protected override void BEnable()
   {
      gs.Add(this);
      SpawnManager.instance.onWaveComplete += act;
   }
   
   protected override void BDisable()
   {
      gs.Remove(this);
      arms[0].sprite = offSpr;
      arms[1].sprite = offSpr;
      SpawnManager.instance.onWaveComplete -= act;
   }

   public void Collect(Transform tran)
   {
      List<SpriteRenderer> srs = new List<SpriteRenderer>();
      foreach (SpriteRenderer s in tran.GetComponentsInChildren<SpriteRenderer>())
      {
         srs.Add(Instantiate(blanksr, s.transform.position, s.transform.rotation, transform));
         srs[^1].transform.localScale = s.transform.localScale;
         srs[^1].material = s.material;
         srs[^1].color = Color.Lerp(s.color, Color.black, 0.5f);
      }

      foreach (SpriteRenderer s in srs)
      {
         s.transform.LeanMoveLocal(Vector3.zero, 1f).setEaseOutQuart();
      }

      StartCoroutine(CollectSoul(srs));
      Animate();

      IEnumerator CollectSoul(List<SpriteRenderer> ss)
      {
         for(float t = 1f; t > 0f; t -= Time.deltaTime)
         {
            for (int i = 0; i < ss.Count; i++)
            {
               ss[i].transform.localScale = t*Vector3.one;
               ss[i].color = new Color(ss[0].color.r, ss[0].color.g, ss[0].color.b, t);
            }
            yield return null;
         }
         for (int i = 0; i < ss.Count; i++)
         {
            Destroy(ss[i].gameObject);
         }
      }
   }
}