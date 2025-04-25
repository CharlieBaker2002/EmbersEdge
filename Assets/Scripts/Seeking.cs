using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UIElements;

[RequireComponent(typeof(ActionScript))]
public class Seeking : MonoBehaviour
{
    public float seekDistance;
    public Transform target; //IF NOT GIVEN, WILL SEEK NEAREST ENEMY
    public float seekRefresh = 1f;
    private ActionScript AS;
    private Vector2 dir;
    public float force = 30f;
    public float rotSpeed = 2f;
    public float degOffset;
    public float initWait = 1f;
    private List<Transform> targets = new List<Transform>();
    public bool seekNewEnemyOnCollide = false;
    [HideInInspector]
    public float dist;
    [SerializeField] private LifeScript killOnLostTarget = null;
    [Header("0f: Totally Direct, 0.5f: Regular Seeking, 1f: Totally Indirect")]
    [Range(0,1)]
    public float indirectness = 0.5f; //USES IP INSTEAD OF NORMALIZED DIRECTION > 0  
    [Header("How Strong The Diverting Force Is")]
    public float divertMagnitude = 1f;
    [Header("Accelerate At Higher Proximities")]
    public float acceleration = 0f;

    private float tim;
    
    void Start()
    {
        AS = GetComponent<ActionScript>();
        this.QA(() => StartCoroutine(Seek()),initWait);
        tim = Time.time;
    }

    private void FixedUpdate()
    {
        if(rotSpeed == 0f)return;
        if (Time.time - tim > initWait)
        {
            transform.up = Vector2.Lerp(transform.up,AS.rb.velocity.normalized.Rotated(degOffset),Time.fixedDeltaTime * rotSpeed);
        }
    }
    
    IEnumerator Seek()
    {
        if (target != null)
        {
            targets.Add(target);
            while(target != null)
            {
                dir = target.position - transform.position;
                dist = dir.magnitude;
                if (indirectness != 0.5f)
                {
                    float y = divertMagnitude * dist / seekDistance;
                    Vector2 newDir =  (transform.position.IP(target.position, 1, y, false) - (Vector2)transform.position).normalized;
                    Vector2 oNewDir = (transform.position.IP(target.position, 1,  -y, false) - (Vector2)transform.position).normalized;
                    if(Vector2.Angle(AS.rb.velocity,newDir) > Vector2.Angle(AS.rb.velocity,oNewDir))
                    {
                        dir = Vector2.Lerp(newDir, oNewDir, indirectness);
                    }
                    else
                    {
                        dir = Vector2.Lerp(oNewDir,newDir,indirectness);
                    }
                   
                }
                else
                {
                    dir = dir.normalized;
                }
                dir *= 1 + acceleration * (1 - dist / seekDistance);
                AS.TryAddForce(force * Time.fixedDeltaTime * dir, true);
                yield return new WaitForFixedUpdate();
            }
        }
        if (killOnLostTarget!=null)
        {
            killOnLostTarget.OnDie();
            yield break;
        }
        StartCoroutine(FindNew());
    }
    
    IEnumerator FindNew()
    {
        while (target == null)
        {
            yield return new WaitForSeconds(seekRefresh);
            target = GS.FindNearestEnemy(tag,transform.position, seekDistance,false);
        }
        StartCoroutine(Seek());
    }

    private void OnCollisionEnter2D(Collision2D other)
    {
        if(!seekNewEnemyOnCollide)return;
        if (other.rigidbody.transform != target) return;
        StopAllCoroutines();
        target = null;
        StartCoroutine(FindNew());
    }
}
