using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Worm : MonoBehaviour
{
    public int n = 30;
    [SerializeField] LineRenderer lr;
    Vector3[] positions;
    Vector2[] posVels; 
    public float interSpace = 0.2f;
    public float smoothSpeed = 0.001f;
    public float trailSpeed = 0.1f;
    public enum tenType { Shrinking, Wagging, WaggingTail, Normalized, Sine, Chaos};
    public tenType tentaType;
  
    public float wiggleSpeed = 0f;
    public float wiggleMagnitude = 0f;
    private float wiggleCap;

    Vector3 dir = Vector3.one;

    public Transform head;
    UnitMovement movement;

    private void Awake()
    {
        wiggleCap = wiggleMagnitude;
        lr.positionCount = n;
        positions = new Vector3[n];
        positions[0] = transform.position;
        posVels = new Vector2[n];
        for(int i = 1; i < n; i++)
        {
            positions[i] = positions[i - 1] + (Vector3) transform.up * interSpace;
            posVels[i] = (Vector3)Vector2.zero;
        }
        if (head != null)
        {
            movement = GetComponentInParent<UnitMovement>();
        }
    }

    public void FixedUpdate()
    {
        Vector2 pos = (Vector2)transform.position + Mathf.Sin(Time.time * wiggleSpeed) * wiggleMagnitude * (Vector2)transform.right;
        positions[0] = pos;
        if (tentaType == tenType.Shrinking)
        {
            for (int i = 1; i < n; i++)
            {
                positions[i] = Vector2.SmoothDamp(positions[i], positions[i - 1] + transform.right * interSpace / i, ref posVels[i], smoothSpeed + trailSpeed / i);
            }
        }
        else if (tentaType == tenType.Wagging) //choose small interspace
        {
            Vector3 targetPoint;
            for (int i = 1; i < n; i++)
            {
                targetPoint = (positions[i - 1] - positions[i]).normalized * interSpace + positions[i - 1];
                positions[i] = Vector2.SmoothDamp(positions[i], targetPoint, ref posVels[i], smoothSpeed + trailSpeed / ((i + n) / 2));
            }
        }
        else if (tentaType == tenType.WaggingTail) //choose small interspace
        {
            Vector3 targetPoint;
            for (int i = 1; i < n; i++)
            {
                targetPoint = (positions[i - 1] - positions[i]).normalized * interSpace + positions[i - 1];
                positions[i] = Vector2.SmoothDamp(positions[i], targetPoint, ref posVels[i], smoothSpeed + trailSpeed / (1.6f * i));
            }
        }
        else if (tentaType == tenType.Normalized)
        {
            Vector3 targetPoint;
            float tolerance = interSpace * interSpace * 0.5f;
            for (int i = 1; i < n; i++)
            {
                targetPoint = (positions[i] - positions[i - 1]).normalized * interSpace + positions[i - 1];
                if ((targetPoint - positions[i]).sqrMagnitude < tolerance)
                {
                    positions[i] = Vector2.SmoothDamp(positions[i], targetPoint + interSpace * n * transform.right / (i * i + n), ref posVels[i], smoothSpeed);
                }
                else
                {
                    positions[i] = Vector2.SmoothDamp(positions[i], targetPoint, ref posVels[i], smoothSpeed);
                }
            }
        }
        else if (tentaType == tenType.Sine)
        {
            positions[0] = transform.position;
            Vector3 targetPoint;
            float tolerance = interSpace * interSpace * 0.5f;
            for (int i = 1; i < n; i++)
            {
                float coef = (float)i / (n - 1);
                targetPoint = (positions[i] - positions[i - 1]).normalized * interSpace + positions[i - 1];
                if (transform.localRotation.eulerAngles.z > 270 || transform.localRotation.eulerAngles.z < 90)
                {
                    Debug.Log(name + transform.localRotation.eulerAngles.z.ToString());
                    targetPoint += coef * Mathf.Sin(Time.time * wiggleSpeed * (0.5f + coef)) * wiggleCap * transform.parent.right;
                }
                else
                {
                    targetPoint -= coef * Mathf.Sin(Time.time * wiggleSpeed * (0.5f + coef)) * wiggleCap * transform.parent.right;
                }
                if ((targetPoint - positions[i]).sqrMagnitude < tolerance)
                {
                    positions[i] = Vector2.SmoothDamp(positions[i], targetPoint + interSpace * n * transform.right / (i * i + n), ref posVels[i], smoothSpeed);
                }
                else
                {
                    positions[i] = Vector2.SmoothDamp(positions[i], targetPoint, ref posVels[i], smoothSpeed);
                }
            }
        }
        else if (tentaType == tenType.Chaos)
        {
            positions[0] = transform.position;
            Vector3 targetPoint;
            for (int i = 1; i < n; i++)
            {
                targetPoint = (positions[i - 1] - positions[i]).normalized * interSpace + positions[i - 1];
                positions[i] = Vector2.SmoothDamp(positions[i], targetPoint, ref posVels[i], smoothSpeed + trailSpeed / ((i + n) / 2));
                float coef = (float)i / (n - 1);
                if (transform.localRotation.eulerAngles.z < -90 || transform.localRotation.eulerAngles.z > 90)
                {
                    positions[i] += coef * Mathf.Sin(Time.time * wiggleSpeed * coef) * wiggleCap * transform.parent.right;
                }
                else
                {
                    positions[i] += coef * Mathf.Sin(Time.time * wiggleSpeed * coef) * wiggleCap * -transform.parent.right;
                }

            }
        }
        lr.SetPositions(positions);
        if(head != null)
        {
            head.position = positions[0];
            head.transform.up = Vector2.Lerp(head.transform.up, (positions[0] - positions[1]).normalized,movement.turniness * 2 * Time.fixedDeltaTime);
        }
    }

    public void UpdateN(int p)
    {
        n = p;
        Awake();
    }

}
