using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AnimateScript : MonoBehaviour
{
    [HideInInspector]
    public Animator anim;
    [HideInInspector]
    public Rigidbody2D rb;
    void Awake()
    {
        rb = GetComponentInParent<Rigidbody2D>();
        anim = GetComponent<Animator>();
    }
    
    void Update()
    {
        anim.SetFloat("Horizontal",rb.velocity.normalized.x);
        anim.SetFloat("Vertical",rb.velocity.normalized.y);
    }
    
    public void Death()
    {
        Destroy(transform.parent.gameObject);
    }
}
 