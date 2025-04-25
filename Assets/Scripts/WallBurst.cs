using UnityEngine;
public class WallBurst : MonoBehaviour
{
    [SerializeField] private WallShoot[] shoots;
    [SerializeField] private GameObject explosion;
    [SerializeField] private Rigidbody2D rb;
    [Header("OnTrigger2D Or OnCollision2D")]
    [SerializeField] private bool isTrigger;
    
    private void OnCollisionEnter2D(Collision2D other)
    {
        if(isTrigger)return;
        Burst(other.collider);
    }
    
    private void OnTriggerEnter2D(Collider2D other)
    {
        if(!isTrigger) return;
        Burst(other);
    }
    
    public void Burst(Collider2D other)
    {
        if (other.transform.CompareTag("Walls") || GS.IsInLayerMask(other.gameObject,LayerMask.GetMask("Walls","Ally Buildings")))
        {
            if (rb != null)
            {
                transform.position -= (Vector3)rb.velocity * Time.fixedDeltaTime;
            }
            foreach (WallShoot s in shoots)
            {
                s.col = other;
                s.gameObject.SetActive(true);
                s.transform.parent = GS.FindParent(GS.Parent.fx);
            }
            transform.parent = GS.FindParent(GS.Parent.fx);
            if (explosion != null)
            {
                explosion.SetActive(true);
            }
            Destroy(gameObject);
        }
    }
    
}