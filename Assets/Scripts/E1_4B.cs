using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class E1_4B : MonoBehaviour
{
    public E1_4 daddy;

    public void ShootA()
    {
        daddy.ShootSmall(0);
    }

    public void ShootB()
    {
        daddy.ShootSmall(1);
    }

    public void ShootC()
    {
        daddy.ShootSmall(2);
    }

    public void ShootD()
    {
        daddy.ShootSmall(3);
    }

    public void ShootE()
    {
        daddy.ShootBig();
    }

    public void ImOn()
    {   
        GS.Stat(daddy,"juggernaut",2f);
        daddy.pfx.Play();
    }

    public void ImOff()
    {
        daddy.pfx.Stop();
    }
}
