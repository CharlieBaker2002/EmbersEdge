using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GrowScript : MonoBehaviour
{
    public float coef = 2;
    public float switchTime = 1;
    private float initialcoef;

    private void Start()
    {
        initialcoef = coef;
    }
    void FixedUpdate()
    {
        if(switchTime != 1f)
        {
            switchTime -= Time.deltaTime;
            coef = initialcoef * switchTime;
        }
        transform.localScale = (transform.localScale.magnitude + coef) * transform.localScale.normalized;
        if(transform.localScale.magnitude < 0.5f)
        {
            Destroy(gameObject);
        }
    }
}
