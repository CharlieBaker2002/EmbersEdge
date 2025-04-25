using UnityEngine;

public class WigglyMissile : MonoBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float speed = 3f;
    [SerializeField] private float frequency = 3f;
    [SerializeField] private float amplitude = 0.5f;
    [SerializeField] private ParticleSystem ps;
    [SerializeField] private GameObject[] FX;
    private Vector3 startPos;
    private Vector3 targetPos;
    public void SetTarget(Vector3 tgt, int level,float speedMultiplier)
    {
        startPos = transform.position;
        targetPos = tgt;
        speed *= speedMultiplier;
        float dist = Vector3.Distance(startPos, targetPos);
        float duration = dist / speed;
        duration = Mathf.Lerp(0.5f, duration, 0.75f);
        frequency *= duration;
        if (level == 1)
        {
            LeanTween.value(gameObject, 0f, 1f, duration)
                .setOnUpdate((float fraction) =>
                {
                    Vector3 direction = (targetPos - startPos).normalized;
                    Vector3 basePos   = Vector3.Lerp(startPos, targetPos, fraction);

                    Vector3 perpendicular = Vector3.Cross(direction, Vector3.forward);
                    float wiggle          = Mathf.Sin(frequency * fraction * Mathf.PI * 2f) * amplitude;

                    transform.position = basePos + perpendicular * wiggle;
                    transform.up       = direction;
                    transform.localScale = Vector3.one * (1.1f - fraction);
                }).setEaseInBack()
                .setOnComplete(() =>
                {
                    Instantiate(FX[0], transform.position, transform.rotation, GS.FindParent(GS.Parent.allyprojectiles));
                    ps.Stop();
                    Destroy(gameObject,4f);
                });
        }
        else if (level == 2)
        {
            LeanTween.value(gameObject, 0f, 1f, duration*0.75f)
                .setOnUpdate((float fraction) =>
                {
                    Vector3 direction = (targetPos - startPos).normalized;
                    Vector3 basePos   = Vector3.Lerp(startPos, targetPos, fraction);

                    Vector3 perpendicular = Vector3.Cross(direction, Vector3.forward);
                    float wiggle          = Mathf.Sin(frequency * fraction * Mathf.PI * 2f) * amplitude;

                    transform.position = basePos + perpendicular * wiggle;
                    transform.up       = direction;
                    transform.localScale = Vector3.one * (1.1f - fraction);
                }).setEaseInExpo()
                .setOnComplete(() =>
                {
                    Instantiate(FX[1], transform.position, transform.rotation, GS.FindParent(GS.Parent.allyprojectiles));
                    ps.Stop();
                    Destroy(gameObject,4f);
                });
        }
        else
        {
            LeanTween.value(gameObject, 0f, 1f, duration*0.5f)
                .setOnUpdate((float fraction) =>
                {
                    Vector3 direction = (targetPos - startPos).normalized;
                    Vector3 basePos   = Vector3.Lerp(startPos, targetPos, fraction);
                    float amp = Mathf.Lerp(amplitude,0f,fraction);
                    Vector3 perpendicular = Vector3.Cross(direction, Vector3.forward);
                    float wiggle          = Mathf.Sin(frequency * fraction * Mathf.PI * 2f) * amp;

                    transform.position = basePos + perpendicular * wiggle;
                    transform.up       = direction;
                    transform.localScale = Vector3.one * (1.1f - fraction);
                }).setEaseInQuint()
                .setOnComplete(() =>
                {
                    Instantiate(FX[2], transform.position, transform.rotation, GS.FindParent(GS.Parent.allyprojectiles));
                    ps.Stop();
                    Destroy(gameObject,4f);
                });
        }
    }
}