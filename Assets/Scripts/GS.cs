using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using Random = UnityEngine.Random;

public static class GS
{
    public static GameObject Manager;
    public static SpawnManager spawn;
    public static GameObject portal;
    public static GameObject character;
    public static bool isRaidPhase = false;
    public static int era = 0;
    public static float il; //illumination coef;
    public static Action<int> OnNewEra;
    public static Collider2D bounds;
    public static readonly int[] daysforeraComplete = new int[] { 6, 10, 14 };

    public static ActionScript AS; 

    public static bool CanAct()
    {
        return AS.canAct;
    }

    public static int Era1()
    {
        return era + 1;
    }

    /// <summary>
    /// 1 to 2
    /// </summary>
    public static float EraModerated()
    {
        return 1 + era * 0.5f;
    }

    public static void IncrementEra()
    {
        era++;
        OnNewEra?.Invoke(era);
        EmbersEdge.currentCores = 1;
        SpawnManager.daySinceNewEra = era == 1
            ? SpawnManager.day - daysforeraComplete[0]
            : SpawnManager.day - daysforeraComplete[0] - daysforeraComplete[1];
    }

    public static void CallSpawnOrbs(Vector2 pos, int[] orbs, Transform p = null)
    {
        spawn.CallSpawnOrbs(pos, orbs, p);
    }

    public static void CallSpawnOrbs(Vector2 pos, float[] orbs, Transform p = null, bool harvest = false)
    {
        spawn.CallSpawnOrbs(pos, orbs, p, harvest);
    }


    public static Transform FindPrimary()
    {
        if (isRaidPhase)
        {
            return portal.transform;
        }
        else
        {
            return character.transform;
        }

    }

    public static string
        EnemyTag(string tagP, bool giveLayer = false, bool giveBuildingLayer = false) //also gives layers
    {
        if (tagP == "Enemies")
        {
            if (giveLayer)
            {
                if (giveBuildingLayer)
                {
                    return "Ally Buildings";
                }

                return "Ally Units";
            }

            return "Allies";
        }
        else if (tagP == "Allies")
        {
            if (giveLayer)
            {
                if (giveBuildingLayer)
                {
                    return "Enemy Buildings";
                }

                return "Enemy Units";
            }

            return "Enemies";
        }
        else if (tagP == "Misc")
        {
            return "Untagged";
        }
        else
        {
            Debug.Log("enemyTag in GS error, tag given: " + tagP);
            return "Untagged";
        }
    }

    public static Transform FindNearestEnemy(string tagP, Vector2 pos, float searchDistance, bool preferBuildings,
        bool allowOther = true)
    {
        Collider2D[] enemies = new Collider2D[] { };
        Collider2D[] buildings = new Collider2D[] { };
        if (!(preferBuildings && !allowOther))
        {
            if (tagP == "Enemies")
            {
                enemies = Physics2D.OverlapCircleAll(pos, searchDistance,
                    LayerMask.GetMask(new string[] { "Character", "Ally Units" }));
            }
            else
            {
                enemies = Physics2D.OverlapCircleAll(pos, searchDistance,
                    LayerMask.GetMask(new string[] { "Enemy Units" }));
            }

        }

        if (preferBuildings || allowOther)
        {
            buildings = Physics2D.OverlapCircleAll(pos, searchDistance,
                1 << LayerMask.NameToLayer(EnemyTag(tagP, true, true)));
        }

        Transform bestTarget = null;
        float closestDistanceSqr = Mathf.Infinity;
        Vector3 currentPosition = pos;
        if (preferBuildings)
        {
            foreach (Collider2D potentialTarget in buildings)
            {
                if (potentialTarget.isTrigger || potentialTarget.attachedRigidbody == null) continue;
                float dSqrToTarget = (potentialTarget.transform.position - currentPosition).sqrMagnitude;
                if (dSqrToTarget < closestDistanceSqr)
                {
                    closestDistanceSqr = dSqrToTarget;
                    bestTarget = potentialTarget.attachedRigidbody.transform;
                }
            }

            if (bestTarget == null && allowOther)
            {
                foreach (Collider2D potentialTarget in enemies)
                {
                    if (potentialTarget.isTrigger || potentialTarget.attachedRigidbody == null) continue;
                    float dSqrToTarget = (potentialTarget.transform.position - currentPosition).sqrMagnitude;
                    if (dSqrToTarget < closestDistanceSqr)
                    {
                        closestDistanceSqr = dSqrToTarget;
                        bestTarget = potentialTarget.attachedRigidbody.transform;
                    }
                }
            }
        }
        else
        {
            foreach (Collider2D potentialTarget in enemies)
            {
                if (potentialTarget.isTrigger || potentialTarget.attachedRigidbody == null) continue;
                float dSqrToTarget = (potentialTarget.transform.position - currentPosition).sqrMagnitude;
                if (dSqrToTarget < closestDistanceSqr)
                {
                    closestDistanceSqr = dSqrToTarget;
                    bestTarget = potentialTarget.attachedRigidbody.transform;
                }
            }

            if (bestTarget == null && allowOther)
            {
                foreach (Collider2D potentialTarget in buildings)
                {
                    if (potentialTarget.isTrigger || potentialTarget.attachedRigidbody == null) continue;
                    float dSqrToTarget = (potentialTarget.transform.position - currentPosition).sqrMagnitude;
                    if (dSqrToTarget < closestDistanceSqr)
                    {
                        closestDistanceSqr = dSqrToTarget;
                        bestTarget = potentialTarget.attachedRigidbody.transform;
                    }
                }
            }
        }

        return bestTarget;
    }

    public enum searchType
    {
        allAndProjSearch,
        allSearch,
        buildingsSearch,
        unitsSearch,
        projSearch
    };

