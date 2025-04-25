using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class E1_0Die : MonoBehaviour
{
    [SerializeField] private Transform[] spawnPoints;
    [SerializeField] private ProjectileScript proj;

   public void Shoot()
   {
       foreach(Transform t in spawnPoints)
       {
           var p = Instantiate(proj, t.position, t.rotation, GS.FindParent(GS.Parent.enemyprojectiles));
           p.SetValues(t.position - transform.position, tag);
       }
   }
}
