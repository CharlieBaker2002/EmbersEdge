using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Retaliator : Part
{
    public float timer;
    [SerializeField] private Sprite[] sprs;
    [SerializeField] private Collider2D col;
    [SerializeField] private float refresh = 8f;
    [SerializeField] private float distance = 1f;
    [SerializeField] private AntiRotate ar;
    
    public override void StopPart(MechaSuit m)
    {
        gameObject.SetActive(false);
    }

    private void Update()
    {
        timer -= Time.deltaTime;
        if (timer <= 0f)
        {
            engagement = 1f;
        }
    }

    public void OnTriggerEnter2D(Collider2D collision)
    {
        if(timer > 0f) return;
        if(collision.attachedRigidbody == null) return;
        if (!collision.attachedRigidbody.CompareTag(GS.EnemyTag(tag))) return;
        if (!collision.attachedRigidbody.GetComponent<Unit>()) return;

        ar.enabled = false;
        timer = refresh;
        engagement = 2f;
        transform.localScale = 2f*Vector3.one;
        Vector3 dir = collision.transform.position - transform.position;
        Deploy(transform.position + dir.normalized * distance, 0.5f, 1f, 2f, () => ar.enabled = true);
        transform.rotation = GS.VTQ(dir);
        col.enabled = true;
        StartCoroutine(GS.Animate(sr, sprs, 1f,false));
       
        this.QA(() =>
        {
            col.enabled = false;
            engagement = 0f;
        },1.1f);
        this.QA(() => sr.sprite = sprs[0], refresh);
    }
}
