using System.Collections;
using System.Collections.Generic;
using UnityEngine;
public class Pusher : MonoBehaviour
{
    public bool convert = false;
    public float duration = 1f;
    public float force = 1f;
    private Rigidbody2D rb;
    private List<EntityId> cols = new List<EntityId>();

    private void Awake()
    {
        rb = GetComponentInParent<Rigidbody2D>();
    }
    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag(GS.EnemyTag(tag)))
        {
            if (cols.Contains(collision.GetEntityId()))
            {
                return;
            }
            cols.Add(collision.GetEntityId());
            if (collision.GetComponentInParent<ActionScript>() != null)
            {
                collision.GetComponentInParent<ActionScript>().AddPush(duration, true, rb.linearVelocity.normalized * force);
            }
            if (convert)
            {
                if (collision.TryGetComponent<ProjectileScript>(out var ps))
                {
                    ps.Convert();
                }
            }
        }
    }

    private void OnCollide(Collision2D collisionP)
    {
        if (collisionP.collider.CompareTag(GS.EnemyTag(tag)))
        {
            var collision = collisionP.collider;
            if (cols.Contains(collision.GetEntityId()))
            {
                return;
            }
            cols.Add(collision.GetEntityId());
            if (collision.GetComponentInParent<ActionScript>() != null)
            {
                collision.GetComponentInParent<ActionScript>().AddPush(duration, true, rb.linearVelocity.normalized * force);
            }
            if (convert)
            {
                if (collision.TryGetComponent<ProjectileScript>(out var ps))
                {
                    ps.Convert();
                }
            }
        }
    }
}