    public static ActionScript FindEnemyAS(Transform t, float searchDistance, searchType search)
    {
        string[] strs = new string[] { };
        string add = t.CompareTag("Allies") ? "Ally " : "Enemy ";
        switch (search)
        {
            case (searchType.allSearch):
                strs = new string[] { add + "Units", add + "Buildings" };
                break;
            case (searchType.allAndProjSearch):
                strs = new string[] { add + "Units", add + "Buildings", add + "Projectiles" };
                break;
            case (searchType.buildingsSearch):
                strs = new string[] { add + "Buildings" };
                break;
            case (searchType.unitsSearch):
                strs = new string[] { add + "Units" };
                break;
            case (searchType.projSearch):
                strs = new string[] { add + "Projectiles" };
                break;
        }

        if (!t.CompareTag("Allies"))
        {
            if (search != searchType.projSearch && search != searchType.buildingsSearch)
            {
                Array.Resize(ref strs, strs.Length + 1);
                strs[^1] = "Character";
            }
        }

        var cols = Physics2D.OverlapCircleAll(t.position, searchDistance, LayerMask.GetMask(strs));
        int rand;
        for (int i = 0; i < cols.Length; i++)
        {
            rand = UnityEngine.Random.Range(0, cols.Length);
            if (cols[rand].isTrigger) continue;
            if (cols[rand].attachedRigidbody != null)
            {
                if (cols[rand].attachedRigidbody.TryGetComponent<ActionScript>(out var AS))
                {
                    return AS;
                }
            }
        }

        return null;
    }

    public static searchType BoolsToSearch(bool allowUnits, bool allowBuildings, bool allowProjectiles)
    {
        if (allowUnits && allowBuildings && allowProjectiles)
        {
            return searchType.allAndProjSearch;
        }
        else if (allowUnits && allowBuildings)
        {
            return searchType.allSearch;
        }
        else if (allowUnits)
        {
            return searchType.unitsSearch;
        }
        else if (allowBuildings)
        {
            return searchType.buildingsSearch;
        }
        else if (allowProjectiles)
        {
            return searchType.projSearch;
        }
        else
        {
            return searchType.allSearch;
        }
    }

    /// <summary>
    /// FINDS ANY TARGET WITHIN RANGE. GIVE COMPARE IF YOU WOULD LIKE TO TRY FIND A NEW TARGET EFFICIENTLY
    /// </summary>
    public static Transform FindEnemy(Transform t, float searchDistance, searchType search, Collider2D[] cols,
        Transform compare = null)
    {
        string[] strs = new string[] { };
        string add = t.CompareTag("Allies") ? "Enemy " : "Ally ";
        switch (search)
        {
            case (searchType.allSearch):
                strs = new string[] { add + "Units", add + "Buildings" };
                break;
            case (searchType.allAndProjSearch):
                strs = new string[] { add + "Units", add + "Buildings", add + "Projectiles" };
                break;
            case (searchType.buildingsSearch):
                strs = new string[] { add + "Buildings" };
                break;
            case (searchType.unitsSearch):
                strs = new string[] { add + "Units" };
                break;
            case (searchType.projSearch):
                strs = new string[] { add + "Projectiles" };
                break;
        }

        if (!t.CompareTag("Allies"))
        {
            if (search != searchType.projSearch && search != searchType.buildingsSearch)
            {
                Array.Resize(ref strs, strs.Length + 1);
                strs[^1] = "Character";
            }
        }

        var size = Physics2D.OverlapCircleNonAlloc(t.position, searchDistance, cols, LayerMask.GetMask(strs));
        if (size > 0)
        {
            if (compare == null)
            {
                for (int i = 0; i < size; i++)
                {
                    if (cols[i].isTrigger || cols[i].attachedRigidbody == null) continue;
                    return cols[i].attachedRigidbody.transform;
                }
            }
            //else
            int l = Mathf.Min(4, size);
            bool longer = size > l;
            for (int i = 0; i < l; i++)
            {
                Collider2D c = longer ? cols[Random.Range(0, size)] : cols[i];
                if (c.attachedRigidbody == null || c.isTrigger) continue;
                if (SqrDist(t, c.attachedRigidbody.transform) < SqrDist(t, compare))
                {
                    return cols[i].attachedRigidbody.transform;
                }
            }
        }

        return compare;
    }

    public static List<Transform> FindEnemies(string tagP, Vector2 pos, float searchDistance,
        bool allowBuildings = true, bool allowProjectiles = false, Collider2D[] cols = null)
    {
        List<string> layers = new List<string>();
        if (tagP.ToLower() == "enemies")
        {
            layers.Add("Character");
            layers.Add("Ally Units");
            if (allowBuildings)
            {
                layers.Add("Ally Buildings");
            }

            if (allowProjectiles)
            {
                layers.Add("Ally Projectiles");
            }
        }
        else
        {
            layers.Add("Enemy Units");
            if (allowBuildings)
            {
                layers.Add("Enemy Buildings");
            }

            if (allowProjectiles)
            {
                layers.Add("Enemy Projectiles");
            }
        }

        List<Transform> objs = new List<Transform>();
        if (cols == null)
        {
            cols = new Collider2D[10];
        }

        int n = Physics2D.OverlapCircleNonAlloc(pos, searchDistance, cols, LayerMask.GetMask(layers.ToArray()));
        for (int x = 0; x < n; x++)
        {
            if (cols[x].isTrigger)
            {
                continue;
            }

            if (cols[x].attachedRigidbody != null)
            {
                objs.Add(cols[x].attachedRigidbody.transform);
            }
        }

        return objs.Distinct().ToList();
    }

    public static Parent ProjParent(Transform t)
    {
        if (t.CompareTag("Allies"))
        {
            return Parent.allyprojectiles;
        }
        else
        {
            return Parent.enemyprojectiles;
        }
    }
    public enum Parent
    {
        enemies, allies, enemyprojectiles, allyprojectiles, fx, ee, buildings, loot, misc, followers
    }

