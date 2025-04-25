using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class OnCollideCC : MonoBehaviour, IOnCollide
{
    public int select = 1;
    public void OnCollide(Collision2D collision)
    {
        if (collision.rigidbody.TryGetComponent<ActionScript>(out var AS))
        {
            if (select == 1)
            {
                if (collision.collider.CompareTag(tag))
                {
                    Debug.Log("ally oncollide");
                    AS.AddCC("speed", 2f, 1.5f);
                    AS.AddCC("mass", 2f, 1.25f);
                }
                else if (collision.collider.CompareTag(GS.EnemyTag(tag)))
                {
                    Debug.Log("enemy oncollide");
                    AS.AddCC("slow", 2f, 0.7f);
                    AS.AddCC("stun", 0.25f, -1f);
                    AS.AddCC("mass", 2f, 0.75f);
                }
            }
            else if (select == 0)
            {
                if (collision.collider.CompareTag(GS.EnemyTag(tag)))
                {
                    AS.AddCC("stun", 1f, -1f);
                }
            }
        }
    }
}
