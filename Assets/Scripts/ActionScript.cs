using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(Rigidbody2D))]
public class ActionScript : MonoBehaviour
{
    [Tooltip("Influences how easily moved and how much push other enemies")]
    public float mass = 1f;
    public float maxVelocity = 1;
    public float turniness = 0f;
    [Tooltip("How much damage dealt in collisions")]
    public float sharpness = 1f;
    [Tooltip("How quick can damage the same enemy")]
    public float hitRate = 1.25f;
    [Tooltip("Influences lowest cap to take damage in collisions")]
    public float hardness = 1f;
    public float dragCoef = 0.98f;
    [HideInInspector]
    public ProjectileScript PS = null;
    public List<MonoBehaviour> onCollides;
    public List<CC> CCs;
    [HideInInspector]
    public Rigidbody2D rb;
    [HideInInspector]
    public float speedCoef = 1f;

    private Dictionary<int, (Vector2,Vector2,bool)> wallNormals = new(); //contact 1, contact 2, dontDeleteBool
    private Dictionary<int, float> contactIDs = new Dictionary<int, float>();
    private List<(int, float)> recentlyHit = new();
    [HideInInspector]
    public Vector3 force = new Vector2(0, 0);
    [HideInInspector]
    public LifeScript ls;
    [Tooltip("Im on not im: not im gets pushed and damaged")]
    public bool immaterial = false; //can't be hit by anything thats not immaterial except walls, but can still hit others.
    public bool canAct = true; //stops self adding forces, ref for other scripts.
    [HideInInspector]
    public bool velCap = true; //massively slows down lerp to maxVel.
    public bool rooted = false; //stop every frame
    public bool wall = false; //if true, things reflect off this instead of being pushed. If pushed into wall, they are stunned and take damage.
    public bool ignoreWalls = false; //guess
    public bool convertProjectiles = false;
    [Tooltip("Forces projectiles to expend all health")] public bool absorbProjectiles = false;
    public bool pushable = true;
    [Tooltip("Reflect others with 3 x vel")]
    public bool spring = false;
    public bool interactive = true; // only functionality of non interactive rbs are to add forces, and collide with walls (and reflect projectiles if you want)

    public bool prepared = true; //can only still be hit by projectiles but can't collide with other non wall bodies. Used in spawning.
        
   

    [HideInInspector]
    public Color color;
    SpriteRenderer[] sr;


    #region MonoFuncs
    private void Awake()
    {
        sr = GetComponentsInChildren<SpriteRenderer>(true);
        if (sr != null)
        {
            if(sr.Length > 0)
            {
                color = sr[0].color;
            }
        }
        rb = GetComponent<Rigidbody2D>();
        if (TryGetComponent<ProjectileScript>(out var PSp))
        {
            PS = PSp;
        }
        if (TryGetComponent<LifeScript>(out var l))
        {
            ls = l;
        }
    }

    private void FixedUpdate()
    {
        if (rooted)
        {
            return;
        }
        Vector2 vel = rb.velocity + (Vector2)force * Time.fixedDeltaTime / mass;
        if (!PS)
        {
            int id = -1;
            while (true)
            {
                id = -1;
                foreach (var idbuffer in wallNormals.Keys)
                {
                    if (wallNormals[idbuffer].Item3 == false)
                    {
                        id = idbuffer;
                        break;
                    }
                }
                if (id != -1)
                {
                    wallNormals.Remove(id);
                    continue;
                }
                break;
            }
            foreach ((Vector2,Vector2,bool) v in wallNormals.Values)
            {
                vel = RemoveComponent(vel, v.Item1);
                if(v.Item2 != Vector2.zero)
                {
                    vel = RemoveComponent(vel, v.Item2);
                }
            }
            List<int> keys = new List<int>(wallNormals.Keys);
            foreach(int key in keys)
            {
                wallNormals[key] = (wallNormals[key].Item1, wallNormals[key].Item2,false);
            }
        }
        rb.velocity = vel;
        float sqrMag = rb.velocity.sqrMagnitude;
        Vector2 velNorm = rb.velocity.normalized;
        if (sqrMag > 256f)
        {
            rb.velocity = Vector2.Lerp(rb.velocity, maxVelocity * velNorm, 0.25f);
        }
        else if (sqrMag > maxVelocity * maxVelocity)
        {
            rb.velocity = Vector2.Lerp(rb.velocity, maxVelocity * velNorm, 0.075f);
        }
        if (dragCoef != 1)
        {
            rb.velocity *= dragCoef;
        }
        if (PS && force != Vector3.zero)
        {
            PS.ChangeDirection(rb.velocity);
        }
        force = Vector3.zero;
    }

