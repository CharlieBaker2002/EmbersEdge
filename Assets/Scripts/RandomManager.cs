using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RandomManager : MonoBehaviour
{
    [SerializeField]
    public static RandomManager i;
    public AnimationCurve a;
    public AnimationCurve b;
    public AnimationCurve c;
    public AnimationCurve d;
    private AnimationCurve[] curves;

    private void Awake()
    {
        i = this;
        curves = new AnimationCurve[] { a, b, c, d };
    }

    /// <summary>
    /// a is slow up (0.25 at 0.5), b is slower up (0.12 at 0.5, but then 0.85 - 0.95 sudden increase and 0.95-1 is 1), c is same idea with no flat at end so high is much less likely.
    /// d is an exacerbated a;
    /// </summary>
    public static float Rand(int ind, float plus = 0)
    {
        return plus + i.curves[ind].Evaluate(Random.Range(0f, 1f));
    }

    public static float Rand(int ind, Vector2 range,float plus = 0)
    {
        return plus + i.curves[ind].Evaluate(Random.Range(range.x, range.y));
    }
}
