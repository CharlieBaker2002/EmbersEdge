using UnityEngine;

public class JustACore : MonoBehaviour
{
    private bool added = false;
    [SerializeField] GameObject FX;
    private void OnCollisionEnter2D(Collision2D other)
    {
        if (added) return;
        ResourceManager.instance.AddCores();
        added = true;
        FX.transform.parent = GS.FindParent(GS.Parent.misc);
        SetFXExplodeDirection(other.GetContact(0).normal);
        Destroy(gameObject);
    }

    void SetFXExplodeDirection(Vector2 direction)
    {
        FX.transform.up = direction;
        FX.gameObject.SetActive(true);
    }
}
