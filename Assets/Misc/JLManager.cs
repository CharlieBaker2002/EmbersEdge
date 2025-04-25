using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class JLManager : MonoBehaviour
{
    List<Joe> joes = new List<Joe>();
    [SerializeField] Joe theJoe;

    private void Update()
    {
        if(Random.Range(0,3) == 0)
        {
            joes.Add(Instantiate(theJoe, transform.position, transform.rotation, transform));
        }
        for (int i = 0; i < joes.Count; i++)
        {
            joes[i].timer += Time.deltaTime;
            joes[i].Spin();
            joes[i].Move();
            joes[i].Animate();
            if (joes[i].timer > 4f)
            {
                Destroy(joes[i].gameObject);
                joes.RemoveAt(i);
                i--;
            }
        }
    }

}
