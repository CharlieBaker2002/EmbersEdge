using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ChildRotator : MonoBehaviour
{
   [SerializeField] private Transform follow;

   private void Update()
   {
      foreach (Transform child in transform)
      {
         child.transform.rotation = follow.transform.rotation;
      }
   }
}
