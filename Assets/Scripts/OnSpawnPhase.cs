using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class OnSpawnPhase : MonoBehaviour
{
    public List<SpriteRenderer> srs;
    public Unit u;
    public ActionScript AS;
    [SerializeField] float speedCoef = 1f;

    private void Awake()
    {
        if (transform.InDungeon())
        {
            AS.prepared = false;
            foreach (SpriteRenderer sr in srs)
            {
                Phase(sr, false);
            }
            u.UpdateLineColour(true);
            StartCoroutine(Phase());
        }
        else
        {
            Destroy(this);
        }
    }

    IEnumerator Phase()
    {
        float t = 0.5f;
        
        while(t > 0.03f)
        {
            foreach (SpriteRenderer sr in srs)
            {
                Phase(sr, false);
            }
            yield return new WaitForSeconds(t);
            foreach (SpriteRenderer sr in srs)
            {
                Phase(sr, true);
            }
            yield return new WaitForSeconds(t);
            t = Mathf.Lerp(t, 0f, 0.3f * speedCoef);
        }
        foreach (SpriteRenderer sr in srs)
        {
            PhaseOFF(sr);
        }
        AS.prepared = true;
        u.UpdateLineColour(true);
        Destroy(this);
    }

    void Phase(SpriteRenderer s, bool on)
    {
        Color col = s.color;
        col.a = (on) ? 0.8f : 0.3f;
        s.color = col;
    }
    void PhaseOFF(SpriteRenderer s)
    {
        Color col = s.color;
        col.a = 1f;
        s.color = col;
    }
}
