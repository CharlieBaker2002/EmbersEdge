using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.Events;

public class HoverWindow : MonoBehaviour, IClickable
{
    [SerializeField] GraphicRaycaster gr;
    [SerializeField] Camera mainCamera;
    public bool hover = true;
    public Vector2 percentMouse;
    public UnityEvent<Vector2> onClicked;
    private void Update()
    {
        hover = false;
        PointerEventData PED = new PointerEventData(EventSystem.current);
        PED.position = (IM.controller) ? mainCamera.WorldToScreenPoint(IM.i.controllerCursor.position) : Mouse.current.position.ReadValue();
        List<RaycastResult> results = new List<RaycastResult>();
        gr.Raycast(PED, results);
        if (results.Count > 0)
        {
            foreach (var result in results)
            {
                if (result.gameObject == gameObject)
                {
                    hover = true;
                }
            }
        }
        if (hover)
        {
            RectTransform rectTransform = GetComponent<RectTransform>();
            Vector2 localPointerPosition;
            // Convert screen point to local point in rectangle
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, PED.position, mainCamera, out localPointerPosition))
            {
                // Normalize the local pointer position within the rect bounds
                float normalizedX = (localPointerPosition.x - rectTransform.rect.min.x) / rectTransform.rect.width;
                float normalizedY = (localPointerPosition.y - rectTransform.rect.min.y) / rectTransform.rect.height;

                percentMouse = new Vector2(normalizedX, normalizedY);
            }
        }
    }

    public void OnClick()
    {
        onClicked.Invoke(percentMouse);
    }
}