    Vector2 RemoveComponent(Vector2 init, Vector2 component)
    {
        float ang = Vector2.SignedAngle(component, init);
        if(Mathf.Abs(ang) < 90)
        {
            return init;
        }
        else
        {
            if (ang > 0)
            {
                return init.magnitude * Mathf.Sin(Mathf.Deg2Rad * Mathf.Abs(ang)) * new Vector2(-component.y, component.x);
            }
            else
            {
                return init.magnitude * Mathf.Sin(Mathf.Deg2Rad * Mathf.Abs(ang)) * new Vector2(component.y, -component.x);
            }
        }
    }

    private void Update()
    {
        for (int i = 0; i < CCs.Count; i++)
        {
            if (Time.time > CCs[i].expire)
            {
                RemoveCC(CCs[i]);
                break;
            }
        }

        for (int i = 0; i < recentlyHit.Count; i++)
        {
            if(Time.time > recentlyHit[i].Item2 + hitRate)
            {
                recentlyHit.RemoveAt(i);
                i--;
            }
        }
    }
    #endregion

    #region CollisionFuncs
    private void OnCollisionEnter2D(Collision2D collision) //1) conversion, 2) wall collision 3) as long as they're not immaterial or you are immaterial: Add forces based on PS, add to contactIDs, deal dmg if not PS. 4) OnCollides. Wall tags treated as wall.
    {
        if (collision.rigidbody == null)
        {
            return;
        }
        if (collision.rigidbody.TryGetComponent<ActionScript>(out var oAS))
        {
            if (oAS.PS!=null) //IGNORING DADDY
            {
                if (oAS.PS.father == transform)
                {
                    return;
                }
            }
            if (PS != null)
            {
                if (PS.father == collision.rigidbody.transform)
                {
                    return;
                }
            }
        
            if (convertProjectiles && oAS.PS != null) //PROJECTILE REFLECTION (AND ABSORPTION)
            {
                if (oAS.CompareTag(GS.EnemyTag(tag)))
                {
                    if (absorbProjectiles && oAS.ls != null && ls!=null)
                    {
                        float reflectiondmg = Mathf.Min(ls.hp, oAS.ls.hp);
                        ls.Change(-reflectiondmg, oAS.ls.race);
                        ls.LimitCheck();
                        if(ls.hp <= 0)
                        {
                            oAS.ls.hp -= reflectiondmg;
                            oAS.ls.LimitCheck();
                            return;
                        }
                    }
                    oAS.PS.Reflect(true);
                    oAS.PS.father = transform;
                    return;
                }
            }
            if(oAS.convertProjectiles && PS != null)
            {
                if (oAS.CompareTag(GS.EnemyTag(tag)))
                {
                    if (oAS.absorbProjectiles && oAS.ls != null && ls != null)
                    {
                        float reflectiondmg = Mathf.Min(ls.hp, oAS.ls.hp);
                        oAS.ls.Change(-reflectiondmg, ls.race);
                        oAS.ls.LimitCheck();
                        if (oAS.ls.hp <= 0)
                        {
                            ls.hp -= reflectiondmg;
                            ls.LimitCheck();
                            return;
                        }
                    }
                    PS.Reflect(true);
                    PS.father = oAS.transform;
                    return;
                }
            }
            if (oAS.wall) //STOPPING VELOCITY TOWARD WALLS (via wallNormals)
            {
                if (!ignoreWalls)
                {
                    AddWall(collision); 
                }
            }
            else //NON-WALL INTERACTION
            {
                if (!oAS.interactive || !interactive || !prepared)
                {
                    return;
                }
                if (!(!oAS.immaterial && immaterial)) // PUSHING SELF (via contactIDs, unless you're immaterial and they're not or parenting)
                {
                    if (PS == null && oAS.PS == null && !oAS.transform.IsChildOf(transform) && !transform.IsChildOf(oAS.transform))
                    {
                        int key = collision.rigidbody.GetInstanceID();
                        if (!contactIDs.ContainsKey(key))
                        {
                            contactIDs.Add(collision.rigidbody.GetInstanceID(), oAS.mass * oAS.mass);
                        }
                    }
                }
                if (!(oAS.immaterial && !immaterial)) // PUSHING OTHER BODY (unless they're immaterial and you're not)
                {
                    if (PS != null)
                    {
                        if (collision.rigidbody.CompareTag(tag))
                        {
                            if (PS.damage > 0)
                            {
                                return;
                            }
                        }
                        else
                        {
                            oAS.AddPush(0.5f, true, rb.velocity.normalized * PS.push);
                        }
                    }
                    else
                    {
                        oAS.TryAddForce(mass * collision.relativeVelocity.magnitude * -collision.GetContact(0).normal * 30f, false);
                    }
                    if (PS == null && oAS.PS == null && !wall) //DAMAGING OTHER BODY
                    {
                        int oASID = oAS.GetInstanceID();
                        bool damage = true;
                        for (int i = 0; i < recentlyHit.Count; i++)
                        {
                            if (recentlyHit[i].Item1 == oASID)
                            {
                                damage = false;
                            }  
                        }
                        if (damage)
                        {
                            recentlyHit.Add((oASID,Time.time));
                            float dmg = sharpness;
                            // float dmg = 0.35f * sharpness * mass / oAS.mass;
                            // if(immaterial && !oAS.immaterial)
                            // {
                            //     dmg *= Mathf.Max(1,1.75f * rb.velocity.magnitude);
                            // }
                            // else
                            // {
                            //     dmg *= collision.relativeVelocity.magnitude;
                            // }
                            if (collision.rigidbody.CompareTag(tag))
                            {
                                if (!CheckCCs(new string[] { "push" }))
                                {
                                    return;
                                }
                                dmg *= 0.5f; //deal half damage to allies if being pushed.
                            }
                            if (transform.CompareTag("Misc"))
                            {
                                return;
                            }
                            if (GetLifeScript(collision, out var oLS)) //deal damage if above their "hardness" threshold, scaled by their shockresistance and your sharpness.
                            {
                                if (oLS.hasDied)
                                {
                                    return;
                                }
                                if (dmg > oAS.hardness)
                                {
                                    dmg -= oAS.hardness;
                                    if (ls != null)
                                    {
                                        oLS.Change(-dmg, ls.race);
                                    }
                                    else
                                    {
                                        oLS.Change(-dmg, 0);
                                    }
                                }
                            }
                        }
                        oAS.AddPush(0.6f, true, Vector2.zero);
                    }
                }
            }
        }
        else
        {
            if (PS == null)
            {
                if (!ignoreWalls)
                {
                    if (collision.rigidbody.CompareTag("Walls")) //add walls that have no AS 
                    {
                        AddWall(collision);
                    }
                }
            }
        }
        OnCollides(collision); //only for valid collisions. For ally collisions only applies if pushed.
    }

