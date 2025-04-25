using System;
using System.Collections;
using System.Linq;
using UnityEngine;

public class WallClinger : MonoBehaviour
{
    public Action<Vector3, Quaternion> act;
    [SerializeField] private float wait;
    public float speed;
    public bool reverse;
    public float distBetweenAct;
    public Collider2D col;
    private Quaternion normalDirection;
    [SerializeField] private bool startWithAct = false;
    
    private IEnumerator Start()
    {
        yield return null;
        yield return new WaitForSeconds(wait);
        StartCoroutine(FollowWall(col));
    }

    private IEnumerator FollowWall(Collider2D coli)
    {
        Vector2[] points;
        if (coli is BoxCollider2D boxCol)
        {
            points = GetBoxColliderPoints(boxCol)
                .Select(p => (Vector2)boxCol.transform.TransformPoint(p) ) // Transform to world space
                .ToArray();
        }
        else if (coli is PolygonCollider2D polyCol)
        {
            points = polyCol.GetPath(0)
                .Select(p => (Vector2)coli.transform.TransformPoint(p + coli.offset)) // Transform to world space
                .ToArray();
        }
        else if (coli is CircleCollider2D circleCol) // Approximate with 20 points
        {
            points = GetCircleColliderPoints(circleCol, 20)
                .Select(p => (Vector2)circleCol.transform.TransformPoint(p)) // Transform to world space
                .ToArray();
        }
        else
        {
            if (coli == null)
            {
                Debug.LogWarning("No collider assigned.");
            }
            else
            {
                Debug.LogWarning("Unsupported collider type.");
                Debug.LogWarning(coli.GetType());
            }
            yield break; // Unsupported collider type
        }

        // Calculate if the points are ordered clockwise
        float sum = 0;
        for (int i = 0; i < points.Length; i++)
        {
            Vector2 p1 = points[i];
            Vector2 p2 = points[(i + 1) % points.Length];
            sum += (p2.x - p1.x) * (p2.y + p1.y);
        }
        bool clockwise = sum > 0;

        // Find the closest point on the collider's edge to the current position
        float minDistance = float.MaxValue;
        int closestSegmentIndex = -1;
        Vector2 closestPoint = Vector2.zero;

        for (int i = 0; i < points.Length; i++)
        {
            Vector2 p1 = points[i];
            Vector2 p2 = points[(i + 1) % points.Length];

            Vector2 closest = ClosestPointOnLineSegment(p1, p2, transform.position);
            float dist = Vector2.Distance(transform.position, closest);

            if (dist < minDistance)
            {
                minDistance = dist;
                closestSegmentIndex = i;
                closestPoint = closest;
            }
        }

        int pointIndex = closestSegmentIndex;
        transform.position = closestPoint;
        float distanceSinceLastAct = startWithAct ? distBetweenAct : 0f; // Distance since last act invocation
       

        while (true)
        {
            Vector2 startPoint, endPoint;
            int nextPointIndex;
            if (!reverse)
            {
                startPoint = points[pointIndex];
                nextPointIndex = (pointIndex + 1) % points.Length;
                endPoint = points[nextPointIndex];
            }
            else
            {
                startPoint = points[(pointIndex + 1) % points.Length];
                nextPointIndex = GS.Cycle(pointIndex, -1, points.Length - 1);
                endPoint = points[pointIndex];
            }

            Vector2 segmentDirection = endPoint - startPoint;
            float segmentLength = segmentDirection.magnitude;
            Vector2 direction = segmentDirection.normalized;

            // Calculate the normal direction based on the edge
            Vector2 normal = new Vector2(-direction.y, direction.x);
            if (!clockwise)
                normal = -normal;

            normalDirection = Quaternion.LookRotation(Vector3.forward, normal);
            if(reverse) normalDirection *= Quaternion.Euler(0, 0, 180);
            float distanceCovered = Vector2.Distance(startPoint, transform.position);

            while (distanceCovered < segmentLength)
            {
                float step = speed * Time.deltaTime;

                // Adjust step to not overshoot the segment
                if (distanceCovered + step > segmentLength)
                {
                    step = segmentLength - distanceCovered;
                }

                distanceCovered += step;
                distanceSinceLastAct += step;

                // Check if we need to invoke 'act'
                while (distanceSinceLastAct >= distBetweenAct)
                {
                    // Calculate overshoot
                    float overshoot = distanceSinceLastAct - distBetweenAct;

                    // Calculate the exact position where 'act' should be invoked
                    float actDistanceCovered = distanceCovered - overshoot;
                    float actT = actDistanceCovered / segmentLength;
                    actT = Mathf.Clamp01(actT);
                    Vector2 actPosition = Vector2.Lerp(startPoint, endPoint, actT);

                    act?.Invoke(actPosition, normalDirection);

                    // Update distanceSinceLastAct
                    distanceSinceLastAct -= distBetweenAct;
                }

                // Move the transform to the new position
                float t = distanceCovered / segmentLength;
                t = Mathf.Clamp01(t);
                transform.position = Vector2.Lerp(startPoint, endPoint, t);

                yield return null;
            }

            // Prepare for the next segment
            pointIndex = nextPointIndex;
            distanceCovered = 0f;

            yield return null;
        }
    }

    // Helper function to find the closest point on a line segment to a given point
    private Vector2 ClosestPointOnLineSegment(Vector2 A, Vector2 B, Vector2 P)
    {
        Vector2 AP = P - A;
        Vector2 AB = B - A;
        float magnitudeAB = AB.sqrMagnitude;
        float ABAPproduct = Vector2.Dot(AP, AB);
        float distance = ABAPproduct / magnitudeAB;

        if (distance <= 0f)
            return A;
        else if (distance >= 1f)
            return B;
        else
            return A + AB * distance;
    }

    private Vector2[] GetBoxColliderPoints(BoxCollider2D boxCol)
    {
        Vector2 size = boxCol.size;
        Vector2 offset = boxCol.offset;
        Vector2[] points = new Vector2[4];
        points[0] = offset + new Vector2(-size.x, -size.y) * 0.5f;
        points[1] = offset + new Vector2(size.x, -size.y) * 0.5f;
        points[2] = offset + new Vector2(size.x, size.y) * 0.5f;
        points[3] = offset + new Vector2(-size.x, size.y) * 0.5f;
        return points;
    }

    private Vector2[] GetCircleColliderPoints(CircleCollider2D circleCol, int numPoints)
    {
        Vector2[] points = new Vector2[numPoints];
        float angleStep = 360f / numPoints;
        float radius = circleCol.radius;
        Vector2 offset = circleCol.offset;

        for (int i = 0; i < numPoints; i++)
        {
            float angle = i * angleStep * Mathf.Deg2Rad;
            Vector2 localPoint = offset + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            points[i] = localPoint; // Will transform to world space later
        }
        return points;
    }
}