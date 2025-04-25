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
        collision.GetComponentInParent<LifeScript>().Change(-Time.deltaTime * damageCoef, 3);
    }
    
    private void Update()
    {
        if(contacts.Count > 0)
        {
            for(int i = 0; i < contacts.Count; i++)
            {
                if (contacts[i] == null)
                {
                    RemoveElements(i);
                }
            }
            for (int i = 0; i < fires.Count; i++)
            {
                if (fires[i] != null && contacts[i].position != null)
                {
                    fires[i].position = contacts[i].position;
                }
            }
        }
    }

    private void RemoveElements(int index)
    {
        Transform theFX = fires[index];
        fires.RemoveAt(index);
        Destroy(theFX.gameObject);
        try
        {
            contacts.RemoveAt(index);
        }
        catch
        {
            Debug.Log("Remove element bug in lava script");
        }
    }
}
