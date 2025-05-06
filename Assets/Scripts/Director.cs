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

   // Markers used by EnemyTracker so we don’t reuse a Director that is
   // already representing a cluster this frame.
   [HideInInspector] public bool inUse = false;
   private Camera cam;
   public static float maxdistance = 12.5f;

   private void Start()
   {
       cam = CameraScript.i.cam;
   }

   public void Update()
   {
      if (ClusterOnScreen())
      {
         // Hide when not needed and exit early
         if (gameObject.activeSelf) gameObject.SetActive(false);
         return;
      }
      LookTowardsCentreOfEnemies();
      if (tmp) tmp.text = ts != null ? ts.Count.ToString() : "0";
      if (!gameObject.activeSelf)
      {
         gameObject.SetActive(true);
      }
   }

   //place on UIManager.i.canvas, scale by distance to nearest enemy (0.25 at 20+ units - 1)
   void LookTowardsCentreOfEnemies()
   {
       if (ts == null || ts.Count == 0) return;

       // Calculate the centre of the cluster
       Vector3 centre = Vector3.zero;
       foreach (var t in ts)
       {
           if (t) centre += t.position;
       }
       centre /= ts.Count;

       Vector3 playerPos = CharacterScript.CS.transform.position;
       // Clamp the centre inside the viewport
       Vector3 vp = cam.WorldToViewportPoint(centre);
       vp.x = Mathf.Clamp(vp.x, 0.05f, 0.95f);
       vp.y = Mathf.Clamp(vp.y, 0.05f, 0.95f);

       // Convert viewport → screen‑space pixel position
       Vector2 screenPoint = new Vector2(vp.x * Screen.width, vp.y * Screen.height);

       // Convert screen‑space → canvas local position
       Vector2 localPoint;
       RectTransformUtility.ScreenPointToLocalPointInRectangle(
           (RectTransform)rt.parent, screenPoint, null, out localPoint);
       rt.anchoredPosition = localPoint;
       
       Vector3 dir       = centre - cam.ScreenToWorldPoint(transform.position);

       // Rotate the arrow so it faces the cluster
       float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
       rt.rotation = Quaternion.Euler(0f, 0f, angle - 90f);
       // keep the label readable
       if (tmp) tmp.rectTransform.rotation = Quaternion.identity;

       // Position the director on the edge of the screen



       // Scale from 1 (close) down to 0.25 (≥20 units away)
       float nearest = float.MaxValue;
       foreach (var t in ts)
       {
           if (!t) continue;
           float d = Vector3.Distance(t.position, playerPos);
           if (d < nearest) nearest = d;
       }
       float scale = Mathf.Lerp(1.5f, 0.5f, Mathf.InverseLerp(0f,maxdistance, nearest));
       rt.localScale = Vector3.one * scale;
   }
   
   

   bool ClusterOnScreen()
   {
       if (ts == null || ts.Count == 0) return true;
       Camera cam = Camera.main;
       foreach (var t in ts)
       {
           if (!t) continue;
           Vector3 vp = cam.WorldToViewportPoint(t.position);
           const float MARGIN = 0.05f;   // give a 5 % border
           if (vp.z < 0f ||
               vp.x < MARGIN || vp.x > 1f - MARGIN ||
               vp.y < MARGIN || vp.y > 1f - MARGIN)
               return false;             // at least one member is off‑screen
       }
       return true;            // every member is visible
   }

   public void Set(List<Transform> enemies, Sprite spr)
   {
      img.sprite = spr;
      ts = enemies;
   }
}
