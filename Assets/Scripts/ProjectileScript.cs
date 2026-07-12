using UnityEngine;
using System;
using System.Collections.Generic;

[RequireComponent(typeof(ActionScript))]
public class ProjectileScript : MonoBehaviour, IOnCollide
{
    public float damage = 1f;
    public int pierce = 1;
    private int hit = 0;
    public float speed = 2f;
    public float timer = 1f;
    public float push = 0f; //called from AS.
    public float angle = 0f;
    public float angVel = 0f;
    [HideInInspector]
    public EntityId[] enemiesHit;
    protected LifeScript lifeScript;
    Rigidbody2D rb;
    public Transform father;
    public Vector2 startDirection;
    ActionScript AS;
    public bool onlyChar = false;
    private ActionScript oAS;

    void Awake()
    {
        AS = GetComponent<ActionScript>();
        if (!AS.onCollides.Contains(this))
        {
            AS.onCollides.Add(this);
        }
        rb = GetComponent<Rigidbody2D>();
        lifeScript = GetComponent<LifeScript>();
        if (lifeScript != null)
        {
            if(lifeScript.hp == 1f)
            {
                lifeScript.maxHp = Mathf.Abs(damage) * pierce;
                lifeScript.hp = Mathf.Abs(damage) * pierce;
            }
        }
        enemiesHit = new EntityId[pierce + 1];
        enemiesHit[0] = GetComponent<Rigidbody2D>().GetEntityId();
        angVel *= GS.PlusMinus();
    }

    void Update()
    {
        if (angVel != 0)
        {
            transform.rotation = Quaternion.Euler(0f, 0f, transform.eulerAngles.z + angVel*Time.deltaTime);
        }
        if (timer <= 0f || hit == pierce)
        {
            if (lifeScript != null)
            {
                lifeScript.OnDie();
            }
            else if (!SpawnManager.TryReleaseProjectile(gameObject))
            {
                Destroy(gameObject);
            }
        }
        else if(timer < 10000)
        {
            timer -= Time.deltaTime;
        }
    }

    void FixedUpdate()
    {
        // Reflect off the mine ore. Tiles carry no physics colliders (kinematic bodies ignore them), so
        // the wall-bounce that OnCollide does for tagged "Walls" is polled in code here — over the
        // segment the body is ABOUT to travel this step, so the bounce lands at the wall face (never a
        // frame spent inside the rock) and fast rounds can't tunnel through thin walls.
        if (MineField.i == null || AS == null || AS.ignoreWalls || rb == null) return;
        Vector2 vel = rb.linearVelocity;
        if (vel.sqrMagnitude < 0.000001f) return;
        Vector2 from = rb.position;
        Vector2 to = from + vel * Time.fixedDeltaTime;
        if (MineField.i.TryReflectProjectile(from, to, vel, out Vector2 newDir, out Vector2 correctedPos))
        {
            rb.position = correctedPos;
            ChangeDirection(newDir);
        }
    }

    public void SetValues(Vector2 direction, string tagP, float strength = 0, Transform t = null)
    {
        if (!gameObject.activeInHierarchy)
        {
            Awake();
        }
        if (t != null)
        {
            father = t;
            gameObject.SetActive(true);
        }
        if (!CompareTag(tagP))
        {
            tag = tagP;
            gameObject.layer = LayerMask.NameToLayer(CompareTag("Allies") ? "Ally Projectiles" : "Enemy Projectiles");
        }
        direction.Normalize();
        if(strength != 0f)
        {
            speed *= 1 + strength * 0.1f;
            float absStrength = Mathf.Abs(strength);
            AS.maxVelocity *= 1 + absStrength * 0.1f;
            AS.mass *= 1 + absStrength * 0.1f;
            push *= 1 + absStrength * 0.1f;
            damage *= 1 + absStrength * 0.1f;
        }
        rb.linearVelocity = direction * speed;
        startDirection = direction;
        transform.up = direction;
        if (angle != 0)
        {
            transform.Rotate(Vector3.forward, angle);
        }
    }

