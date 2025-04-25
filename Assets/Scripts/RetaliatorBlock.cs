using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

public class RetaliatorBlock : Part
{
    [SerializeField] private Retaliator[] retals;
    private bool any = false;
    private List<int> ids = new List<int>();
    private List<float> ts = new List<float>();
    
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!any) return;
        retals.FirstOrDefault(x=>x.timer < 0f)?.OnTriggerEnter2D(other);
        ts.Add(0f);
        ids.Add(other.GetInstanceID());
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        int ind = ids.IndexOf(other.GetInstanceID());
        if (ind == -1) return;
        ts[ind] += 2*Time.deltaTime;
        if (ts[ind] < 1f) return;
        ts[ind] -= 1f;
        OnTriggerEnter2D(other);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        int ind = ids.IndexOf(other.GetInstanceID());
        if (ind == -1) return;
        ids.RemoveAt(ind);
        ts.RemoveAt(ind);
    }

    private void Update()
    {
        any = retals.Any(x => x.timer < 0f);
        engagement = any ? 1f : 0f;
        foreach (var retaliator in retals)
        {
            retaliator.transform.localScale = Vector3.Lerp(retaliator.transform.localScale,
                (0.5f + retaliator.engagement * 0.5f) * Vector3.one, Time.deltaTime * 5f);
        }
    }
}
