using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UnitMovement : MonoBehaviour
{
    float maxT;
    Vector2 pos;
    Vector2 targetPos;
    float t;
    Vector3 before;
    float randX;
    float randY;
    float rand2X;
    float rand2Y;
    public int maxSearch = 10;

    private Collider2D[] cols;

    Transform target;
    public enum moveType { Straight, BezQuad, BezCub, BezQuadChase, BezCubChase, AroundClose, AroundFar }
    public moveType movType;
    public AnimationCurve moveSpeedGraph;
    float moveSpeed = 1f;
    [SerializeField] float searchDistance = 6f;
    public float turniness = 5f;

    float speedTimeFrame;
    IEnumerator Start()
    {
        cols = new Collider2D[maxSearch];
        speedTimeFrame = moveSpeedGraph[moveSpeedGraph.length - 1].time;
        yield return new WaitForFixedUpdate();
        yield return new WaitForSeconds(Random.Range(0.1f, 1.25f));
        while (true)
        {
            yield return new WaitForFixedUpdate();
            target = GS.FindEnemy(transform, searchDistance, GS.searchType.unitsSearch, cols);
            if (target == null)
            {
                yield return new WaitForSeconds(0.5f);
                continue;
            }
            switch (movType)
            {
                case moveType.Straight: //straight toward target
                    for (float i = Random.Range(1f, 3f); i > 0f; i -= Time.fixedDeltaTime)
                    {
                        Turn((Vector2)(target.position - transform.position));
                        transform.position += moveSpeed * Time.fixedDeltaTime * (target.position - transform.position).normalized;
                        transform.position = (Vector2)transform.position;
                        yield return new WaitForFixedUpdate();
                        if (BreakCheck(target.position))
                        {
                            break;
                        }
                    }
                    break;
                case moveType.BezQuad: //bezier quadratic curve from inital A to current B
                    Debug.Log("new");
                    maxT = Vector2.Distance(transform.position, target.position);
                    pos = transform.position;
                    targetPos = target.position;
                    randX = Random.Range(0.2f, 0.8f);
                    randY = Random.Range(0.15f, 0.7f) * GS.PlusMinus();
                    for (float i = Time.fixedDeltaTime; i < maxT; i += Time.fixedDeltaTime * moveSpeed)
                    {
                        before = (Vector2)transform.position;
                        transform.position = GS.Bez(new Vector2[] { pos, GS.IP(pos, targetPos, randX, randY,false), targetPos }, i, maxT);
                        Turn(transform.position - before);
                        yield return new WaitForFixedUpdate();
                        if(target == null)
                        {
                            break;
                        }
                    }
                    break;
                case moveType.BezQuadChase:
                    randX = Random.Range(0.2f, 0.8f); //quadratic bezier from initial A to live B, updated by distance
                    randY = Random.Range(0.15f, 0.7f) * GS.PlusMinus(); ;
                    pos = transform.position;
                    for (float i = Random.Range(0f, 1f); i < 2f; i += Time.fixedDeltaTime)
                    {
                        t = searchDistance - Vector2.Distance(transform.position, target.position) + moveSpeed * Time.fixedDeltaTime;
                        if (t > searchDistance)
                        {
                            t = searchDistance;
                        }
                        if (t < 0)
                        {
                            break;
                        }
                        before = transform.position;
                        transform.position = GS.Bez(new Vector2[] { pos, GS.IP(pos, target.position, randX, randY,false), target.position }, t, searchDistance);
                        Turn(transform.position - before);
                        yield return new WaitForFixedUpdate();
                        if(target == null)
                        {
                            break;
                        }
                    }
                    break;
                case moveType.BezCub: //bezier cubic curve from inital A to initial B
                    maxT = Vector2.Distance(transform.position, target.position);
                    pos = transform.position;
                    targetPos = target.position;
                    randX = Random.Range(0.1f, 0.3f);
                    randY = Random.Range(0f, 0.5f) * GS.PlusMinus();
                    rand2X = randX + 0.1f + Random.Range(0.1f, 0.3f);
                    rand2Y = Random.Range(0f, 0.5f) * GS.PlusMinus();
                    for (float i = Time.fixedDeltaTime; i < maxT; i += Time.fixedDeltaTime * moveSpeed)
                    {
                        before = transform.position;
                        transform.position = GS.Bez(new Vector2[] { pos, GS.IP(pos, targetPos, randX, randY,false), GS.IP(pos, targetPos, rand2X, rand2Y,false), targetPos }, i, maxT);
                        Turn(transform.position - before);
                        yield return new WaitForFixedUpdate();
                        if (target == null)
                        {
                            break;
                        }
                    }
                    break;
                case moveType.BezCubChase: //cubic bezier from initial A to live B, updated by distance
                    randX = Random.Range(0.1f, 0.3f);
                    randY = Random.Range(0f, 0.5f) * GS.PlusMinus();
                    rand2X = randX + 0.1f + Random.Range(0.1f, 0.3f);
                    rand2Y = Random.Range(0f, 0.5f) * GS.PlusMinus();
                    pos = transform.position;
                    for (float i = Random.Range(0f, 1f); i < 2f; i += Time.fixedDeltaTime)
                    {
                        t = searchDistance - Vector2.Distance(transform.position, target.position) + moveSpeed * Time.fixedDeltaTime;
                        if (t > searchDistance)
                        {
                            t = searchDistance;
                        }
                        if (t < 0)
                        {
                            break;
                        }
                        before = transform.position;
                        transform.position = GS.Bez(new Vector2[] { pos, GS.IP(pos, targetPos, randX, randY,false), GS.IP(pos, targetPos, rand2Y, rand2Y,false), targetPos }, t, searchDistance);
                        Turn(transform.position - before);
                        yield return new WaitForFixedUpdate();
                        if (target == null)
                        {
                            break;
                        }
                    }
                    break;
                case moveType.AroundClose: // tries to move to somewhere near target;
                    Vector3 rand = Random.insideUnitCircle;
                    for (float i = Random.Range(1f, 3f); i > 0f; i -= Time.fixedDeltaTime)
                    {
                        Turn((target.position + rand) - transform.position);
                        transform.position += moveSpeed * Time.fixedDeltaTime * ((target.position + rand) - transform.position).normalized;
                        yield return new WaitForFixedUpdate();
                        if (BreakCheck(target.position + rand))
                        {
                            break;
                        }
                    }
                    break;
                case moveType.AroundFar:
                    Vector3 randbig = Random.insideUnitCircle * 3f;
                    for (float i = Random.Range(1f, 3f); i > 0f; i -= Time.fixedDeltaTime)
                    {
                        Turn((target.position + randbig) - transform.position);
                        transform.position += ((target.position + randbig) - transform.position).normalized * moveSpeed * Time.fixedDeltaTime;
                        yield return new WaitForFixedUpdate();
                        if (BreakCheck(target.position + randbig))
                        {
                            break;
                        }
                    }
                    break;
            }
        }
    }
    private bool BreakCheck(Vector2 target)
    {
        if(target == null)
        {
            return true;
        }
        if (((Vector2)transform.position - target).sqrMagnitude <= 0.3f)
        {
            return true;
        }
        return false;
    }

    private void Update()
    {
        float timeNow = Time.time / speedTimeFrame;
        timeNow = (timeNow - Mathf.Floor(timeNow)) * speedTimeFrame;
        moveSpeed = moveSpeedGraph.Evaluate(timeNow);

    }

    private void Turn(Vector2 newDir)
    {
        transform.up = Vector2.Lerp(transform.up, newDir.normalized, Time.fixedDeltaTime * turniness);
    }
}
