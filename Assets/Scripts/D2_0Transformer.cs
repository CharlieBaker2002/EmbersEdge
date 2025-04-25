using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class D2_0Transformer : MonoBehaviour
{
    public Animator[] anims;
    public Light2D[] lights;
    public Color green;
    public Light2D l2;
    public Room room;

    public IEnumerator TransformTime()
    {
        Color ogCol = lights[0].color;
        foreach(Animator a in anims)
        {
            a.SetBool("Transform", true);
        }
        for (int i = 0; i < 4; i++)
        {
            Instantiate(room.enemySOs[0].prefab, room.sp[i].position, Quaternion.identity, GS.FindParent(GS.Parent.enemies)).GetComponent<LifeScript>().orbs = new float[] { 0, 1, 0, 0 };
        }
        for (float i = 2f; i > 0f; i -= Time.deltaTime)
        {
            foreach (Light2D l in lights)
            {
                l.color = Color.Lerp(l.color, green, Time.deltaTime * 1.5f);
            }
            l2.intensity = Mathf.Lerp(l2.intensity, 0.85f, Time.deltaTime);
            yield return null;
        }
        for (float i = 3f; i > 0f; i -= Time.deltaTime)
        {
            foreach (Light2D l in lights)
            {
                l.color = Color.Lerp(l.color,ogCol,Time.deltaTime);
            }
            yield return null;
            l2.intensity = Mathf.Lerp(l2.intensity, 0.3f, Time.deltaTime);
        }
    }
}
