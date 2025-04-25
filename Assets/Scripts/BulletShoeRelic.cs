using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class BulletShoeRelic : Part
{
    System.Action<InputAction.CallbackContext> dashDel;
    System.Action<InputAction.CallbackContext> startDel;
    public GameObject bullet;
    public float maxN = 2;
    public float minN = 1;
    public float intermitentWait = 0.5f;
    public float energyCostPerBullet;
    [SerializeField] private Sprite[] sprites;
    public float refreshCD = 6f;
    
    private float timer = 0f;

    private void Awake()
    {
        startDel = ctx =>
        {
            if (timer > 0f) return;
            sr.sprite = sprites[1];
        };
        
        dashDel = ctx =>
        {
            if (sr.sprite == sprites[1])
            {
                sr.sprite = sprites[0];
                int n = Mathf.RoundToInt(Mathf.Lerp(minN, maxN, (float)ctx.duration));
                if (n <= 0)
                {
                    return;
                }
                StartCoroutine(ShootProjectiles(intermitentWait, n));
                timer = refreshCD;
            }
        };
    }

    private void Update()
    {
        if (sr.sprite != sprites[2]) return;
        timer -= Time.deltaTime;
        if (timer <= 0f)
        {
            sr.sprite = sprites[0];
        }
    }

    public override void StartPart(MechaSuit m)
    {
        base.StartPart(m);
        enabled = true;
        IM.i.pi.Player.Jump.performed += dashDel;
        IM.i.pi.Player.Jump.started += startDel;
        sr.sprite = sprites[0];
    }
    
    public override void StopPart(MechaSuit m)
    {
        enabled = false;
        IM.i.pi.Player.Jump.performed -= dashDel;
        IM.i.pi.Player.Jump.started -= startDel;
    }

    IEnumerator ShootProjectiles(float waitTime, int n)
    {
        for(int i = 0; i< n; i++)
        {
            if (!ResourceManager.instance.ChangeFuels(-energyCostPerBullet))
            {
                sr.sprite = sprites[2];
                yield break;
            }
            
            yield return new WaitForSeconds(waitTime);
            var bul = Instantiate(bullet, transform.position, transform.rotation, GS.FindParent(GS.Parent.allyprojectiles));
            bul.GetComponent<ProjectileScript>().SetValues(-transform.up, "Allies");
            bul.SetActive(true);
        }
        sr.sprite = sprites[2];
    }
}
