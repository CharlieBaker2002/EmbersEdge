using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class Shield : MonoBehaviour
{
    public float hp;
    public float maxScale;
    public float maxTime = 5f;
    private Unit u;
    public bool increaseMax = true;
    private int id;

    public virtual void Start()
    {
        u = GetComponentInParent<Unit>();
        if (u == null)
        {
            u = CharacterScript.CS;
            Debug.LogWarning("Didnt find ls, so defaulting to player ls");
        }
        id = u.CreateShield(hp);
        StartCoroutine(Grow());
    }

    public IEnumerator Grow()
    {
        float s = transform.localScale.x;
        float timer = Time.time;
        while (transform.localScale.x < maxScale)
        {
            s += Time.deltaTime;
            transform.localScale = new Vector3(s, s, 1);
            yield return null;
        }
        if(maxTime <= 0)
        {
            Destroy(gameObject);
            yield break;
        }
        yield return new WaitForSeconds(Mathf.Max(0.1f, maxTime - 2*(Time.time - timer)));
        while (transform.localScale.x > 0.1f)
        {
            s -= Time.deltaTime;
            transform.localScale = new Vector3(s, s, 1);
            yield return null;
        }
        if (u != null)
        {
            u.RemoveShield(id);
        }
        Destroy(gameObject);
    }
}
