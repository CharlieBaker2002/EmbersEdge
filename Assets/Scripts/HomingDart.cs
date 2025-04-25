using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HomingDart : MonoBehaviour
{
    private float t;
    [SerializeField] private float size = 1f;
    [SerializeField] private GameObject FX;
    [SerializeField] private Seeking s;
    [SerializeField] private float damage;
    private void Start()
    {
        s.dist = 10;
        t = Time.time;
    }

    // Update is called once per frame
    void Update()
    {
        transform.localScale = size*(1-Mathf.Sin(0.5f*(Time.time - t))) * Vector3.one;
        if (s.dist < 0.6f)
        {
            if (s.target != null)
            {
                transform.position = s.target.position;
                s.target.GetComponent<LifeScript>().Change(-damage,0);
            }
            Instantiate(FX, transform.position, transform.rotation,GS.FindParent(GS.Parent.fx));
            Destroy(gameObject);
            return;
        }

        if (Time.time-t > 2f)
        {
            Destroy(gameObject);
        }
    }
}
