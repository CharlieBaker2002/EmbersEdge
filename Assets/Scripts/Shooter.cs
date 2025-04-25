using System;
using System.Collections;
using UnityEngine;

public class Shooter : MonoBehaviour
{
    Animator anim;
    public GameObject[] projectiles;
    private int pInd = 0;
    public Transform[] shootPoints;
    private int tInd = 0;
    public float reset;
    float timer;
    bool waiting = false;
    [Tooltip("degrees")]
    public float w;
    public float randSpread;
    public event Action<int> OnShoot;

    private void Awake()
    {
        anim = GetComponent<Animator>();
        timer = reset;
    }

    private void Update()
    {
        transform.rotation = Quaternion.Euler(0f, 0f, transform.rotation.eulerAngles.z + w / Time.deltaTime);
        timer -= Time.deltaTime;
        if(timer <= 0f && waiting == false)
        {
            anim.SetBool("Shooting", true);
            waiting = true;
            StartCoroutine(WaitForFalseBool());
        }
    }

    IEnumerator WaitForFalseBool()
    {
        yield return null;
        while(anim.GetBool("Shooting") == false)
        {
            yield return null;
        }
        timer = reset;
        waiting = false;
    }

    IEnumerator Test()
    {
        yield return new WaitForSeconds(1f);
        Debug.Log("a");
    }

    public void Shoot()
    {
        Transform parent = GS.FindParent(GS.ProjParent(transform));
        var p = Instantiate(projectiles[pInd], shootPoints[tInd].position,Quaternion.identity, parent).GetComponent<ProjectileScript>();
        p.SetValues(shootPoints[tInd].position + (Vector3) UnityEngine.Random.insideUnitCircle * randSpread - transform.position, tag);
        OnShoot?.Invoke(pInd);
        pInd++;
        if(pInd >= projectiles.Length)
        {
            pInd = 0;
        }
        tInd++;
        if(tInd >= shootPoints.Length)
        {
            tInd = 0;
        }
    }

    public void SetShootingBoolFalse()
    {
        anim.SetBool("Shooting", false);
    }
}
