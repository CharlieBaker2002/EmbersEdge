using UnityEngine;

public class Unparent : MonoBehaviour, IOnCollide, IOnDeath
{
    public GameObject[] children;
    public bool newObj = false;
    public int n = 5;
    public float waitT = 0.5f;
    float timer = 0f;
    public bool atStart = false;

    private void Start()
    {
        if (atStart)
        {
            OnDeath();
            Destroy(gameObject);
        }
    }

    public void Update()
    {
        timer-= Time.deltaTime;
    }

    public void OnCollide(Collision2D collision)
    {
	    if(timer <= 0f)
	    {
            if (!collision.rigidbody.CompareTag(tag))
            {
                OnDeath();
                timer = waitT;
            }
	    }
    }

    public void OnDeath()
    {
        n--;
        if (n < 0)
        {
            return;
        }
        foreach (GameObject c in children)
        {
            var obj = c;
            if (newObj)
            {
                obj = Instantiate(c,transform.position,transform.rotation,null);
            }
            if (obj != null)
            {
                obj.SetActive(true);
                foreach(Behaviour b in obj.GetComponents<Behaviour>())
                {
                    b.enabled = true;
                }
                obj.transform.parent = null;
            }
        }
    }
}
