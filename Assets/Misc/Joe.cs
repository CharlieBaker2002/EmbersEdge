using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Joe : MonoBehaviour
{
    public float timer = 0f;
    private bool left;
    [SerializeField] Sprite[] sprs;
    [SerializeField] SpriteRenderer sr;
    float speed;

    private void Awake()
    {
        left = Random.Range(0, 2) == 0;
        speed = Random.Range(1f, 3f);
    }

    public void Spin()
    {
        transform.rotation = Quaternion.Euler(0f, 0f, 10 * Mathf.Pow(timer, 3f));
        transform.localScale = (4 - timer) * Vector3.one;
    }

    public void Animate()
    {
        sr.sprite = sprs[Mathf.FloorToInt(timer * 1.9f)];
    }

    public void Move()
    {
        transform.position +=  Time.deltaTime * (new Vector3(left ? timer : -timer, speed, 0f) + 2f * (Vector3)Random.insideUnitCircle);
    }
}
