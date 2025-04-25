using System.Collections;
using System.Collections.Generic;
using UnityEngine;
public class E2_7P0 : MonoBehaviour
{
    Vector2 pos;
    public Transform marker;
    bool switchDir = false;
    public SpriteRenderer sr;
    private bool hasBoshed = false;

    public void SetDestination(Vector2 posP)
    {
        pos = posP;
        marker.transform.SetParent(GS.FindParent(GS.Parent.enemyprojectiles));
        marker.transform.position = pos;
        marker.gameObject.SetActive(true);
    }

    private IEnumerator Start()
    {
        yield return new WaitForSeconds(Random.Range(0f, 0.5f));
        float vel = 1f;
        transform.up = GS.Rotated(transform.up, 50f, true);
        while(switchDir == false)
        {
            vel = Mathf.Lerp(vel, 0f, Time.fixedDeltaTime);
            transform.position += Time.fixedDeltaTime * vel * transform.up;
            yield return new WaitForFixedUpdate();
        }
        Vector3 dir = (pos - (Vector2)transform.position) * 0.9f;
        float dist = 1000000f;
        for(float t = 0f; t <= 2f; t+=Time.fixedDeltaTime)
        {
            float temp = ((Vector2)transform.position - pos).sqrMagnitude;
            if(temp < 0.06f || temp > dist)
            {
                Bosh();
                yield break;
            }
            dist = temp;
            transform.position += t * Time.fixedDeltaTime * dir;
            yield return new WaitForFixedUpdate();
        }
    }

    public void Switch()
    {
        switchDir = true;
    }

    public void Bosh()
    {
        if (!hasBoshed)
        {
            hasBoshed = true;
        }
        else
        {
            return;
        }
        foreach(Transform t in GS.FindEnemies(tag, pos, 0.3f, true))
        {
            if (t.GetComponentInChildren<LifeScript>() != null)
            {
                t.GetComponentInChildren<LifeScript>().Change(-3f, 2);
            }
            if(t.TryGetComponent<ActionScript>(out var AS))
            {
                AS.AddCC("stun", 0.75f, -1);
            }
        }
        Instantiate(Resources.Load("E2_7PFX"),transform.position,transform.rotation,GS.FindParent(GS.Parent.fx));
        Destroy(marker.gameObject);
        StartCoroutine(Fade());
    }

    private IEnumerator Fade()
    {
        for(float i = 0f; i < 0.5f; i-= Time.fixedDeltaTime)
        {
            sr.color = Color.Lerp(sr.color, Color.clear, 3*Time.fixedDeltaTime);
            yield return new WaitForFixedUpdate();
        }
        Destroy(gameObject);
    }
}