    public void ChangeDirection(Vector2 direction)
    {
        if (!AS.rooted)
        {
            direction.Normalize();
            rb.linearVelocity = direction * rb.linearVelocity.magnitude;
            startDirection = direction;
            transform.up = direction;
            if (angle != 0)
            {
                transform.Rotate(Vector3.forward, angle);
            }
        }
    }

    public void Reflect(bool convert)
    {
        ChangeDirection(-startDirection);
        if(convert)
        {
            Convert();
        }
    }

    public void Convert()
    {
        if (CompareTag("Enemies"))
        {
            tag = "Allies";
            transform.parent = GS.FindParent(GS.Parent.allyprojectiles);
            gameObject.layer = LayerMask.NameToLayer("Ally Projectiles");

        }
        else if (CompareTag("Allies"))
        {
            tag = "Enemies";
            transform.parent = GS.FindParent(GS.Parent.enemyprojectiles);
            gameObject.layer = LayerMask.NameToLayer("Enemy Projectiles");
        }
    }

    public virtual void OnCollide(Collision2D coli) 
    {
        if (onlyChar && coli.collider.name != "Character" || coli.rigidbody == null || enemiesHit == null)
        {
            return;
        }
        if (hit < pierce)
        {
            oAS = null;
            if (Array.IndexOf(enemiesHit, coli.rigidbody.GetEntityId()) == -1)
            {
                if (coli.rigidbody.TryGetComponent<ActionScript>(out var actionScript))
                {
                    oAS = actionScript;
                    if (actionScript.wall)
                    {
                        if (!AS.ignoreWalls)
                        {
                            ChangeDirection(AS.Reflect(coli.GetContact(0).normal,false));
                            return;
                        }
                        else
                        {
                            return;
                        }
                    }
                    else
                    {
                        if (AS.pushable)
                        {
                            if (!(AS.immaterial && !actionScript.immaterial))//as long as not the case where proj. is immaterial and other is not immaterial.
                            {
                                ChangeDirection(Vector2.Lerp(AS.rb.linearVelocity, AS.Reflect(coli.GetContact(0).normal,false), actionScript.mass / (AS.mass + actionScript.mass)));
                            }
                        }
                    }
                    if (!AS.immaterial && actionScript.immaterial) //if case where proj is not immaterial and other is immaterial, no damage dealt (but still bounce).
                    {
                        return;
                    }
                }
                else
                {
                    if (coli.rigidbody.CompareTag("Walls") && !AS.ignoreWalls)
                    {
                        ChangeDirection(AS.Reflect(coli.GetContact(0).normal,false));
                        return;
                    }
                    else
                    {
                        return;
                    }
                }
                float dmg = damage;
                if ((coli.transform.CompareTag(GS.EnemyTag(tag)) && damage >= 0f) || (coli.transform.CompareTag(tag) && damage < 0f))
                {
                    if (coli.transform.TryGetComponent<LifeScript>(out var ls))
                    {
                        if (ls.hasDied)
                        {
                            return;
                        }
                        if(oAS.absorbProjectiles == true)
                        {
                            dmg = Mathf.Min(ls.hp, lifeScript.hp);
                        }
                        ls.Change(-dmg, lifeScript.race);
                    }
                }
                if(coli.rigidbody != null)
                {
                    enemiesHit[hit] = coli.rigidbody.GetEntityId();
                }
                lifeScript.hp -= dmg;
                lifeScript.LimitCheck();
                hit++;
                if (hit == pierce)
                {
                    lifeScript.OnDie();
                }
            }
        }
    }

    public void DestroyImmed()
    {
        if (!SpawnManager.TryReleaseProjectile(gameObject))
        {
            Destroy(gameObject);
        }
    }

    /// <summary>Pool-reuse seam: restart the flight bookkeeping a fresh Instantiate would get
    /// from Awake — hit counter and victim list (slot 0 stays this body's own id).</summary>
    public void ResetFlight()
    {
        hit = 0;
        if (enemiesHit != null)
        {
            for (int i = 1; i < enemiesHit.Length; i++)
            {
                enemiesHit[i] = default;
            }
        }
    }
}
