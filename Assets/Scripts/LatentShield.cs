using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.TextCore.Text;

public class LatentShield : MonoBehaviour
{
    public float rate;
    public float max;
    [SerializeField] Unit u;
    public int ID;
    private void Awake()
    {
        if (u == null)
        {
            u = GetComponentInParent<Unit>();
            if (u == null)
            {
                Debug.LogError("Didnt find unit!!!");
                Destroy(gameObject);
            }
        }
    }

    private void Start()
    {
        ID = u.CreateShield(max,false,false);
    }

    private void Update()
    {
        if (u != null)
        {
            u.ModifyShieldStrength(ID, rate * Time.deltaTime);
        }
    }

    public void UpdateMax(float delta)
    {
        max += delta;
        LifeScript.Shield s = u.ls.shields.FirstOrDefault(x => x.ID == ID);
        float val = s.strength;
        u.RemoveShield(ID);
        ID = u.CreateShield(max,false,false);
        u.ModifyShieldStrength(ID,val + delta);
    }
}