    private void AddWall(Collision2D coli)
    {
        int ID = coli.collider.GetInstanceID();
        if (!wallNormals.ContainsKey(ID))
        {
            if(coli.contacts.Length == 1)
            {
                wallNormals.Add(ID, (coli.GetContact(0).normal, Vector2.zero,true));
            }
            else
            {
                if(Vector2.Angle(coli.GetContact(1).normal,coli.GetContact(0).normal) <= 135f)
                {
                    wallNormals.Add(ID, (coli.GetContact(0).normal, coli.GetContact(1).normal, true));
                }
                else
                {
                    wallNormals.Add(ID, (coli.GetContact(0).normal, Vector2.zero, true));
                }
            }
            TryAddForce(Reflect(mass * rb.velocity.magnitude * 1.5f * coli.GetContact(0).normal), false);
        }
    }

    bool GetLifeScript(Collision2D c, out LifeScript tl)
    {
        if (c.gameObject.TryGetComponent<LifeScript>(out var l))
        {
            tl = l;
            return true;
        }
        tl = c.collider.attachedRigidbody.GetComponent<LifeScript>();
        return tl != null;
    }
    private void OnCollisionStay2D(Collision2D collision) //MOVE AWAY FROM OTHER UNITS YOU'RE STILL IN CONTACT WITH AND READJUST NORMALS
    {
        if (rooted)
        {
            return;
        }
        if(PS == null && collision.rigidbody != null)
        {
            var id = collision.rigidbody.GetInstanceID();
            if (contactIDs.ContainsKey(id))
            {
                TryAddForce(3 * contactIDs[id] * collision.GetContact(0).normal, false); 
                contactIDs[id] *= 1 + Time.fixedDeltaTime;
            }
            else 
            {
                id = collision.collider.GetInstanceID();
                if (wallNormals.ContainsKey(id))
                {
                    if(collision.contacts.Length == 1)
                    {
                        wallNormals[id] = (collision.GetContact(0).normal,Vector2.zero,true);
                    }
                    else //ADDS A SECOND "BEST" OTHER NORMAL TO TUPLE IF THERE ARE TWO CONTACTS
                    {
                        float bestAngle = 0f;
                        Vector2 norm = Vector2.zero;
                        Vector2 oNorm = collision.GetContact(0).normal;
                        foreach(var p in collision.contacts)
                        {
                            float ang = Vector2.Angle(p.normal, oNorm);
                            if(ang > bestAngle && ang < 135)
                            {
                                norm = p.normal;
                                bestAngle = ang;
                            }
                        }
                        wallNormals[id] = (collision.GetContact(0).normal, norm,true);
                    }
                    TryAddForce(collision.GetContact(0).normal * mass, false);
                }
            }
        }
    }

