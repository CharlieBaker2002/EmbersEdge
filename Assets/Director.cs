using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class Director : MonoBehaviour
{
   [SerializeField] Image img;
   [SerializeField] private TextMeshProUGUI tmp;
   public RectTransform rt;
   public List<Transform> ts;

   public void Update()
   {
      LookTowardsCentreOfEnemies();
      tmp.text = ts.Count.ToString();
   }

   //place on UIManager.i.canvas, scale by distance to nearest enemy (0.25 at 20+ units - 1)
   public void LookTowardsCentreOfEnemies()
   {
      
   }

   public void Set(List<GameObject> enemies, Sprite spr)
   {
      img.sprite = spr;
      ts = new List<Transform>();
      foreach (GameObject enemy in enemies)
      {
         ts.Add(enemy.transform);
      }
   }
}
