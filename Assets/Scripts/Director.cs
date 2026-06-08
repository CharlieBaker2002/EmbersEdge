using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UIElements;
using Image = UnityEngine.UI.Image;

public class Director : MonoBehaviour
{
   [SerializeField] Image img;
   [SerializeField] private Image[] imgs;
   [SerializeField] private TextMeshProUGUI tmp;
   public RectTransform rt;
   public List<Transform> ts;

   // Markers used by EnemyTracker so we don’t reuse a Director that is
   // already representing a cluster this frame.
   [HideInInspector] public bool inUse = false;
   // Pre-wave preview Directors keep showing even when their cluster is on-screen (so the wave UI
   // never blinks out as you walk up to a spawn point). Live combat Directors leave this false.
   [HideInInspector] public bool alwaysShow = false;
   // Pre-wave preview only: a canvas offset so overlapping enemy-type Directors fan out instead of
   // stacking on top of each other (which hid types entirely). Set by EnemyTracker.ResolvePreviewOverlaps.
   [HideInInspector] public Vector2 previewOffset;
   // This Director's intended canvas position BEFORE previewOffset — read by EnemyTracker to detect overlaps.
   [HideInInspector] public Vector2 basePos;
   private Camera cam;
   public static float maxdistance = 12.5f;

   private void Start()
   {
       cam = CameraScript.i.cam;
   }

   public void SetVisuals(bool off)
   {
       if(img.enabled == off) return;
       img.enabled = off;
       tmp.enabled = off;
       imgs[0].enabled = off;
       imgs[1].enabled = off;
   }

   public void Update()
   {
      if (ts == null || ts.Count == 0)
      {
         // No cluster to represent (e.g. recycled but not reused) — never leave a stray "0" Director up,
         // even in alwaysShow preview mode.
         if (gameObject.activeSelf) gameObject.SetActive(false);
         return;
      }
      bool visible = ClusterOnScreen();
      if (!alwaysShow && visible)
      {
         // Hide when not needed and exit early (live combat radar behaviour)
         if (gameObject.activeSelf) gameObject.SetActive(false);
         return;
      }
      LookTowardsCentreOfEnemies(visible);
      SetVisuals(true);
      if (tmp) tmp.text = ts.Count.ToString();
   }

   //place on UIManager.i.canvas, scale by distance to nearest enemy (0.25 at 20+ units - 1)
   void LookTowardsCentreOfEnemies(bool clusterVisible = false)
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
       // Off-screen: clamp to the viewport edge so the arrow rides the border. On-screen (only the
       // alwaysShow preview Directors get here) hug the EXACT spawn position instead of clamping.
       Vector3 vp = cam.WorldToViewportPoint(centre);
       if (!clusterVisible)
       {
           vp.x = Mathf.Clamp(vp.x, 0.05f, 0.95f);
           vp.y = Mathf.Clamp(vp.y, 0.05f, 0.95f);
       }

       // Convert viewport → screen‑space pixel position
       Vector2 screenPoint = new Vector2(vp.x * Screen.width, vp.y * Screen.height);

       // Convert screen‑space → canvas local position
       Vector2 localPoint;
       RectTransformUtility.ScreenPointToLocalPointInRectangle(
           (RectTransform)rt.parent, screenPoint, null, out localPoint);
       basePos = localPoint; // intended position pre-offset, so EnemyTracker can resolve overlaps
       rt.anchoredPosition = localPoint + (alwaysShow ? previewOffset : Vector2.zero);
       
       // Preview Directors (alwaysShow) ALWAYS point along the attack vector — from the spawn point
       // toward the base centre (0,0) — whether on- or off-screen. Live radar Directors instead point
       // from the screen edge toward their cluster.
       Vector3 dir = alwaysShow
           ? Vector3.zero - centre
           : centre - cam.ScreenToWorldPoint(transform.position);

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
       // Preview Directors never grow past their grounded ("locked") size — they may shrink with distance
       // but never balloon, on- or off-screen.
       if (alwaysShow) scale = Mathf.Min(scale, 1f);
       rt.localScale = Vector3.one * scale;
   }

   bool ClusterOnScreen()
   {
       if (ts == null || ts.Count == 0) return true;
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
