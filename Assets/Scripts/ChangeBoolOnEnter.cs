using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ChangeBoolOnEnter : StateMachineBehaviour
{
    [SerializeField]
    string[] bools;
    [SerializeField]
    bool[] vals;

    public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        for(int i = 0; i < bools.Length; i++)
        {
            animator.SetBool(bools[i], vals[i]);
        }
    }
}
