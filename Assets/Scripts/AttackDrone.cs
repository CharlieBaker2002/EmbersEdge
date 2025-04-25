using UnityEngine;
public class AttackDrone : ProjectileScript
{
    private Transform T;
    [SerializeField] Collider2D col;
    [HideInInspector]public float acceleration = 3f;
    //Move Towards An Enemy Indefinitely
    public void Attack(Transform t)
    {
        enabled = true;
        col.enabled = true;
        T = t;
    }
    
    void Update()
    {
        if (T == null)
        {
            lifeScript.OnDie();
            enabled = false;
            return;
        }
        speed += Time.deltaTime;
        var position = transform.position;
        var position1 = T.position;
        position = Vector2.MoveTowards(position, position1, speed * Time.deltaTime);
        var transform2 = transform;
        var transform1 = transform2;
        transform1.position = position;
        transform1.up = Vector2.Lerp(transform2.up, ((Vector2)(position1 - position)).Rotated(45f), acceleration * Time.deltaTime);
    }
}
