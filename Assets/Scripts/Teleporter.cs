using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Teleporter : MonoBehaviour
{
    public bool charOnly = false;
    public bool invertVel = false;
    public Transform spawnPoint;
    private List<int> IDs = new List<int>();
    public Teleporter oT;
    public bool redirectVel = false;

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.isTrigger)
        {
            return;
        }
        if (oT != null)
        {
            if (collision.attachedRigidbody != null)
            {
                if (!IDs.Contains(collision.attachedRigidbody.GetInstanceID()))
                {
                    if (collision.attachedRigidbody.GetComponent<LifeScript>() != null)
                    {
                        oT.IDs.Add(collision.attachedRigidbody.GetInstanceID());
                        collision.transform.position = oT.spawnPoint.position;
                        if (oT.redirectVel)
                        {
                            collision.attachedRigidbody.linearVelocity = oT.transform.right * collision.attachedRigidbody.linearVelocity.magnitude;
                        }
                        if (oT.invertVel)
                        {
                            collision.attachedRigidbody.linearVelocity *= -1;
                        }
                    }
                }
            }
        }
       
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.isTrigger)
        {
            return;
        }
        if (oT != null)
        {
            if (collision.attachedRigidbody != null)
            {
                if (IDs.Contains(collision.attachedRigidbody.GetInstanceID()))
                {
                    IDs.Remove(collision.attachedRigidbody.GetInstanceID());
                }
            }
        }
    }
}
