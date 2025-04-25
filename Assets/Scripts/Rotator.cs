using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Rotator : MonoBehaviour
{
    public float omega = 180f;
    public bool deltaT = true;
    public bool started = true;
    [SerializeField] bool randStartThingy = true;
    public bool rand = false;
    public Transform extra;
    public float offset;

    private void Awake()
    {
        if (rand)
        {
            omega = Random.Range(-omega, omega);
        }
        if (randStartThingy)
        {
            if(omega < 30 && omega > 0)
            {
                omega += 30;
            }
            else if(omega > -30 && omega < 0)
            {
                omega -= 30;
            }
        }
    }

    private void FixedUpdate()
    {
        if (started)
        {
            if (!deltaT)
            {
                transform.rotation = Quaternion.Euler(0f, 0f, transform.rotation.eulerAngles.z + 2 * omega);
            }
            else
            {
                transform.rotation = Quaternion.Euler(0f, 0f, transform.rotation.eulerAngles.z + 2 * omega * Time.fixedDeltaTime);
            }
            if (extra != null)
            {
                extra.rotation = Quaternion.Euler(0f, 0f, transform.rotation.eulerAngles.z + offset);
            }
        }
    }

    public void StartSpinning()
    {
        started = true;
    }

    public void StopSpinning()
    {
        started = false;
    }

    public IEnumerator Accelerate()
    {
        started = true;
        for(float t = 2f; t > 0f; t-= Time.fixedDeltaTime)
        {
            yield return new WaitForFixedUpdate();
            omega += Time.fixedDeltaTime * 3000;
        }
    }
}
