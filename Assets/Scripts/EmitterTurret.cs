using System;
using System.Collections.Generic;
using System.Collections;
using UnityEngine;

public class EmitterTurret : Building
{
    
    //this script creates a turret that shoots at enemies by first collection up to 8 projectiles
    //that spin around the turret. When enemies are close enough, it is activated, it shoots the projectiles at enemies
    // It uses a Finder script to handle the finding of enemies.

    [SerializeField] AttackDrone prefab;
    private List<AttackDrone> projectiles = new List<AttackDrone>();
    float timer;
    private float refreshDuration = 4f;
    private float resources;
    private float maxResources = 4f;
    private int maxDrones = 6;
    private float attackTimer = 0.25f;

    public static Dictionary<Transform, float> targets = new Dictionary<Transform, float>();

    [SerializeField] Transform spinner;

    [SerializeField] private Finder find;
    public override void Start()
    {
        var transform1 = transform;
        prefab = Instantiate(prefab, transform1.position, Quaternion.identity, transform1);
        prefab.gameObject.SetActive(false);
        base.Start();
        find.OnFound += StartAttack;
        resources = maxResources;
    }

    private void StartAttack(Transform t)
    {
        if (projectiles.Count == 0)
        {
            return;
        }
        if (targets.ContainsKey(t))
        {
            if (targets[t] > 0f)
            {
                StartCoroutine(Attack(t));
            }
            return;
        }
        var oLS = t.GetComponentInChildren<LifeScript>();
        if (oLS != null)
        {
            targets.Add(t,oLS.hp);
            var dt = oLS.gameObject.AddComponent<DroneTracker>();
            physic.onDeaths.Add(dt);
            StartCoroutine(Attack(t));
        }
    }

    private IEnumerator Attack(Transform t)
    {
       while (targets.ContainsKey(t))
       {
           if (targets[t] > 0f && projectiles.Count > 0)
           {
              
               projectiles[0].Attack(t);
               projectiles[0].transform.parent = GS.FindParent(GS.Parent.allyprojectiles);
               projectiles.RemoveAt(0);
               targets[t] -= 1.5f; //implement race multiplier
               yield return new WaitForSeconds(attackTimer);
           }
           else
           {
               yield break;
           }
          
       }
    }
    
    private void Update()
    {
        UpdateRotations();
        while(resources <= 0f || projectiles.Count >= maxDrones)
        {
            return;
        } 
        timer -= Time.deltaTime;
        if (timer <= 0f)
        {
            resources -= 0.25f;
            if (resources % 1 == 0f)
            {
                ResourceManager.instance.NewTask(gameObject,new[]{1,0,0,0},delegate {resources += 1;},false);
            } 
            timer += refreshDuration;
            projectiles.Add(Instantiate(prefab, spinner.position + GS.RandCircle(0.4f,0.5f), Quaternion.identity, spinner));
            projectiles[^1].gameObject.SetActive(true);
        }
      
    }
    // Sets each projetiles transform.up to be facing outwards from the turret
    private void UpdateRotations()
    {
        for (int i = 0; i < projectiles.Count; i++)
        {
            projectiles[i].transform.up = GS.Rotated((projectiles[i].transform.position - transform.position).normalized,45f);
        }
    }

    public void ShrapnelUpgrade()
    {   
        prefab.GetComponent<ProjectilesOnDie>().n = 3;
    }

    public void SwarmUpgrade(int lvl)
    {
        if (lvl == 1)
        {
            refreshDuration = 2f;
            maxDrones = 10;
            maxResources = 5;
            attackTimer = 0.25f;
            prefab.speed = 0.25f;
            prefab.acceleration = 4f;
        }

        if (lvl == 2)
        {
            refreshDuration = 1f;
            maxDrones = 16;
            maxResources = 10;
            attackTimer = 0.15f;
            prefab.speed = 0.5f;
            prefab.acceleration = 5f;
        }

        if (lvl == 3)
        {
            refreshDuration = 0.6f;
            maxDrones = 32;
            maxResources = 22;
            attackTimer = 0.075f;
            prefab.speed = 0.75f;
            prefab.acceleration = 6f;
        }
    }
}