    public static Transform FindParent(GS.Parent p)
    {
        return spawn.FindParent(p);
    }

    public static GameObject RE(GameObject[] objs)
    {
        return objs[UnityEngine.Random.Range(0, objs.Length)];
    }


    public static Transform RE(Transform[] objs)
    {
        return objs[UnityEngine.Random.Range(0, objs.Length)];
    }

    public static int Factorial(int a)
    {
        int b = a;
        while (a > 1)
        {
            a--;
            b *= a;
        }

        return b;
    }

    public static int Sigma(int a)
    {
        int b = a;
        while (a > 1)
        {
            a--;
            b += a;
        }

        return b;
    }

    public static int PlusMinus()
    {
        if (UnityEngine.Random.Range(0, 2) == 0)
        {
            return 1;
        }
        else
        {
            return -1;
        }
    }

    /// <summary>
    /// shoots transform.up, with innacuracy being double the range of random perpedincular component applied to final vector.
    /// </summary>
    public static GameObject NewP(GameObject proj, Transform T, string ta, float innaccuracy = 0f, float strength = 0,
        float randStrength = 0f)
    {
        if (randStrength > 0f)
        {
            strength += UnityEngine.Random.Range(0, randStrength);
        }

        return SpawnManager.instance.NewP(proj, T, ta, innaccuracy, strength);
    }

    /// <summary>
    /// shoots in direction given, with innacuracy being double the range of random perpedincular component applied to final vector.
    /// </summary>
    public static GameObject NewP(GameObject proj, Transform T, string ta, Vector2 dir, float innaccuracy = 0f,
        float strength = 0f, float randStrength = 0f)
    {
        if (randStrength > 0f)
        {
            strength += UnityEngine.Random.Range(0, randStrength);
        }

        return SpawnManager.instance.NewP(proj, T, ta, dir, innaccuracy, strength);
    }

    public static ProjectileScript NewP(ProjectileScript ps, Transform T, string ta, float innaccuracy = 0f,
        float strength = 0, float randStrength = 0f)
    {
        if (randStrength > 0f)
        {
            strength += UnityEngine.Random.Range(0, randStrength);
        }

        return SpawnManager.NewP(ps, T, ta, innaccuracy, strength);
    }

    public static Transform ProjectileParent(string t)
    {
        t.ToLower();
        if (t == "Allies")
        {
            return spawn.AllyProjectiles;
        }
        else if (t == "Enemies")
        {
            return spawn.EnemyProjectiles;
        }
        else
        {
            throw new System.Exception("GS ProjectileParent Issue, string given: " + t);
        }
    }

    public static Transform CS()
    {
        return character.transform;
    }



