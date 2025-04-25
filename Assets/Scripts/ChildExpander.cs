using UnityEngine;

public class ChildExpander : MonoBehaviour
{
    public float distanceBreak = 1f;
    public float coef = 1f;
    public bool randOnStart = true;
    [Tooltip("If seperate, the distanceBreak is the maximum distance until break. Otherwise it's minimum.")]
    public bool seperate = true;
   
    private void Start()
    {
        if (randOnStart)
        {
            foreach (Transform c in transform)
            {
                c.localPosition = seperate ? Random.insideUnitCircle * 0.15f : 2 * Random.insideUnitCircle.normalized + 1.5f * Random.insideUnitCircle;
            }
        }
    }

    private void Update()
    {
        foreach (Transform c in transform)
        {
            c.localPosition = seperate ? Vector2.Lerp(c.localPosition, 1.1f * distanceBreak * c.localPosition.normalized, Time.deltaTime * coef) : Vector2.Lerp(c.localPosition, 0.95f * distanceBreak * c.localPosition.normalized, Time.deltaTime * coef);
            if (DistanceCheck(c))
            {
                enabled = false;
                return;
            }
        }
    }

    private bool DistanceCheck(Transform c)
    {
        if (seperate)
        {
            if (c.localPosition.sqrMagnitude >= 0.98f * distanceBreak * distanceBreak)
            {
                return true;
            }
        }
        else
        {
            if (c.localPosition.sqrMagnitude <= 1.02f * distanceBreak * distanceBreak)
            {
                return true;
            }
        }
        return false;
    }
}
