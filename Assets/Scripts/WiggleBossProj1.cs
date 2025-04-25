using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WiggleBossProj1 : MonoBehaviour
{
    private float t;
    [SerializeField] private Animator anim;
    [SerializeField] private ActionScript AS;
    [SerializeField] private ProjectileScript ps;
    private bool turned = false;
    public static float speed = 1f;

    [SerializeField] private SpriteRenderer sr;
    [SerializeField] private Material changeMat;
    void Start()
    {
        t = Time.time;
    }
    // Update is called once per frame
    void Update()
    {
        if (!(Time.time > 1.5f + t)) return;
        if (turned)
        {
            Destroy(gameObject);
            return;
        }
        turned = true;
        t = Time.time + 3f;
        anim.SetBool(Random.Range(0,2) == 0 ? "left" : "right",true);
    }

    public void PushLeft()
    {
        ps.angle = -45f;
        sr.material = changeMat;
        AS.TryAddForce(speed * 100f * new Vector2(-0.5f, 0.5f).Rotated(transform.rotation.eulerAngles.z),true);
    }

    public void PushRight()
    {
        ps.angle = 45f;
        sr.material = changeMat;
        AS.TryAddForce(speed * 100f * new Vector2(0.5f, 0.5f).Rotated(transform.rotation.eulerAngles.z),true);
    }
}
