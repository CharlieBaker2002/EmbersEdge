using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AnimationPasser : MonoBehaviour
{
    
    [Header("Functions 1-5")]
    
    [SerializeField] private string[] names;

    [SerializeField] private MonoBehaviour mono;
    
    
    public void F1()
    {
        mono.Invoke(names[0],0);
    }

    public void F2()
    {
        mono.Invoke(names[1],0);
    }

    public void F3()
    { 
        mono.Invoke(names[2],0);
    }

    public void F4()
    {
        mono.Invoke(names[3],0);
    }

    public void F5()
    {
        mono.Invoke(names[4],0);
    }
}