    public static bool VP(int vulnerableType, Transform t, Vector2 pos, float chance = 100)
    {
        if (UnityEngine.Random.Range(0f, 100f) <= chance)
        {
            spawn.MakeVulnerable(vulnerableType, t, pos);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Vector to Quaternion
    /// </summary>
    public static Quaternion VTQ(Vector2 v)
    {
        return Quaternion.Euler(0, 0, Vector2.SignedAngle(Vector2.up, v));
    }

    public static float VTA(Vector2 a)
    {
        return PutInRange(Mathf.Abs(Vector2.SignedAngle(-Vector2.up, a) - 180f),0f,360f);
    }

    public static Vector2 QTV(Quaternion q)
    {
        return new Vector2(-Mathf.Sin(q.eulerAngles.z * Mathf.Deg2Rad), Mathf.Cos(q.eulerAngles.z * Mathf.Deg2Rad));
    }

    public static void KillNonAlloc(Vector2 pos, float rad, string[] mas)
    {
        spawn.StartCoroutine(IKillNonAlloc(pos, rad, mas));
    }

    static IEnumerator IKillNonAlloc(Vector2 pos, float rad, string[] mas)
    {
        for (int i = 0; i < 3; i++)
        {
            foreach (Collider2D col in Physics2D.OverlapCircleAll(pos, rad, LayerMask.GetMask(mas)))
            {
                if (col != null)
                {
                    if (col.TryGetComponent<LifeScript>(out var ls))
                    {
                        ls.Change(-1000, 0, true, false, false, true);
                        yield return null;
                    }
                }
            }
        }
    }


    public static void GatherResources(Transform t)
    {
        foreach (Transform c in t)
        {
            if (c.TryGetComponent<OrbScript>(out var OS))
            {
                if (OS.state == OrbScript.OrbState.wild)
                {
                    OS.state = OrbScript.OrbState.collect;
                }
            }
        }
    }

    public static void DestroyResources(Transform t)
    {
        foreach (Transform c in t)
        {
            if (c.TryGetComponent<OrbScript>(out var OS))
            {
                if (OS.state ==OrbScript.OrbState.wild)
                {
                    OS.ReturnToPool();
                }
            }
        }
    }

    public static Vector3 RandCircle(float min, float max)
    {
        return (Vector3)UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(min, max);
    }

    public static Vector2 RandCircleV2(float min, float max)
    {
        return UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(min, max);
    }

    /// <summary>
    /// is x inbetween y and z? Inclusive.
    /// </summary>
    public static bool InRange(float x, float y, float z)
    {
        float small = Mathf.Min(y, z);
        float big = Mathf.Max(y, z);
        if (x >= small && x <= big)
        {
            return true;
        }

        return false;
    }

    public static float SqrDist(Transform a, Transform b)
    {
        if (a != null && b != null)
        {
            return (a.position - b.position).sqrMagnitude;
        }
        else
        {
            return -1;
        }
    }

    public static void DrawCircle(LineRenderer lr, float rad, int points)
    {
        if (lr.positionCount < points)
        {
            lr.positionCount = points;
        }

        float theta = 0f;
        float thetaIncrement = 2 * Mathf.PI / (points - 1);
        for (int i = 0; i < points; i++)
        {
            lr.SetPosition(i, rad * new Vector3(Mathf.Cos(theta), -Mathf.Sin(theta), 0));
            theta += thetaIncrement;
        }
    }

    public static Color ColourFromCost(int[] cost)
    {
        List<Color> cols = new List<Color>();
        for (int i = 0; i < 4; i++)
        {
            if (cost[i] > 0)
            {
                cols.Add(ColorFromIndex(i));
            }
        }

        if (cols.Count == 0)
        {
            return new Color(0.5f, 0.5f, 0.5f, 0.6f);
        }

        float[] col = new float[3] { 0, 0, 0 };
        foreach (Color c in cols)
        {
            col[0] += c.r;
            col[1] += c.g;
            col[2] += c.b;
        }

        col[0] /= cols.Count;
        col[1] /= cols.Count;
        col[2] /= cols.Count;

        return new Color(col[0], col[1], col[2], 0.4f);
    }

    public static float CostFromIndex(int index, float N)
    {
        return index switch
        {
            0 => N,
            1 => 3 * N,
            2 => 15 * N,
            _ => 45 * N
        };
    }

    private static Color ColorFromIndex(int ind)
    {
        return ind switch
        {
            0 => UIManager.i.colSO.StandardWhite,
            1 => UIManager.i.colSO.StandardGreen,
            2 => UIManager.i.colSO.StandardBlue,
            3 => UIManager.i.colSO.StandardRed,
            _ => Color.white,
        };
    }

    public static Vector2 VectInRange(Vector2 vect, float min, float max)
    {
        if (vect.sqrMagnitude > max * max)
        {
            return vect.normalized * max;
        }
        else if (vect.sqrMagnitude < min * min)
        {
            return vect.normalized * min;
        }
        else
        {
            return vect;
        }
    }

    public static float PutInRange(float x, float min, float max)
    {
        if (x < min)
        {
            return min;
        }

        if (x > max)
        {
            return max;
        }

        return x;
    }

    /// <summary>
    /// Adds a force towards the postion of v2. maxForce is the maximum force applied. maxDist is the distance at which maxForce is applied. zeroDist is the distance at which no force is applied.
    /// </summary>
    public static void TryAddForceToward(this ActionScript AS, Vector2 v2, float maxForce, float maxDist,
        float zeroDist = 0f)
        {
        float d = ((Vector2)AS.transform.position - v2).sqrMagnitude;
        float coef = 0f;
        if (d > zeroDist * zeroDist)
        {
            if (maxDist * maxDist > d)
            {
                coef = maxForce * (Mathf.Sqrt(d) - zeroDist) / (maxDist - zeroDist);
            }
            else
            {
                coef = maxForce;
            }

            AS.TryAddForce(coef * (v2 - (Vector2)AS.transform.position).normalized, true);
        }
    }

    /// <summary>
    /// 90 rand is 180 field of possible angle
    /// </summary>
    public static Vector2 Rotated(this Vector2 v, float degrees, bool rand = false)
    {
        if (rand)
        {
            degrees = GS.PlusMinus() * UnityEngine.Random.Range(0f, degrees);
        }

        float radians = degrees * Mathf.Deg2Rad;
        float sin = Mathf.Sin(radians);
        float cos = Mathf.Cos(radians);
        return new Vector2(cos * v.x - sin * v.y, sin * v.x + cos * v.y);
    }

    public static Quaternion RandRot()
    {
        return Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(0f, 360f));
    }

    /// <summary>
    /// return new vector where 90 degrees is (0,1)
    /// </summary>
    public static Vector2 ATV(float degrees)
    {
        float radians = degrees * Mathf.Deg2Rad;
        float sin = Mathf.Sin(radians);
        float cos = Mathf.Cos(radians);
        return new Vector2(cos, sin);
    }

    /// <summary>
    /// return new vector where 90 degrees is (0,1)
    /// </summary>
    public static Vector3 ATV3(float degrees)
    {
        float radians = degrees * Mathf.Deg2Rad;
        float sin = Mathf.Sin(radians);
        float cos = Mathf.Cos(radians);
        return new Vector2(cos, sin);
    }

    public static int Sign(float x)
    {
        if (x >= 0)
        {
            return 1;
        }
        else
        {
            return -1;
        }
    }

    public static bool InState(this Animator a, string s)
    {
        if (a.GetCurrentAnimatorStateInfo(0).IsName("Base Layer." + s))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Intermediate Point between two v2s.
    /// </summary>
    /// <param name="x"> 0 to 1. Lerp from a to b. </param>
    /// <param name="y"> -inf to +inf. Normal coef between a and b. Not normalised </param>
    /// <returns></returns>
    public static Vector2 IP(this Vector2 a, Vector2 b, float x, float y, bool RY = true)
    {
        if (RY)
        {
            y *= GS.PlusMinus();
        }

        Vector2 pos = Vector2.LerpUnclamped(a, b, x);
        Vector2 norm = (b - a);
        norm = norm.Rotated(90);
        pos += norm * y;
        return pos;
    }

    //v3, v2
    public static Vector2 IP(this Vector3 a, Vector2 b, float x, float y, bool RY = true)
    {
        if (RY)
        {
            y *= GS.PlusMinus();
        }

        Vector2 a1 = (Vector2)a;
        Vector2 pos = Vector2.Lerp(a, b, x);
        Vector2 norm = (b - a1);
        norm = norm.Rotated(90);
        pos += norm * y;
        return pos;
    }

    private static Vector2 BezQ(Vector2 a, Vector2 b, Vector2 c, float t)
    {
        Vector2 l0 = Vector2.Lerp(a, b, t);
        Vector2 l1 = Vector2.Lerp(b, c, t);
        return Vector2.Lerp(l0, l1, t);
    }

    private static Vector2 BezC(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float t)
    {
        Vector2 l0 = Vector2.Lerp(a, b, t);
        Vector2 l1 = Vector2.Lerp(b, c, t);
        Vector2 l2 = Vector2.Lerp(c, d, t);
        Vector2 q0 = Vector2.Lerp(l0, l1, t);
        Vector2 q1 = Vector2.Lerp(l1, l2, t);
        return Vector2.Lerp(q0, q1, t);
    }

    /// <summary>
    /// gives a point along a quadratic or cubic bezier
    /// </summary>
    public static Vector2 Bez(Vector2[] vs, float t, float maxT = 1f)
    {
        if (vs.Length == 3)
        {
            return BezQ(vs[0], vs[1], vs[2], t / maxT);
        }
        else if (vs.Length == 4)
        {
            return BezC(vs[0], vs[1], vs[2], vs[3], t / maxT);
        }
        else
        {
            return Vector2.zero;
            throw new Exception("Bezier n error");
        }
    }

    /// <summary>
    /// gives n points along an entire quadr or cubic bezier
    /// </summary>
    public static Vector2[] Bezes(Vector2[] vs, float n)
    {
        List<Vector2> pos = new List<Vector2>();
        for (int i = 1; i < n + 1; i++)
        {
            if (vs.Length == 3)
            {
                pos.Add(BezQ(vs[0], vs[1], vs[2], i / n));
            }
            else
            {
                pos.Add(BezC(vs[0], vs[1], vs[2], vs[3], i / n));
            }
        }

        return pos.ToArray();
    }

    /// <summary>
    /// gives n directions along an entire quadr or cubic bezier
    /// </summary>
    public static Vector2[] BezesDir(Vector2[] vs, float n, bool normalise = true)
    {
        List<Vector2> pos = new List<Vector2>();
        for (int i = 1; i < n + 1; i++)
        {
            if (vs.Length == 3)
            {
                pos.Add(BezQ(vs[0], vs[1], vs[2], i / n));
            }
            else
            {
                pos.Add(BezC(vs[0], vs[1], vs[2], vs[3], i / n));
            }
        }
        Vector2[] dirs = new Vector2[pos.Count];
        for (int i = 0; i < pos.Count; i++)
        {
            if (i == 0)
            {
                dirs[i] = pos[i] - vs[0];
            }
            else
            {
                dirs[i] = pos[i] - pos[i - 1];
            }

            if (normalise)
            {
                dirs[i] = dirs[i].normalized;
            }
        }
        return dirs;
    }

    public static void CopyArray<T>(ref T[] A, T[] B)
    {
        A = new T[B.Length];
        for (int i = 0; i < B.Length; i++)
        {
            A[i] = B[i];
        }
    }

    /// <summary>
    /// B COPIED TO A
    /// </summary>
    public static void CopyList<T>(ref List<T> A, List<T> B)
    {
        A = new List<T>();
        foreach (T t in B)
        {
            A.Add(t);
        }
    }

    public static void CopyList<T>(ref List<T> A, T[] B)
    {
        A = new List<T>();
        foreach (T t in B)
        {
            A.Add(t);
        }
    }

    /// <summary>
    /// Copy B to A
    /// </summary>
    public static void CopyArray(ref int[] A, int[] B)
    {
        for (int i = 0; i < A.Length; i++)
        {
            A[i] = B[i];
        }
    }

    /// <summary>
    /// Copy B to A
    /// </summary>
    public static void CopyArray(ref float[] A, float[] B)
    {
        for (int i = 0; i < A.Length; i++)
        {
            A[i] = B[i];
        }
    }

    /// <summary>
    /// Copy B to A
    /// </summary>
    public static void CopyArray(ref float[] A, int[] B)
    {
        for (int i = 0; i < A.Length; i++)
        {
            A[i] = B[i];
        }
    }

    public static int[] FloatToIntArray(float[] array)
    {
        int[] x = new int[array.Length];
        for (int i = 0; i < array.Length; i++)
        {
            x[i] = Mathf.RoundToInt(array[i]);
        }

        return x;
    }

    public static void AddArray(ref int[] array, int[] add)
    {
        for (int i = 0; i < array.Length; i++)
        {
            array[i] += add[i];
        }
    }

    public static void AddArray(ref float[] array, float[] add)
    {
        for (int i = 0; i < array.Length; i++)
        {
            array[i] += add[i];
        }
    }

    public static void AddArray(ref float[] array, int[] add)
    {
        for (int i = 0; i < array.Length; i++)
        {
            array[i] += (float)add[i];
        }
    }

    public static void TimesArray(ref int[] array, int x)
    {
        for (int i = 0; i < array.Length; i++)
        {
            array[i] *= x;
        }
    }

    public static int[] TimesArray(int[] array, int x)
    {
        int[] newAr = new int[array.Length];
        for (int i = 0; i < array.Length; i++)
        {
            newAr[i] = array[i] * x;
        }

        return newAr;
    }

    /// <summary>
    /// pref round up
    /// </summary>
    /// <param name="array"></param>
    public static void TimesArray(ref int[] array, float x)
    {
        for (int i = 0; i < array.Length; i++)
        {
            array[i] = Mathf.RoundToInt(array[i] * x + 0.01f);
        }
    }

    public static void TimesArray(ref float[] array, float x)
    {
        for (int i = 0; i < array.Length; i++)
        {
            array[i] *= x;
        }
    }

    public static string ArrayToString(int[] array)
    {
        string s = "";
        foreach (int i in array)
        {
            s += i.ToString();
            s += " | ";
        }

        return s;
    }

    public static string ArrayToString(float[] array)
    {
        string s = "";
        foreach (float i in array)
        {
            s += i.ToString();
            s += " | ";
        }

        return s;
    }

    public static void SetParent(Transform t, Transform p)
    {
        t.parent = p;
        t.localPosition = Vector3.zero;
        t.localRotation = Quaternion.identity;
        t.localScale = Vector3.one;
    }

    public static void Shuffle<T>(this List<T> lst)
    {
        int n = lst.Count;
        while (n > 1)
        {
            int k = UnityEngine.Random.Range(0, n--);
            (lst[n], lst[k]) = (lst[k], lst[n]);
        }
    }

    public static void Shuffle<T>(this T[] array)
    {
        int n = array.Length;
        while (n > 1)
        {
            int k = UnityEngine.Random.Range(0, n--);
            (array[n], array[k]) = (array[k], array[n]);
        }
    }

    /// <summary>
    /// Paramater chance is % (of 100).
    /// </summary>
    public static bool Chance(float chance)
    {
        if (chance < 0f)
        {
            return false;
        }

        if (chance >= 100f)
        {
            return true;
        }

        return UnityEngine.Random.Range(0f, 100f) <= chance;
    }

    public static bool InDungeon(this Transform t)
    {
        if (t.position.sqrMagnitude > 600000)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// cost must be len 4
    /// </summary>
    public static float CostValue(int[] cost, float coef = 1f)
    {
        float t = 0f;
        t += cost[0];
        t += cost[1] * 3;
        t += cost[2] * 15;
        t += cost[3] * 45;
        return t * coef;
    }

    public static int[] CostArray(int i, int x = 1)
    {
        int[] v = new int[4];
        v[i] = x;
        return v;
    }

    /// <summary>
    /// Returns a length 4 int array with position i having value-scaled x | E.G. (3,45) returns {0,0,0,1}
    /// </summary>
    public static int[] CostArrayScaled(int i, int x)
    {
        int[] a = new int[4];
        float val = CostValue(CostArray(i, 1)); //value of 1 orb of type i
        return CostArray(i, Mathf.RoundToInt(x / val));
    }

    public static string RemClone(this string s)
    {
        return s.Split("(Clone)")[0];
    }

    public static void TurnTowards(this MonoBehaviour T, Transform o, float time, float coef)
    {
        if (o == null)
        {
            return;
        }

        T.StartCoroutine(TurnTowardsI(T, o, time, coef));
    }

    private static IEnumerator TurnTowardsI(MonoBehaviour T, Transform o, float time, float coef)
    {
        for (float i = time; i > 0f; i -= Time.fixedDeltaTime)
        {
            if (o == null) yield break;
            T.transform.up = Vector2.Lerp(T.transform.up, (o.position - T.transform.position).normalized,
                coef * Time.fixedDeltaTime);
            yield return new WaitForFixedUpdate();
        }
    }

    public static float Distance(this Transform T, Transform o)
    {
        return Vector2.Distance(T.position, o.position);
    }

    public static float Distance(this Transform T, Vector2 o)
    {
        return Vector2.Distance(T.position, o);
    }

    public static float Distance(this Transform T, Vector3 o)
    {
        return Vector2.Distance(T.position, o);
    }

    /// <summary>
    /// Normalized direction vector
    /// </summary>
    public static Vector2 V(this Transform T, Transform o)
    {
        return (o.position - T.position).normalized;
    }

    public static Vector2 V(this Transform T, Vector2 o)
    {
        return (o - (Vector2)T.position).normalized;
    }

    public static Vector2 V(this Transform T, Vector3 o)
    {
        return ((Vector2)(o - T.position)).normalized;
    }

    /// <summary>
    /// Instant turn, using vector2 lerp and fixed delta time.
    /// </summary>
    public static void Turn(this Transform T, Transform o, float coef, bool invert = false)
    {
        if (!invert)
        {
            T.up = Vector2.Lerp(T.up, (o.position - T.position).normalized, coef * Time.fixedDeltaTime);
        }
        else
        {
            T.up = Vector2.Lerp(T.up, (T.position - o.position).normalized, coef * Time.fixedDeltaTime);
        }


    }

    public static string ToRoman(int number)
    {
        if ((number < 0) || (number > 3999)) throw new ArgumentOutOfRangeException("insert value betwheen 1 and 3999");
        if (number < 1) return string.Empty;
        if (number >= 1000) return "M" + ToRoman(number - 1000);
        if (number >= 900) return "CM" + ToRoman(number - 900);
        if (number >= 500) return "D" + ToRoman(number - 500);
        if (number >= 400) return "CD" + ToRoman(number - 400);
        if (number >= 100) return "C" + ToRoman(number - 100);
        if (number >= 90) return "XC" + ToRoman(number - 90);
        if (number >= 50) return "L" + ToRoman(number - 50);
        if (number >= 40) return "XL" + ToRoman(number - 40);
        if (number >= 10) return "X" + ToRoman(number - 10);
        if (number >= 9) return "IX" + ToRoman(number - 9);
        if (number >= 5) return "V" + ToRoman(number - 5);
        if (number >= 4) return "IV" + ToRoman(number - 4);
        if (number >= 1) return "I" + ToRoman(number - 1);
        else return "InvalidRoman";
    }

    public static bool CloserAThanB(this float x, float a, float b)
    {
        return Mathf.Abs(x - a) < Mathf.Abs(x - b);
    }

    /// <summary>
    /// 0-index based
    /// </summary>
    public static Color ColFromEra()
    {
        if (era == 0)
        {
            return SpawnManager.instance.e1;
        }
        else if (era == 1)
        {
            return SpawnManager.instance.e2;
        }
        else
        {
            return SpawnManager.instance.e3;
        }
    }
    

    public static float FixedAngle(float ang, bool degrees)
    {
        if (degrees)
        {
            if (ang < 0)
            {
                ang += 360f;
            }
            else if (ang > 360f)
            {
                ang -= 360f;
            }
        }
        else
        {
            if (ang < 0)
            {
                ang += Mathf.PI * 2;
            }
            else if (ang > Mathf.PI * 2)
            {
                ang -= Mathf.PI * 2;
            }
        }

        return ang;
    }

    public static float AngleDifference(float a, float b)
    {
        while (a < 0f)
        {
            a += 360f;
        }

        while (b < 0f)
        {
            b += 360f;
        }

        while (a >= 360f)
        {
            a -= 360f;
        }

        while (b >= 360f)
        {
            b -= 360f;
        }

        float max = Mathf.Max(a, b);
        float min = max == a ? b : a;
        if (max - min <= 180f)
        {
            return max - min;
        }

        return 360f - (max - min);
    }


    public static float EraCompletion()
    {
        return (float)SpawnManager.daySinceNewEra / daysforeraComplete[era];
    }

    /// <summary>
    /// era 0 = 0. Superbright & bright cannot be lit
    /// </summary>
    public static Material MatByEra(int era, bool bright = false, bool lit = false, bool superBright = false)
    {
        if (superBright)
        {
            return SpawnManager.instance.eraMats[era + 9];
        }
        if (lit)
        {
            return SpawnManager.instance.eraMats[era + 6];
        }
        if (!bright)
        {
            return SpawnManager.instance.eraMats[era];
        }
        return SpawnManager.instance.eraMats[era + 3];
    }

    /// <summary>
    /// t in range 0 to 1
    /// </summary>
    public static T PercentParameter<T>(T[] values, float t)
    {
        t = PutInRange(t, 0f, 0.9999999f);
        int ind = Mathf.FloorToInt(t * values.Length);
        return values[ind];
    }

    public static Vector2? GetWorldCoordinate(Camera mainCamera, Camera designerCamera,
        RectTransform textureRectTransform, Vector2 point)
    {
        if (!RectTransformUtility.RectangleContainsScreenPoint(textureRectTransform, point, mainCamera))
        {
            return null;
        }

        RectTransformUtility.ScreenPointToLocalPointInRectangle(textureRectTransform, point, mainCamera,
            out Vector2 localClick);

        localClick.y = (textureRectTransform.rect.yMin * -1) - (localClick.y * -1);

        Vector2 viewportClick = new(localClick.x / textureRectTransform.rect.width,
            localClick.y / textureRectTransform.rect.height);

        Vector2 worldClick = designerCamera.ViewportToWorldPoint(viewportClick);

        return worldClick + new Vector2(designerCamera.orthographicSize, 0);
    }

    static IEnumerator QAI(System.Action a, float t)
    {
        yield return new WaitForSeconds(t);
        a.Invoke();
    }

    /// <summary>
    /// Quick Async called on a given mono behaviour, executed after t seconds
    /// </summary>
    public static void QA(this MonoBehaviour m, System.Action a, float t)
    {
        m.StartCoroutine(QAI(a, t));
    }

    /// <summary>
    /// Quick Async called on RefreshManager, executed after n yield return nulls
    /// </summary>
    public static void QA(System.Action a, int n)
    {
        RefreshManager.i.StartCoroutine(QAIN(a, n));
    }

    static IEnumerator QAIN(System.Action a, int n)
    {
        for (int x = 0; x < n; x++)
        {
            yield return null;
        }

        a.Invoke();
    }

    public static void FadeSR(MonoBehaviour me, SpriteRenderer sr, float time, float to = 0f,
        System.Action onComplete = null)
    {
        me.StartCoroutine(FadeSRI(sr, time, to, onComplete));
    }

    static IEnumerator FadeSRI(SpriteRenderer sr, float time, float to, System.Action onComplete)
    {
        Color col = sr.color;
        for (float t = 1; t < 1 + time; t += Time.deltaTime)
        {
            sr.color = new Color(col.r, col.g, col.b, Mathf.Lerp(sr.color.a, to, 2 * Time.deltaTime * t / time));
            yield return null;
        }

        sr.color = new Color(col.r, col.g, col.b, to);
        onComplete?.Invoke();
    }

    public static void DistributeSprites(List<SpriteRenderer> srs, Sprite[] sprites, float value)
    {
        if (srs.Count == 0 || sprites == null || sprites.Length == 0)
        {
            return;
        }

        value = Mathf.Clamp(value, 0, 1);
        value *= srs.Count;
        int n = 0;
        while (value > 1)
        {
            srs[n].sprite = sprites[^1];
            value -= 1;
            n += 1;
        }

        srs[n].sprite = PercentParameter(sprites, value);
        for (int i = n + 1; i < srs.Count; i++)
        {
            srs[i].sprite = sprites[0];
        }
    }

    public static int Cycle(this int x, int increment, int max, int min = 0)
    {
        int range = max - min + 1; // The '+1' is to include the max in the range
        x += increment;
        x = (x - min) % range;
        if (x < 0) x += range;
        return x + min;
    }

    public static string JustName(string nam)
    {
        if (nam.Contains("("))
        {
            nam = nam.Split("(")[0];
            nam.Remove(nam.Length - 1);
        }

        return nam;
    }

    public static bool CheckAdjacent(Vector2 position, Vector2 pos, bool inclDiagonal = false)
    {
        if (position == pos) return false;
        if (!inclDiagonal)
        {
            if (Mathf.Abs(position.x - pos.x) <= 1 && position.y == pos.y)
            {
                return true;
            }
            else if (position.x == pos.x && Mathf.Abs(position.y - pos.y) <= 1)
            {
                return true;
            }
        }
        else
        {
            if (Mathf.Abs(position.x - pos.x) <= 1 && Mathf.Abs(position.y - pos.y) <= 1)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// OrbMagnet tasks must already be added
    /// </summary>
    public static void QuickMorphWithOrbs(GameObject g, Sprite endGoal, Transform magnetParent = null)
    {
        RefreshManager.i.StartCoroutine(QuickMorphWithOrbsI(g, endGoal, magnetParent));
    }

    static IEnumerator QuickMorphWithOrbsI(GameObject g, Sprite endGoal, Transform magnetParent = null)
    {
        yield return new WaitForSeconds(0.125f);
        if (magnetParent == null) magnetParent = g.transform;
        var morpher = g.AddComponent<SpriteMorpher>();
        morpher.endSprite = endGoal;
        foreach (OrbMagnet om in magnetParent.GetComponents<OrbMagnet>())
        {
            if (om.typ == OrbMagnet.OrbType.Task)
            {
                morpher.oms.Add(om);
            }
        }
    }

    public static Status Stat(Unit u, string typ, float value1, float value2 = 0f)
    {
        int indtyp = Status.GetTypIndex(typ);
        if (u.juggernaut)
        {
            if (indtyp < 5)
            {
                Debug.Log("I'm the juggernaut bitch, can't CC me");
                return null;
            }
        }
        if (indtyp == -1)
        {
            Debug.LogWarning("bad status type: " + typ);
            return null;
        }
        if (u.stati.All(x => x.ind != indtyp))
        {
            Status s = StatusManager.i.statusPool.Get();
            s.SetTyp(indtyp, u, value1, value2);
            return s;
        }
        return u.AddToExistingStatus(indtyp, value1, value2);
        
    }

    public static void RemStat(Unit u, string typ)
    {
        int indtyp = Status.GetTypIndex(typ);
        for (int i = 0; i < u.stati.Count; i++)
        {
            if (u.stati[i].ind != indtyp) continue;
            u.stati[i].Dissapear();
            u.StatusComplete(indtyp);
        }
    }

    //Removes Bad CCs
    public static void RemAllStats(Unit u, bool remBad = true)
    {
        if (remBad)
        {
            for (int x = 0; x < 4; x++)
            {
                for (int i = 0; i < u.stati.Count; i++)
                {
                    if (u.stati[i].ind != x) continue;
                    u.stati[i].Dissapear();
                    u.StatusComplete(x);
                }
            }

            for (int x = 13; x < 17; x++)
            {
                for (int i = 0; i < u.stati.Count; i++)
                {
                    if (u.stati[i].ind != x) continue;
                    u.stati[i].Dissapear();
                    u.StatusComplete(x);
                }
            }
        }
        else
        {
            for (int x = 5; x <= 12; x++)
            {
                for (int i = 0; i < u.stati.Count; i++)
                {
                    if (u.stati[i].ind != x) continue;
                    u.stati[i].Dissapear();
                    u.StatusComplete(x);
                }
            }

            for (int x = 17; x <= 18; x++)
            {
                for (int i = 0; i < u.stati.Count; i++)
                {
                    if (u.stati[i].ind != x) continue;
                    u.stati[i].Dissapear();
                    u.StatusComplete(x);
                }
            }
        }
    }

    public static IEnumerator Animate(SpriteRenderer sr, Sprite[] sprs, float t, bool fullCycle = true)
    {
        for(float x = 0f; x < t; x += Time.deltaTime)
        {
            sr.sprite = PercentParameter(sprs, x / t);
            yield return null;
        }
        if (fullCycle)
        {
            sr.sprite = sprs[0];
        }
    }
    
    public static LTDescr LeanAnimate(this SpriteRenderer sr, Sprite[] sprs, float t, bool fullCycle = false, bool flipped = false)
    {
        if (flipped)
        {
            return LeanTween.value(1, 0, t).setOnUpdate((float x) => sr.sprite = PercentParameter(sprs, x)).setOnComplete(() =>
            {
                if (fullCycle)
                {
                    sr.sprite = sprs[^1];
                }
            });
        }
        else
        {
            return LeanTween.value(0, 1, t).setOnUpdate((float x) => sr.sprite = PercentParameter(sprs, x)).setOnComplete(() =>
            {
                if (fullCycle)
                {
                    sr.sprite = sprs[0];
                }
            });
        }
     
    }
    
    public static LTDescr LeanAnimateFPS(this SpriteRenderer sr, Sprite[] sprs, int FPS, bool fullCycle = false, bool flipped = false)
    {
        if (flipped)
        {
            return LeanTween.value(sr.gameObject,1f, 0f,  (float)sprs.Length / FPS).setOnUpdate((float x) => sr.sprite = PercentParameter(sprs, x)).setOnComplete(() =>
            {
                if (fullCycle)
                {
                    sr.sprite = sprs[^1];
                }
            });
        }
        else
        {
            return LeanTween.value(sr.gameObject,0f, 1f, (float)sprs.Length / FPS).setOnUpdate((float x) => sr.sprite = PercentParameter(sprs, x)).setOnComplete(() =>
            {
                if (fullCycle)
                {
                    sr.sprite = sprs[0];
                }
            });
        }
    }

    public static float TsTA(Vector3 A, Vector3 B)
    {
        Vector3 dir = B - A;
        return Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
    }
    
    
    public static Vector3 TsTV(Vector3 A, Vector3 B)
    {
        return new Vector3(0f, 0f, TsTA(A, B));
    }

    /// <summary>
    /// Normalised * Vector Deg degrees from up, clockwise, * Mag
    /// </summary>
    public static Vector2 VTheta(float deg, float mag = 1f)
    {
        return new Vector2(Mathf.Sin(2 * Mathf.PI * deg / 360), Mathf.Cos(2 * Mathf.PI * deg / 360)) * mag;
    }

    public static bool IsInLayerMask(GameObject obj, LayerMask mask) => (mask.value & (1 << obj.layer)) != 0;
}