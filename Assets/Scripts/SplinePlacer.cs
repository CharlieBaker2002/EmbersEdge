using UnityEngine;
using UnityEngine.Splines;
using UnityEngine.InputSystem;
using System.Threading.Tasks;

public class SplinePlacer : MonoBehaviour
{
    [ExecuteInEditMode]
    public async void PlaceSpline()
    {
        PlayerInput p = new PlayerInput();
        SplineContainer s = GetComponent<SplineContainer>();
        Debug.Log(Mouse.current.position.ReadValue().x);
        while (Mouse.current.position.ReadValue().x > 5)
        {
            Debug.Log(Mouse.current.leftButton.ReadValue());
            if (Mouse.current.leftButton.ReadValue() > 0)
            {
                Debug.Log("Pressed");
                s.Spline.Add(new BezierKnot((Vector3)(Vector2)Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue())));
                await Task.Delay(500);
            }
            await Task.Delay(10);
        }
       
    }

    public void DeleteSplineData()
    {
        SplineContainer s = GetComponent<SplineContainer>();
        s.Spline.Clear();
    }
}