    private void OnCollisionExit2D(Collision2D collision) //CLEAN UP LISTS
    {
        if (PS == null && collision.rigidbody!=null)
        {
            var id = collision.rigidbody.GetInstanceID();
            if (contactIDs.ContainsKey(id))
            {
                contactIDs.Remove(id);
            }
            //else 
            //{
            //    id = collision.collider.GetInstanceID(); //removal has been taken of (and made more robust) through addition of third boolean tuple parameter in wallNormals
            //    if (wallNormals.ContainsKey(id))
            //    {
            //        wallNormals.Remove(id);
            //    }
            //}
        }
    }
    #endregion collisionFuncs

    #region ForceFuncs
    public bool TryAddForce(Vector2 forceP, bool self)
    {
        if(forceP == Vector2.zero) { return false; } 
        if(pushable || self)
        {
            if (!rooted && (canAct || !self))
            {
                forceP *= speedCoef;
                if (turniness != 0 && self)
                {
                    float turn = forceP.x * rb.velocity.x + forceP.y * rb.velocity.y; //dot product
                    if (turn != 0)
                    {
                        turn /= forceP.magnitude * rb.velocity.magnitude; //cos theta
                        forceP *= (float)Math.Pow(2 - turn, turniness); // (2 - cos(theta))^turniness. For turniness == 1: gives 2 * multiplier for 90deg and 3* multi for 180 deg.
                    }
                }

                if (!self && (forceP.x > 1000f * mass || forceP.y > 1000f * mass))
                {
                    StartCoroutine(OffVelCapForT(1f));
                }
                force += (Vector3)forceP;
                return true;
            }
            return false;
        }
        return false;
        
    }

    public void AddPush(float duration, bool negative, Vector2 forceP)
    {
        if(pushable || !negative)
        {
            forceP *= 2; //now i've set to fixed update
            StartCoroutine(AddPushE(duration, negative, forceP));
        }
    }

    public IEnumerator AddPushE(float duration, bool negative, Vector2 forceP)
    {
        if (negative)
        {
            AddCC("push", duration, -0.75f, false);
        }
        while (duration > 0f)
        {
            duration -= Time.fixedDeltaTime;
            TryAddForce(forceP,!negative);
            yield return new WaitForFixedUpdate();
        }
    }

    //private void WallCollision(Collision2D collision, bool spring = false)
    //{
    //    if (PS == null)
    //    {
    //        if(collision.contactCount > 0)
    //        {
    //            rb.velocity = Reflect(collision.GetContact(0).normal, true,spring);
    //        }
    //    }
    //}

    public Vector3 Reflect(Vector2 n, bool change = true, bool spring = false) //irrespective of pushable
    {
        n.Normalize();
        Vector2 vel = rb.velocity;
        float dot = vel.x * n.x + vel.y * n.y;
        float x = vel.x - 2 * dot * n.x;
        float y = vel.y - 2 * dot * n.y;
        if (spring)
        {
            x *= 2;
            y *= 2;
            StartCoroutine(OffVelCapForT());
        }
        if (change)
        {
            rb.velocity = new Vector3(x, y, 0f);
        }
        return new Vector3(x, y, 0f);
    }

    public IEnumerator OffVelCapForT(float t = 0.5f)
    {
        if(velCap == false)
        {
            yield break;
        }
        velCap = false;
        yield return new WaitForSeconds(t);
        velCap = true;
    }
    
    
    
