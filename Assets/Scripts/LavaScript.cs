using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LavaScript : MonoBehaviour
{
    public GameObject fireFX;
    public float damageCoef = 0.25f;
    private List<Transform> contacts = new List<Transform>();
    private List<Transform> fires = new List<Transform>();

    private void OnTriggerEnter2D(Collider2D collision)
    {
        contacts.Add(collision.transform);
        fires.Add(Instantiate(fireFX, collision.transform.position, transform.rotation, GS.FindParent(GS.Parent.fx)).transform);
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        RemoveElements(contacts.IndexOf(collision.transform));
    }

    private void OnTriggerStay2D(Collider2D collision)
    {
        LifeScript ls = collision.GetComponentInParent<LifeScript>();
        if (ls != null)
        {
            ls.Change(-Time.deltaTime * damageCoef, 3);
        }
    }

    private void Update()
    {
        for (int i = contacts.Count - 1; i >= 0; i--)
        {
            if (contacts[i] == null)
            {
                RemoveElements(i);
            }
            else if (fires[i] != null)
            {
                fires[i].position = contacts[i].position;
            }
        }
    }

    // The two lists are index-paired; always add and remove them together.
    private void RemoveElements(int index)
    {
        if (index < 0 || index >= contacts.Count) return;
        if (fires[index] != null)
        {
            Destroy(fires[index].gameObject);
        }
        fires.RemoveAt(index);
        contacts.RemoveAt(index);
    }
}
