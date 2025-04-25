using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(ActionScript))]
public class Jelly1 : MonoBehaviour
{
    Transform player;
    ActionScript AS;
    SpriteRenderer SR;
    public delegate void EnrageDelegate();
    public static EnrageDelegate Enrage;
    bool ready = true;

    float speed = 1f;

    // Start is called before the first frame update

    private void Awake()
    {
        player = GS.character.transform;
        AS = GetComponent<ActionScript>();
        SR = GetComponentInChildren<SpriteRenderer>();
    }

    // Update is called once per frame
    void Update()
    {
        AS.TryAddForce((player.position - transform.position).normalized * speed, true);
    }

    private void OnEnable()
    {
        Enrage += CallEnrage;
    }

    private void OnDisable()
    {
        Enrage -= CallEnrage;
    }

    private void CallEnrage()
    {
        if (ready)
        {
            StartCoroutine(IEnrage());
            ready = false;
        }
    }

    IEnumerator IEnrage()
    {
        SR.color = Color.magenta;
        AS.turniness = 3;
        speed = 1.5f;
        while(speed > 1)
        {
            yield return new WaitForSeconds(1f);
            speed -= 0.1f;
            AS.turniness -= 0.25f;
            SR.color = Color.Lerp(SR.color, Color.white, 0.2f);
        }
        AS.turniness = 1.5f;
        speed = 1f;
        ready = true;
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if(collision.gameObject.name == "Character")
        {
            Enrage?.Invoke();
            Destroy(gameObject);
        }
    }
}
