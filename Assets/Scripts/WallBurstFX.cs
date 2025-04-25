using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WallBurstFX : MonoBehaviour
{
   [SerializeField] float speed = 1f;
   [SerializeField] private SpriteRenderer sr;
   [SerializeField] private Sprite[] sprs;
   [SerializeField] private BoxCollider2D col;
   
   private IEnumerator Start()
   {
      for (float x = 0f; x < 1f; x += Time.deltaTime * speed)
      {
         sr.sprite = GS.PercentParameter(sprs, x);
         float y = 0.1f + Mathf.Sin(Mathf.PI * x);
         col.size = new Vector2(0.0937f, y * 0.175f);
         col.offset = new Vector2(0f, y * 0.3f);
         yield return null;
      }
      Destroy(gameObject);
   }
}