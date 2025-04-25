using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class ResourceVP : MonoBehaviour, IClickable
{
    float timer = 0f;
    float z;

    Color[] cols;
    SpriteRenderer sr;
    float coef = 0.2f;
    public float scale = 1f;
    public GameObject fx;
    // Start is called before the first frame update

    private void OnEnable()
    {
        sr = GetComponent<SpriteRenderer>();
        z = transform.rotation.eulerAngles.z;
        timer = 0f;
        transform.localScale = new Vector3(0, 0, 0);
    }

    private void Update()
    {
        transform.rotation = Quaternion.Euler(0f, 0f, z + 1000f * Time.deltaTime * Mathf.Abs(1.5f - timer));
        z = transform.rotation.eulerAngles.z;
    }

    public void SetOre(Ore ore)
    {
        StartCoroutine(Shift());
    }

    IEnumerator Shift()
    {
        timer = 0f;
        sr.color = cols[3];
        while (timer < 0.15f)
        {
            transform.localScale = Mathf.Lerp(transform.localScale.x, scale * 0.6f, 0.03f) * Vector3.one;
            coef = timer * 0.4f; //up to 0.2
            sr.color = Color.Lerp(sr.color, cols[0], 0.05f);
            timer += Time.deltaTime;
            yield return null;
        }
        while (timer < 0.3f)
        {
            transform.localScale = Mathf.Lerp(transform.localScale.x, scale, 0.03f) * Vector3.one;
            coef = 0.2f + (timer - 0.5f) / 5;
            sr.color = Color.Lerp(sr.color, cols[1], 0.05f); //up to 0.3
            timer += Time.deltaTime;
            yield return null;
        }
        while (timer < 0.5f)
        {
            transform.localScale = Mathf.Lerp(transform.localScale.x, scale * 1.5f, 0.03f) * Vector3.one;
            coef = (0.3f + (timer - 1f) / 5);
            sr.color = Color.Lerp(sr.color, cols[2], 0.05f); //up to 0.4
            timer += Time.deltaTime;
            yield return null;
        }
        while (timer < 0.7f)
        {
            transform.localScale = Mathf.Lerp(transform.localScale.x, scale, 0.03f) * Vector3.one;
            coef = 0.4f - (timer - 1.5f) / 5;
            sr.color = Color.Lerp(sr.color, cols[1], 0.05f); //down to 0.3
            timer += Time.deltaTime;
            yield return null;
        }
        while (timer < 0.85f)
        {
            transform.localScale = Mathf.Lerp(transform.localScale.x, scale * 0.6f, 0.03f) * Vector3.one;
            coef = 0.3f - (timer - 2f) / 5;
            sr.color = Color.Lerp(sr.color, cols[0], 0.05f); //down to 0.2
            timer += Time.deltaTime;
            yield return null;
        }
        while (timer < 1f)
        {
            transform.localScale = Mathf.Lerp(transform.localScale.x, 0f, 0.03f) * Vector3.one;
            timer += Time.deltaTime;
            coef = 0.2f - (timer - 2.5f) / 5f;
            sr.color = Color.Lerp(sr.color, cols[3], 0.05f); //down to 0
            yield return null;
        }
        gameObject.SetActive(false);

    }

    public void OnClick()
    {
        var obj = Instantiate(fx, transform.position, Quaternion.identity, GS.FindParent(GS.Parent.misc));
        ParticleSystem ps = obj.GetComponent<ParticleSystem>();
        var colOL = ps.colorOverLifetime;
        colOL.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(new GradientColorKey[] { new GradientColorKey(sr.color, 0.0f), new GradientColorKey(cols[3], 1.0f) }, new GradientAlphaKey[] { new GradientAlphaKey(1.0f, 0.0f), new GradientAlphaKey(0.0f, 1.0f) });
        colOL.color = grad;
        var emitter = ps.emission;
        emitter.rateOverTime = Mathf.RoundToInt(coef * 50);
        Gradient tgrad = new Gradient();
        tgrad.SetKeys(new GradientColorKey[] { new GradientColorKey(sr.color, 0.0f), new GradientColorKey(cols[3], 1.0f) }, new GradientAlphaKey[] { new GradientAlphaKey(1.0f, 0f), new GradientAlphaKey(0.75f, 1.0f) });
        var trail = ps.trails;
        trail.colorOverTrail = tgrad;
        Destroy(gameObject);
    }
}