    private void OnCollides(Collision2D collision)
    {
        if(collision.rigidbody == null)
        {
            return;
        }
        for(int i = 0; i < onCollides.Count; i++)
        {
            if(onCollides[i] != null)
            {
                ((IOnCollide)onCollides[i]).OnCollide(collision);
            }
        }
    }
    
    
    
    public void Stop(int stopAll = 0)
    {
        force = Vector2.zero;
        rb.velocity = Vector2.zero;
        if (stopAll != 0)
        {
            StopAllCoroutines();
        }
    }

    public void Decelerate(float t, float x)
    {
        StartCoroutine(DecelerateI(t, x));
    }

    /// <summary>
    /// Decelerate for 0.1 * t seconds, with each deceleration multiply vel by x.
    /// </summary>
    private IEnumerator DecelerateI(float t, float x)
    {
        for (int i = 0; i < Mathf.RoundToInt(10 * t); i++)
        {
            if (CheckCCs(new string[] { "stun", "root" }) == false)
            {
                rb.velocity *= x;
                i++;
                yield return new WaitForSeconds(0.1f);
            }
            else
            {
                yield break;
            }
        }
    }

    public void FaceDirectionOverT(Vector2 dir, float t, float coef = 5f)
    {
        if (canAct)
        {
            dir = dir.normalized;
            StartCoroutine(FaceDirectionOverTI(dir, t, coef));
        }
    }

    public IEnumerator FaceDirectionOverTI(Vector2 dir, float t, float coef)
    {
        while (t > 0f)
        {
            t -= Time.fixedDeltaTime;
            transform.up = Vector2.Lerp(transform.up, dir, coef * Time.fixedDeltaTime * 0.7f);
            yield return new WaitForFixedUpdate();
        }
    }

    public void RandDirectionOverT(float t, float changeCoef, float spinCoef = 5f)
    {
        if (canAct)
        {
            StartCoroutine(RandDirectionOverTI(t, changeCoef, spinCoef));
        }
    }

    // x = sin (theta), y = cos(theta)
    private IEnumerator RandDirectionOverTI(float t, float changeCoef, float spinCoef)
    {
        float angle = Mathf.Asin(transform.up.x) + UnityEngine.Random.Range(-2 * Mathf.PI * changeCoef, 2 * Mathf.PI * changeCoef);
        Vector2 dir = new(Mathf.Sin(angle), Mathf.Cos(angle));
        while (t > 0f)
        {
            transform.up = Vector2.Lerp(transform.up, dir, spinCoef * Time.fixedDeltaTime * 0.5f);
            yield return new WaitForFixedUpdate();
            t -= Time.fixedDeltaTime;
        }
    }


    /// <summary>
    /// coef is multiplied by delta time in lerp op every frame
    /// </summary>
    public void FaceEnemyOverT(float t, float coef, Transform t2, bool randIfNull, bool invert = false)
    {
        if (canAct)
        {
            StartCoroutine(FaceEnemyOverTI(t, coef, t2, randIfNull, invert));
        }
    }

    private IEnumerator FaceEnemyOverTI(float t, float coef, Transform t2, bool randIfNull = true, bool invert = false)
    {
        if (t2 == null)
        {
            if (randIfNull)
            {
                Vector2 dir = UnityEngine.Random.insideUnitCircle.normalized;
                while (t > 0f)
                {
                    if (CheckCCs(new string[] { "stun", "root" }) == false)
                    {
                        transform.up = Vector2.Lerp(transform.up, dir, Mathf.Min(1, coef * Time.fixedDeltaTime * 0.5f));
                    }
                    t -= Time.fixedDeltaTime;
                    yield return new WaitForFixedUpdate();
                }
            }
        }
        else
        {
            while (t > 0f)
            {
                if (t2 != null)
                {
                    if (canAct)
                    {
                        if (invert)
                        {
                            transform.up = Vector2.Lerp(transform.up, (transform.position - t2.position).normalized, Mathf.Min(1, coef * Time.fixedDeltaTime * 0.7f));
                        }
                        else
                        {
                            transform.up = Vector2.Lerp(transform.up, (t2.position - transform.position).normalized, Mathf.Min(1, coef * Time.fixedDeltaTime * 0.7f));
                        }
                    }
                    else
                    {
                        yield break;
                    }
                }
                else
                {
                    yield break;
                }
                t -= Time.fixedDeltaTime;
                yield return new WaitForFixedUpdate();
            }
        }
    }

