using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Dematerial : MonoBehaviour
{
   public static List<Dematerial> dematerials = new List<Dematerial>();
   [SerializeField] private SpriteRenderer sr;
   private float timer;
   private float startTime;

   [SerializeField] private Sprite[] sprs;
   
   const float lifeTime = 20f;

   private float buf;
   private void OnEnable()
   {
      dematerials.Add(this);
      startTime = Time.time;
   }

   private IEnumerator Start()
   {
      Vector3 pos = GS.RandCircleV2(1f, 1.5f);
      for(float t = 1f; t > 0f; t -= Time.deltaTime)
      {
         transform.Translate(pos * Time.deltaTime * t);
         yield return null;
      }
   }

   private void Update()
   {
      buf = 1 - (Time.time - startTime) / lifeTime;
      timer += Time.deltaTime * buf;
      transform.localScale = (buf + 0.2f) * Vector3.one;
      
      if (timer > 0.33334f)
      {
         timer -= 0.33334f;
      }
      sr.sprite = GS.PercentParameter(sprs, 3f * timer);
      
      if (!(Time.time - startTime > lifeTime)) return;
      dematerials.Remove(this);
      Destroy(gameObject);
   }

   //And then do whatever tf you want w em
   public static List<SpriteRenderer> GetDematerials(Vector2 pos, float dist)
   {
      List<SpriteRenderer> res = new List<SpriteRenderer>();
      for (int i = 0; i < dematerials.Count; i++)
      {
         if (Vector2.Distance(dematerials[i].transform.position, pos) < dist)
         {
            Dematerial d = dematerials[i];
            res.Add(d.sr);
            Destroy(d);
            dematerials.RemoveAt(i);
            i--;
         }
      }
      return res;
   }
}
