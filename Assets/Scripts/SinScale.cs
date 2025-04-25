using UnityEngine;

public class SinScale : MonoBehaviour
{
    private float t;
    [SerializeField] private bool sinSinceBorn = false;
    [Header("From, and Delta values:")]
    [SerializeField] private Vector2 range = Vector2.one;
    [SerializeField] private float speed = 1f; // Speed multiplier for the sine wave
    
    private void Awake()
    {
        t = Time.time;
    }
    
    void Update()
    {
        float timeFactor = sinSinceBorn ? Time.time - t : Time.time;
        float sineValue = Mathf.Pow(Mathf.Sin(speed * timeFactor), 2);
        transform.localScale = sineValue * range.y* Vector3.one + Vector3.one * range.x;
    }
}