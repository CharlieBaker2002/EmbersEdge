using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Tentacle : MonoBehaviour, IOnCollide
{
    public int N = 50;

    [Range(0.05f,0.35f)]
    public float segLength = 0.1f;
    [Header("Defines how much the tentacle bends, don't go below 0.1")]
    public float fluidness = 0.5f;

    [Header("Defines how responsive the tentacle is, don't go below 0.1")]
    public float hastiness = 0.5f;
    
    public enum TentacleUpdateMode { Smooth,Lerp};
    [Header("")]
    public TentacleUpdateMode updateMode = TentacleUpdateMode.Smooth;

    public int enforcedRotationSegs = 0;
    [SerializeField]
    bool sinOnOneSide = false;
    [SerializeField]
    bool randomiseStartSize = true;
    
    [HideInInspector]
    public bool canWave = true;
    public bool busy = false;

    Vector2[] posVels;
    Vector3[] positions;
    private List<Vector2> prevData = new List<Vector2>();

    [Header("")]
    public LineRenderer lr;
    public ActionScript AS;
    public Transform targetEnd;
    private Vector2 targetPosition = Vector2.zero;
    public Transform attachPoint;
    [Header("Attach LS to make it respond to damage")]
    public LifeScript ls;
    private float dmgRefresh = 0f;
    private int ogN;

    private void Awake()
    {
        busy = false;
        if (randomiseStartSize)
        {
            float rand = Random.Range(0.8f, 1.25f);
            N = Mathf.RoundToInt(N * rand); //length is randomised by 25%
            if (ls != null)
            {
                ls.maxHp *= rand;
                ls.hp = ls.maxHp;
            }
        }
        ogN = N;
        posVels = new Vector2[N];
        positions = new Vector3[N];
        positions[0] = attachPoint.transform.position;
        Vector3 dir = (Vector2)attachPoint.transform.up * segLength;
        for(int i = 1; i <= enforcedRotationSegs; i++)
        {
            positions[i] = positions[i-1] + dir;
        }
        for (int i = 1 + enforcedRotationSegs; i < N; i++)
        {
            positions[i] = positions[i - 1] + dir + segLength * fluidness * (Vector3)Random.insideUnitCircle;
            dir = GS.Rotated(dir, Random.Range(0, fluidness * 80f / (N-enforcedRotationSegs)), true);
            posVels[i] = Vector2.zero;
        }
        lr.positionCount = N;
        lr.SetPositions(positions);
        targetEnd.position = (Vector3)Random.insideUnitCircle + attachPoint.transform.up + positions[^1];
        if (ls != null)
        {
            ls.onDamageDelegate+= change => {
                if (change > 0)
                {
                    return;
                }
                float hpdecimal = ls.hp / ls.maxHp;
                ChangeN(Mathf.RoundToInt(Mathf.Lerp(ogN,0.35f*ogN,1-hpdecimal)));
                if (dmgRefresh <= 0f)
                {
                    StartCoroutine(Randomise());
                    dmgRefresh = Random.Range(1.5f,2.5f);
                }
	        };
        }
    }

    private void FixedUpdate()
    {
        //less wrinkling, smart turning.
        var holdStillParameter = HoldStillParameter();
        float proximityLimit = N * segLength * 0.5f;
        float theta = -Vector2.SignedAngle(positions[^1] - attachPoint.position, positions[^5] - attachPoint.position);
        if (Vector2.Distance(targetEnd.position,attachPoint.position) < proximityLimit) //If donny is close, then try and get around donny unless hes v slow, in which case go closer to him.
        {
            targetPosition = attachPoint.position + (targetEnd.position - attachPoint.position).normalized * proximityLimit;
            targetPosition = (Vector2)attachPoint.position + (targetPosition - (Vector2)attachPoint.position).Rotated((theta > 0f?1:-1f) * 60f * hastiness * (proximityLimit - Vector2.Distance(targetEnd.position,attachPoint.position))/proximityLimit);
            targetPosition = Vector2.Lerp(targetPosition, targetEnd.position, holdStillParameter);
        }
        else
        {
            targetPosition = targetEnd.position;
        }

        Vector3[] initPositions = new Vector3[N];
        for (int i = 0; i < N; i++)
        {
            initPositions[i] = positions[i];
        }
       
        for (int repetitions = 0; repetitions < 10; repetitions++) //inverse kinematics loopage
        {
            positions[^1] = targetPosition;
            for (int i = positions.Length - 2; i > enforcedRotationSegs; i--)
            {
                positions[i] = positions[i + 1] + (positions[i] - positions[i + 1]).normalized * segLength;
            }
            positions[0] = attachPoint.position;
            for (int i = 1; i <= enforcedRotationSegs; i++)
            {
                positions[i] = positions[i - 1] + attachPoint.transform.up * segLength;
            }
            for (int i = 1 + enforcedRotationSegs; i < positions.Length; i++)
            {
                positions[i] = positions[i - 1] + (positions[i] - positions[i - 1]).normalized * segLength;
            }
            if (Vector2.Distance(positions[^1], targetPosition) < segLength/3)
            {
                break;
            }
        }
        Vector2 tangent = (targetPosition - (Vector2)attachPoint.position).normalized * segLength;
        tangent = new Vector2(tangent.y, -tangent.x);
        for (int i = 1; i < N; i++)                                 //move lr points towards target points by lerp or smoothdamp
        {
            if (updateMode == Tentacle.TentacleUpdateMode.Smooth)
            {
                if (positions[i] != initPositions[i])
                {
                    positions[i] = Vector2.SmoothDamp(initPositions[i], positions[i], ref posVels[i], ((N + 0.5f * i) / hastiness) * Time.fixedDeltaTime * 0.2f);
                }
            }
            else if (updateMode == Tentacle.TentacleUpdateMode.Lerp)
            {
                positions[i] = Vector2.Lerp(initPositions[i], positions[i], 3 * (1 + hastiness) * Time.fixedDeltaTime * (N - 0.5f * i) / N);
            }
            if (tangent == Vector2.zero || !canWave)
            {
                continue;
            }
            if (!sinOnOneSide)
            {
                positions[i] += (Vector3)((1 - Mathf.Exp(-0.1f * i)) * fluidness * segLength * Mathf.Sin(hastiness * 5 * Time.time + i * 10 * Mathf.Deg2Rad * (1f + fluidness)) * tangent);
            }
            else
            {
                positions[i] += (Vector3)(Mathf.Max(0f, (1 - Mathf.Exp(-0.1f * i)) * fluidness * segLength * Mathf.Sin(hastiness * 5 * Time.time + i * 10 * Mathf.Deg2Rad * (1f + fluidness))) * tangent);
            }
        }
        lr.SetPositions(positions);
        dmgRefresh -= Time.fixedDeltaTime;
    }

    public float HoldStillParameter() //Gets closer to a cunt when they are still
    {
        if(prevData.Count < 15) {
            prevData.Add(targetEnd.position);
            return 1;
        }
        float dist = Vector2.Distance(prevData[0], prevData[14]);
        prevData.RemoveAt(0);
        prevData.Add(targetEnd.position);
        return 1-Mathf.Min(0.05f*dist/Time.fixedDeltaTime, 1);
    }


    public IEnumerator Randomise() //make shit go lil cray cray, hurt? This is actually a happy mis-hap in code.
    {
        if (busy == true)
        {
            yield break;
        }
        busy = true;
        for (float t = 0f; t < 1.5f; t++)
        {
            Vector2 rand = Vector2.zero;
            for (int i = 1; i < positions.Length; i++)
            {
                rand += segLength * fluidness * Random.insideUnitCircle;
                positions[i] = (Vector2)positions[i] + rand;
            }
            yield return new WaitForFixedUpdate();
        }
        busy = false;
    }

    public IEnumerator Relax() // bring it back home slowly
    {
        if (busy == true)
        {
            yield break;
        }
        busy = true;
        int max = Mathf.Max(2,Mathf.CeilToInt(Random.Range(0, 3) * (2-hastiness)));
        float prevHaste = hastiness;
        float prevFluid = fluidness;
        fluidness *= 2;
        hastiness /= 2;
        float timeStart = Time.time;
        for (int i = 0; i < max; i++)
        {
            targetEnd.position = attachPoint.position + (Vector3)Random.insideUnitCircle.normalized * segLength * N/5;
            yield return new WaitForSeconds(Mathf.Max(1f,Mathf.Max(2f-hastiness,0.1f) * Random.Range(0.75f, 1.25f)));
            if(Time.time >= timeStart + 5f)
            {
                break;
            }
        }
        hastiness = prevHaste;
        fluidness = prevFluid;
        busy = false;
    }

    public IEnumerator Attack(Vector2 dir) //lez go towards der
    {
        dir.Normalize();
        if (busy == true)
        {
            yield break;
        }
        busy = true;
        float prevFluid = fluidness;
        fluidness *= 2;
        float prevHaste = hastiness;
        hastiness *= 2;
        bool prevSinSide = sinOnOneSide;
        sinOnOneSide = true;
        targetEnd.localPosition = dir * (segLength * ((N-enforcedRotationSegs) - 2*fluidness)) + enforcedRotationSegs * segLength * (Vector2)attachPoint.transform.up;
        while (Vector2.Distance(positions[^1], targetEnd.position) > 2 * segLength)
        {
            yield return null;
        }
        yield return new WaitForSeconds(0.2f);
        hastiness = prevHaste;
        sinOnOneSide = prevSinSide;
        fluidness = prevFluid;
        busy = false;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(targetPosition, 0.1f);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(targetEnd.position, 0.125f);
    }

    public void ChangeN(int newN)
    {
        lr.positionCount = newN;
        System.Array.Resize(ref positions, newN);
        System.Array.Resize(ref posVels, newN);
        if (newN > N)
        {
            for(int i = N; i < newN; i++)
            {
                positions[i] = positions[i - 1];
            }
        }
        N = newN;
    }

    public void OnCollide(Collision2D collision)
    {
        if(AS.sharpness == 0f)
        {
            return;
        }
        if (collision.gameObject.CompareTag(GS.EnemyTag(tag)) && collision.gameObject.GetComponent<ProjectileScript>() == null)
        {
            AS.sharpness = 0f;
            StartCoroutine(OnSoon());
        }
    }

    private IEnumerator OnSoon()
    {
        yield return new WaitForSeconds(0.75f);
        AS.sharpness = 0.15f;
    }
}
