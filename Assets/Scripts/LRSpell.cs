using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class LRSpell : Spell
{
    private float atr = 0;
    private Coroutine cor;
    public LineRenderer[] lrs;
    private List<LR> LRs = new List<LR>();
    public static GameObject xpl;

    private void Awake()
    {
        xpl = (GameObject)Resources.Load("xpl");
        foreach (LineRenderer l in lrs)
        {
            LRs.Add(new LR(l, this));
        }
        for(int i =0; i < 2; i++)
        {
            LRs[i].acc = true;
        }
    }

    public override Vector2 GetManaAndCd()
    {
        return new Vector2(1 + level * 2,Mathf.Max(3, 10 - 0.5f * atr));
    }

    public override void Intellect(float intellect)
    {
        atr = level;
    }

    public override void LevelUp()
    {
        level++;
        foreach (LR l in LRs)
        {
            l.lvl += 1;
        }
        if(level == 2)
        {
            for (int i = 2; i < 6; i++)
            {
                LRs[i].acc = true;
            }
        }
        else if (level == 3)
        {
            for (int i = 6; i < 18; i++)
            {
                LRs[i].acc = true;
            }
        }
    }

    public override void Performed(InputAction.CallbackContext ctxs)
    {
        base.Performed(ctxs);
        if (cor != null)
        {
            StopCoroutine(cor);
        }
        foreach (LR l in LRs)
        {
            l.Reset();
        }
    }

    public override void Started(InputAction.CallbackContext ctx)
    {
        int ang;
        int dAng;
        base.Started(ctx);
        switch (level)
        {
            case 1:
                ang = 135;
                dAng = -90;
                break;
            case 2:
                ang = 180;
                dAng = -36;
                break;
            case 3:
                ang = 0;
                dAng = 20;
                break;
            default:
                throw new System.Exception("LRSpell lvl > 3");
        }
        ang += (int)GS.CS().rotation.eulerAngles.z;
        for(int i = 0; i < 2 * Mathf.Pow(level, 2); i++)
        {
            LRs[i].Init(ang);
            ang += dAng;
        }
        cor = StartCoroutine(LRSpellI());
    }

    private IEnumerator LRSpellI()
    {
        float t = 1 + 0.75f * level;
        while(t > 0f)
        {
            yield return new WaitForSeconds(0.0675f);
            t -= 0.0675f;
            foreach (LR l in LRs)
            {
                l.NextMove();
            }
        }
        foreach(LR l in LRs)
        {
            l.Reset();
        }
    }

    private class LR
    {
        public LR(LineRenderer lr, LRSpell spell)
        {
            sp = spell;
            l = lr;
            dir = Vector2.zero;
            lvl = 1;
            resetting = false;
            z = 0f;
            addingZ = false;
            l.enabled = false;
            acc = false;
            m = 0;
        }

        public int lvl { get; set; }
        private Vector3 dir { get; set; }
        private LRSpell sp { get; set; }
        private LineRenderer l { get; set; }
        private bool resetting { get; set; }
        private float z { get; set; }
        private bool addingZ { get; set; }
        public bool acc { get; set; }
        int m { get; set; }

        public void Init(float a)
        {
            if (acc)
            {
                dir = (0.1f + lvl * 0.05f) * GS.ATV3(a);
                l.enabled = true;
                l.positionCount = 1;
                l.SetPosition(0, (Vector2)(GS.CS().position + dir));
                resetting = false;
                z = -1f;
                m = 0;
            }
        }

        public void NextMove()
        {
            if (acc &&!resetting)
            {
                Vector2 p = l.GetPosition(l.positionCount-1) + dir;
                l.positionCount++;
                l.SetPosition(l.positionCount-1, new Vector3(p.x, p.y, z));
                AlterZ();
                m++;
                if(m > 2*(5 - lvl))
                {
                    CalcNextDir();
                }
            }
        }

        private void CalcNextDir()
        {
            dir = (0.1f + lvl * 0.05f) * Vector2.Lerp(dir, IM.i.MousePosition(Vector2.zero,true) - (Vector2)l.GetPosition(l.positionCount - 1), 0.6f * RandomManager.Rand(0)).normalized;
            dir = GS.Rotated(dir, Random.Range(-5f, 5f));
            if(Vector2.Distance(IM.i.MousePosition(Vector2.zero, true), l.GetPosition(l.positionCount-1)) < 0.5f)
            {
                Reset();
            }
        }

        private void AlterZ()
        {
            z = 0;
            //z += addingZ ? 2f : -2f;
            //if (z >= -1f)
            //{
            //    addingZ = false;
            //    z = -1.1f;
            //}
            //else if(z <= -11f)
            //{
            //    z = -10.9f;
            //    addingZ = true;
            //}
        }

        public void Reset()
        {
            if (!resetting && acc)
            {
                resetting = true;
                sp.StartCoroutine(ResetI());
            }
        }

        private IEnumerator ResetI() //remove from line while points arent equal to initialiser points
        {
            while(l.positionCount > 1)
            {
                Vector3[] vs = new Vector3[l.positionCount - 1];
                for(int i = 0; i < vs.Length; i++)
                {
                    vs[i] = l.GetPosition(i + 1);
                }
                l.SetPositions(vs);
                l.positionCount--;
                yield return new WaitForSeconds(0.08f * Mathf.Min(1, l.positionCount / ((1 + 0.75f * lvl)/0.0675f)) / lvl);
            }
            Instantiate(xpl, new Vector3(l.GetPosition(0).x,l.GetPosition(0).y,-1), GS.RandRot());
            l.enabled = false;
        }
    }
}
