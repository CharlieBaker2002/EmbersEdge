using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ChallengeShrine : MonoBehaviour
{
    public GameObject[] e1;
    public GameObject[] e2;
    public GameObject[] e3;
    public int[] ns;
    private GameObject[][] es;
    private int times = 0;
    private int maxTimes;
    bool running = false;
    private List<Transform> actives = new List<Transform>();
    Animator anim;

    private void Awake()
    {
        es = new GameObject[][] { e1, e2, e3 };
        anim = GetComponent<Animator>();
        maxTimes = Random.Range(1, 4);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.name == "Character")
        {
            if (!running)
            {
                StartCoroutine(Spawnage());
            }
        }
    }

    IEnumerator Spawnage()
    {
        anim.SetBool("Trigger", true);
        PortalScript.i.canPortal = false;
        running = true;
        times += 1;
        GameObject[] ePrefabs = es[GS.era];
        for (int i = 0; i < times; i++)
        {
            for (int j = 0; j < ns[GS.era] + Mathf.Pow((times - 1) * (GS.era + 1), 2); j++)
            {
                Transform instance = Instantiate(GS.RE(ePrefabs), (Vector2) transform.position + Random.insideUnitCircle * 1f, transform.rotation, GS.FindParent(GS.Parent.enemies)).transform; //instantiate and place enemies
                actives.Add(instance);
                yield return new WaitForSeconds(5f * (GS.era + 1)/ ns[GS.era]);
            }
            while (actives.Count > 0) //wait for all enemies to die
            {
                for (int k = 0; k < actives.Count; k++)
                {
                    if (actives[k] == null)
                    {
                        actives.RemoveAt(k);
                        break;
                    }
                }
                yield return null;
            }
            GS.GatherResources(DM.i.activeRoom.transform);
        } //repeat for the number of times you've challenged the shrine.
        yield return new WaitForSeconds(1f);
        GS.GatherResources(DM.i.activeRoom.transform);
        running = false;
        PortalScript.i.canPortal = true;
        if (times >= maxTimes)
        {
            anim.SetBool("End", true);
            Destroy(this);
            yield break;
        }
        anim.SetBool("Trigger", false);
    }
}
