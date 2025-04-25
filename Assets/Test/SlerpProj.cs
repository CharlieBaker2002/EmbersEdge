using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SlerpProj : MonoBehaviour
{
    float speed;
    float timer;
    private Vector3[] positions = new Vector3[5];
    public bool up;
    [NonReorderable]
    public Vector3[] deltas; //RAND x -x, RAND y z
    public float[] ts;
    bool startedCont = false;

    void Start()
    {
        timer = Time.time + Random.Range(1.5f, 2.5f);
        positions[0] = transform.position;
        speed = Random.Range(1f, 1.4f);
        if (!up)
        {
            for(int j = 0; j < deltas.Length; j+= 1)
            {
                deltas[j] = new Vector3(deltas[j].x, -deltas[j].y, -deltas[j].z);
            }
        }
        for (int i = 1; i < 5; i++)
        {
            positions[i] = positions[i - 1] + new Vector3(Random.Range(deltas[i - 1].x, -deltas[i - 1].x), Random.Range(deltas[i - 1].y, deltas[i - 1].z));
        }
    }

    // Update is called once per frame
    void Update()
    {
        if(Time.time - timer < 0f)
        {
            transform.position = Vector2.Lerp(transform.position, positions[1], Time.deltaTime * speed);
        }
        else if(Time.time - timer < ts[0])
        {
            transform.position = Vector3.Slerp(transform.position, positions[2], Time.deltaTime * speed);
        }
        else if (Time.time - timer < ts[1])
        {
            transform.position = Vector3.Slerp(transform.position, positions[3], Time.deltaTime * speed);
        }
        else if (Time.time - timer < ts[2])
        {
            transform.position = Vector3.Slerp(transform.position, positions[4], Time.deltaTime * speed);
        }
        else
        {
            if (!startedCont) { StartCoroutine(Continue()); startedCont = true; }
        }
    }

    IEnumerator Continue()
    {
        Vector3 vel = (positions[4] - positions[3]).normalized;
        float t = 2.5f;
        while(t > 0f)
        {
            t -= Time.deltaTime;
            transform.position += t * Time.deltaTime * vel;
            yield return null;
        }
    }
}
