using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Spin : MonoBehaviour
{
    public Vector2 angVel;
    private Vector3 rot;

    private void Awake()
    {
        rot = new Vector3(0f, 0f, Random.Range(angVel.x,angVel.y));
    }
    // Update is called once per frame
    void Update()
    {
        transform.Rotate(rot * Time.deltaTime);
    }

    public IEnumerator StopSpinning()
    {
        while(rot.z > 2f)
        {
            rot.z = Mathf.Lerp(rot.z, 0f, 3 * Time.deltaTime);
            yield return null;
        }
        Destroy(this);
    }
}
