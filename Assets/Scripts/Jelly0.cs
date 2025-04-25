using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class Jelly0 : MonoBehaviour
{
    public GameObject Jelly1;
    public GameObject VP;

    LifeScript ls;
    bool hasMorphed = false;

    // Start is called before the first frame update
    void Start()
    {
        ls = GetComponent<LifeScript>();
        GetComponent<Rigidbody2D>().velocity = Random.insideUnitCircle.normalized * 1.5f;
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (!hasMorphed)
        {
            if (collision.gameObject.TryGetComponent<Jelly0>(out var jel))
            {
                if (Random.Range(1, 4) == 1)
                {
                    hasMorphed = true;
                    StartMorph();
                    jel.StartMorph();
                }
            }
        }
    }
    
    public void StartMorph()
    {
        ls.maxHp += 4;
        ls.hp = ls.maxHp;
        GetComponent<ActionScript>().AddCC("root",2f,1,false);
        GetComponent<Animator>().SetBool("Morph", true);
        Instantiate(VP, transform.position, transform.rotation, transform);
    }

    public void Morph()
    {
        Instantiate(Jelly1, transform.position, transform.rotation,GS.FindParent(GS.Parent.enemies));
        Destroy(gameObject);
    } 
}
