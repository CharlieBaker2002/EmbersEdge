using UnityEngine;

public class LootBox : MonoBehaviour
{
    public int n = 3;
    public SpriteRenderer sr;
    public static LootBox i;
    [SerializeField] private FXWormhole fx;
    [SerializeField] private Part.RingClassifier classifier = Part.RingClassifier.Core;
    [SerializeField] private bool bonu;
    [SerializeField] Collider2D col;
    private void Start()
    {
        if (GS.Chance(15))
        {
            n = 4;
        }
        else if (GS.Chance(60)) 
        {
            n = 2;
        }
        transform.localScale = Vector3.zero;
        LeanTween.scale(gameObject, Vector3.one, 2f).setEaseInOutCirc().setOnComplete(() => { col.enabled = true;});
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        col.enabled = false;
        i = this;
        BlueprintManager.bonus = fx == null;
        float[] lucks = new float[n];
        float range = n <= 3 ? 2f : 2.75f;
        for (int i = 0; i < lucks.Length; i++)
        {
            lucks[i] = Mathf.Lerp(0f, 0.25f * range, (float)i / (lucks.Length - 1));
        }
        BlueprintManager.GetLoot(lucks, Mathf.Max(1,lucks.Length - 2),true,classifier, bonu);
    }
}
