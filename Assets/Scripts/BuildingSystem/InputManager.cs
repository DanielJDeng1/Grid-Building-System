using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.EventSystems;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Hardware input bridge that blocks gameplay actions when cursor hovers over UI
/// </summary>
public class InputManager : MonoBehaviour
{
    public event Action OnMouseDown;
    public event Action OnMouseRelease;
    public event Action OnExit;
    public event Action OnPressR;

    public event Action OnPageUp;

    public event Action OnPageDown;

    void Update()
    {
        if (!IsPointerOverUI())
        {
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;

            if (mouse != null)
            {
                if (mouse.leftButton.wasPressedThisFrame)
                    OnMouseDown?.Invoke();
                
                if (mouse.leftButton.wasReleasedThisFrame)
                    OnMouseRelease?.Invoke();
            }

            if (keyboard != null)
            {
                if (keyboard.escapeKey.wasPressedThisFrame)
                    OnExit?.Invoke();
                
                if (keyboard.rKey.wasPressedThisFrame)
                    OnPressR?.Invoke();

                if (keyboard.periodKey.wasPressedThisFrame)
                    OnPageUp?.Invoke();

                if (keyboard.commaKey.wasPressedThisFrame)
                    OnPageDown?.Invoke();
            }
        }
    }

    public bool IsPointerOverUI() => EventSystem.current.IsPointerOverGameObject();

    [SerializeField] private Camera sceneCamera;

    private Vector3 lastPosition;

    /// <summary>
    /// Ground-level shortcut targeting Y=0 plane
    /// </summary>
    public Vector3 GetSelectedMapPosition()
    {
        return GetSelectedMapPositionAtHeight(0f);
    }

    /// <summary>
    /// Projects cursor ray onto arbitrary Y-plane to correct camera pitch parallax when shifting elevation
    /// </summary>
    public Vector3 GetSelectedMapPositionAtHeight(float height)
    {
        var mouse = Mouse.current;
        if (mouse == null)
            return lastPosition;

        Vector3 mousePos = mouse.position.ReadValue();
        mousePos.z = sceneCamera.nearClipPlane;
        Ray ray = sceneCamera.ScreenPointToRay(mousePos);

        Plane heightPlane = new Plane(Vector3.up, new Vector3(0f, height, 0f));

        if (heightPlane.Raycast(ray, out float enter))
        {
            lastPosition = ray.GetPoint(enter);
        }

        return lastPosition;
    }
}