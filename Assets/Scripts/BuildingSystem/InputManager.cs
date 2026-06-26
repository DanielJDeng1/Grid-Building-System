using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.EventSystems;
using UnityEngine;
using UnityEngine.InputSystem; // Added for the new Input System

/// <summary>
/// Handles all logic regarding player input
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

    [SerializeField] private LayerMask placementLayermask;

    /// <summary>
    /// Raycasts and finds the collidable position the mouse is pointing at
    /// If no such position exists, no change to lastPosition occurs
    /// </summary>
    /// 
    /// <returns>
    /// Vector3 that is the last collidable position the mouse pointed at
    /// </returns>
    public Vector3 GetSelectedMapPosition()
    {
        var mouse = Mouse.current;

        Vector3 mousePos = mouse.position.ReadValue();
        mousePos.z = sceneCamera.nearClipPlane;
        Ray ray = sceneCamera.ScreenPointToRay(mousePos);
        RaycastHit hit;
        
        if (Physics.Raycast(ray, out hit, 100, placementLayermask))
        {
            lastPosition = hit.point;
        }

        return lastPosition;
    }
}