using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Threading.Tasks;
using UnityEngine.UIElements;

public class Move : MonoBehaviour
{
    [Header("Add AS for force-based movement")]
    public ActionScript AS;

    public float magnitude = 1f;
    public float duration = 1f;
    [SerializeField]
    float acceleration = 0f;
    public List<Vector2> positions = new List<Vector2> { };
    [Header("If interwait = 0, calculates a relevant number")]
    [SerializeField] float interWait = 0f;
    [SerializeField] bool rotateByTransformAtStart;
    [SerializeField]
    float rotation = 1f;
    [SerializeField]
    bool destroyOnEnd = false;
    [SerializeField]
    [Range(0,1)]
    float randomness = 0f;

    private float time;
    private Vector2 startPos;
    [Header("positions n wil be about 20 x film duration")]
    [Range(0.25f, 6f)]
    public float filmDuration = 1f;
    [Header("setDistance = 0f gives no min distance, thus how long you hover will affect movement")]
    public float setDistance = 0f;
    public bool randEveryFrame = false;
    public bool flipX = false;
    private int lastUsedN = 40;
 
    private void Awake()
    {
        time = 0f;
        Randomise();
        startPos = transform.position;
    }

    private IEnumerator Start()
    {
        if (flipX)
        {
            FlipX();
        }
        if (rotateByTransformAtStart)
        {
            RotateAllByTransform();
        }
        //if (randEveryFrame)
        //{
        //    while (true)
        //    {
        //        Randomise();
        //        yield return new WaitForFixedUpdate();
        //    }
        //}
        float wait = interWait == 0f ? duration / positions.Count : interWait;
        if(wait < 0.02f)
        {
            Debug.Log("sub 20 ms wait in move script",gameObject);
        }
        for(int j = 0; j < positions.Count; j++)
        {
            if (AS != null)
            {
                AS.TryAddForce(magnitude * 60 * (1 + acceleration * time + 1) * GetNextDir(j), true);
            }
            else
            {
                transform.position += magnitude * (acceleration * time + 1) * (Vector3)GetNextDir(j);
            }
            yield return new WaitForSeconds(wait);
        }
        if (destroyOnEnd)
        {
            Destroy(gameObject);
        }
        else
        {
            Destroy(this);
        }
    }

    private void Update()
    {
        time += Time.deltaTime;
    }

    public void RotateAllByTransform()
    {
        for(int i = 0; i < positions.Count; i++)
        {
            positions[i] = positions[i].Rotated(transform.rotation.eulerAngles.z);
        }
    }

    public void FlipX()
    {
        for (int i = 0; i < positions.Count; i++)
        {
            positions[i] = new Vector2(-positions[i].x, positions[i].y);
        }
    }

    private Vector2 GetNextDir(int i)
    {
        if(i == positions.Count - 1)
        {
            return Vector2.zero;
        }
        Vector2 v = positions[i + 1] - positions[i];
        if (rotation > 0f && v.sqrMagnitude > 0)
        {
            transform.up = Vector2.Lerp(transform.up, v, rotation);
        }
        return v;
    }

    public async void SetPositions()
    {
        Task T = TrackMouse();
        await T;
    }

    public async void ContinuePositions()
    {
        Task T = ContinueTrackingMouse();
        await T;
    }

    private async Task TrackMouse()
    {
        positions.Clear();
        Camera cam = Camera.main;
        await Task.Delay(1000);
        Debug.Log("Tracking Mouse");
        lastUsedN = Mathf.CeilToInt(filmDuration * 20);
        for (int i = 0; i < filmDuration * 20; i++)
        {
            if (setDistance > 0)
            {
                if (positions.Count == 0)
                {
                    positions.Add(cam.ScreenToWorldPoint(Mouse.current.position.ReadValue()));
                }
                else if (Vector2.Distance(cam.ScreenToWorldPoint(Mouse.current.position.ReadValue()), positions[^1]) > setDistance)
                {
                    positions.Add(((Vector2)cam.ScreenToWorldPoint(Mouse.current.position.ReadValue()) - positions[^1]).normalized * setDistance + positions[^1]);
                    i -= 1;
                    continue;
                }
            }
            else
            {
                positions.Add(cam.ScreenToWorldPoint(Mouse.current.position.ReadValue()));
            }
            await Task.Delay(50);
        }
        Vector2 offset = positions[0];
        for(int i = 0; i < positions.Count; i++)
        {
            positions[i] -= offset;
        }
        Debug.Log("Finished Tracking");
    }

    private async Task ContinueTrackingMouse()
    {
        Camera cam = Camera.main;
        await Task.Delay(1000);
        Debug.Log("Tracking Mouse");
        int startC = positions.Count;
        lastUsedN = Mathf.CeilToInt(filmDuration * 20);
        for (int i = startC; i < filmDuration * 20 + startC; i++)
        {
            if (setDistance > 0)
            {
                if (positions.Count == 0)
                {
                    positions.Add(cam.ScreenToWorldPoint(Mouse.current.position.ReadValue()));
                }
                else if (Vector2.Distance(cam.ScreenToWorldPoint(Mouse.current.position.ReadValue()), positions[^1]) > setDistance)
                {
                    positions.Add(((Vector2)cam.ScreenToWorldPoint(Mouse.current.position.ReadValue()) - positions[^1]).normalized * setDistance + positions[^1]);
                    i -= 1;
                    continue;
                }
            }
            else
            {
                positions.Add(cam.ScreenToWorldPoint(Mouse.current.position.ReadValue()));
            }
            await Task.Delay(50);
        }
        Vector2 offset = positions[0];
        for (int i = 0; i < positions.Count; i++)
        {
            positions[i] -= offset;
        }
        Debug.Log("Finished Tracking");
    }

    public void Randomise()
    {
        Vector2 rand = Vector2.zero;
        for (int i = 1; i < positions.Count; i++)
        {
            rand += Random.insideUnitCircle * randomness * (setDistance > 0f? setDistance : 0.1f);
            positions[i] = Vector2.Lerp(positions[i], positions[i] + rand,0.5f);
        }
    }

    public void Undo()
    {
        positions.RemoveRange(positions.Count - lastUsedN - 1, lastUsedN);
    }

    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying)
        {
            startPos = transform.position;
        }
        for (int i = 1; i < positions.Count; i++)
        {
            Gizmos.DrawLine(startPos + positions[i - 1], startPos + positions[i]);
        }

    }
}