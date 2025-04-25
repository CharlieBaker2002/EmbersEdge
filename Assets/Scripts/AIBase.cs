using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class AIBase : MonoBehaviour
{
    Vector3 dest;
    public Transform target;
    public NavMeshAgent agent;
    public ActionScript AS;
    public float updateTime;
    public float thrust;
    public float chaseRad;
    public float searchRad;
    Vector2 initialPos;
    public bool move = true;
    public string seekType = "GuardSeeking";
    [HideInInspector]
    public float distancesqr;
    
    void Start()
    {
        initialPos = transform.position;
        agent.updateRotation = false;
        agent.updateUpAxis = false;
        agent.updatePosition = false;
        StartCoroutine(seekType);
        dest = initialPos;
    }

    void Update(){
        if(agent.path.corners[0] != null && move){
            AS.TryAddForce((agent.path.corners[0] - transform.position).normalized*thrust, true); 
        }
    }
    IEnumerator GuardSeeking(){ 
        while (true) {
            distancesqr = (transform.position- dest).sqrMagnitude;
            if(distancesqr > Mathf.Pow(chaseRad,2)){
                target = null;
            }
            if(target == null){
                target = GS.FindNearestEnemy(tag,transform.position,searchRad,false);
            }
            if(target == null){ 
                    dest = initialPos;
            }
            else{
                dest = target.position;
            }
            agent.nextPosition = transform.position;
            agent.SetDestination(dest);
            yield return new WaitForSeconds(updateTime);
        }
    }
    IEnumerator ChaseSeeking(){ 
        target = null;
        while (true) {
            if(target == null){
                target = GS.FindNearestEnemy(tag,transform.position,searchRad,false);
            }
            else{
                agent.nextPosition = transform.position;
                agent.SetDestination(target.position);
            }
            yield return new WaitForSeconds(updateTime);
        }
    }
}   