    public bool CheckWall(Transform t, bool inclParent = false)
    {
        if (t.TryGetComponent<ActionScript>(out var AS))
        {
            if (AS.wall)
            {
                return true;
            }
        }
        else if (t.CompareTag("Walls"))
        {
            return true;
        }
        else if (inclParent)
        {
            if (t.GetComponentInParent<ActionScript>() != null)
            {
                if (t.GetComponentInParent<ActionScript>().wall)
                {
                    return true;
                }
            }
        }
        return false;
    }

    #endregion

    #region CCs
    public void AddCC(string ccName, float duration, float value, bool colour = true)
    {
        ccName = ccName.ToLower();
        CCs.Add(new CC(ccName,duration,value,colour));
        if (ccName == "slow")
        {
            if (colour)
            {
                ColourAll(Color.yellow, 0.5f * (1 - value));
            }
            speedCoef *= value;
            rb.velocity *= speedCoef;
        }
        else if (ccName == "stun")
        {
            canAct = false;
            if (colour)
            {
                ColourAll(Color.yellow, 0.5f);
            }
            if (PS)
            {
                PS.startDirection = rb.velocity;
            }
            Stop();
        }
        else if (ccName == "root")
        {
            if (colour)
            {
                ColourAll(Color.red, 0.5f);
            }
            if (PS)
            {
                PS.startDirection = rb.velocity;
            }
            rooted = true;
        }
        else if (ccName == "speed")
        {
            if (colour)
            {
                ColourAll(Color.green, Mathf.Min(1, 1 + Mathf.Log(value)));
            }
            speedCoef *= value;
            rb.velocity *= speedCoef;
            maxVelocity *= speedCoef;
        }
        else if (ccName == "mass")
        {
            if (colour)
            {
                if (value > 1)
                {
                    ColourAll(Color.grey, Mathf.Min(1, 1 + Mathf.Log(value)));
                }
                else
                {
                    ColourAll(Color.magenta, 1-value);
                }
            }
            mass *= value;
        }
        else if (ccName == "restrict")
        {
            if (colour)
            {
                ColourAll(Color.red, 0.5f);
            }
            canAct = false;
        }
    }

    private void ColourAll(Color col, float t)
    {
        foreach(SpriteRenderer s in sr)
        {
            s.color = Color.Lerp(s.color, col, t);
        }
    }

    void RemoveCC(CC c)
    {
        CCs.Remove(c);
        if (c.name == "slow")
        {
            speedCoef /= c.value;
            if (speedCoef > 0.99f && speedCoef < 1.01f)
            {
                speedCoef = 1f;
            }
            if (PS)
            {
                rb.velocity = PS.startDirection * speedCoef;
            }
        }
        else if (c.name == "speed")
        {
            speedCoef /= c.value;
            if (speedCoef > 0.99f && speedCoef < 1.01f)
            {
                speedCoef = 1f;
            }
            maxVelocity /= c.value;
            if (PS)
            {
                rb.velocity = PS.startDirection * speedCoef;
            }
        }
        else if (c.name == "stun" && !CheckCCs(new string[] { "stun", "restrict" }))
        {
            canAct = true;
        }
        else if (c.name == "mass")
        {
            mass /= c.value;
        }
        else if (c.name == "root" && !CheckCCs(new string[] { "root"}))
        {
            rooted = false;
        }
        else if (c.name == "restrict" && !CheckCCs(new string[] { "restrict", "stun" }))
        {
            canAct = true;
        }
        else if (PS && c.name == "push")
        {
            PS.ChangeDirection(rb.velocity);
        }
        if (c.colour)
        {
            if (CCs.Count == 0)
            {
                ColourAll(color, 1);
            }
        }
    }

    public bool CheckCCs(string[] ccs)
    {
        foreach (string c in ccs)
        {
            foreach(CC cc in CCs)
            {
                if(cc.name == c)
                {
                    return true;
                }
            }
        }
        return false;
    }

    [Serializable]
    public struct CC
    {
        public CC(string nameP, float durationP, float valueP, bool colourP)
        {
            name = nameP;
            expire = Time.time + durationP;
            value = valueP;
            colour = colourP;
        }

        public string name { get; }
        public float expire { get; }
        public float value { get; }
        public bool colour { get; }
    }

    #endregion

}