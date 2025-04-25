using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Gravity : MonoBehaviour
{
    public Vector2 dir;
    public ActionScript AS;
    public float force = 1f;
    public float initT = 0.5f;
    public float dTincrement = 0.1f;
    public enum Typ {Curve,Back};
    public Typ typ;
    private float time = 100000000;

    void Start()
    {
        time = Time.time;
        if (dir == Vector2.zero)
        {
            if(typ == Typ.Curve)
            {
                dir = new Vector2(-transform.up.x * Random.Range(0.2f, 1.1f), -transform.up.y).normalized;
            }
            else if (typ == Typ.Back)
            {
                dir = -transform.up;
            }
        }
    }

    private void Update()
    {
        if(Time.time > time + initT)
        {
            AS.TryAddForce(force * dir, true);
            force += dTincrement * Time.deltaTime;
        }
    }
}
