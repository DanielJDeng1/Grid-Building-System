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

    /// <summary>
    /// Returns the mouse's projected position on the ground plane (world Y = 0).
    /// Equivalent to GetSelectedMapPositionAtHeight(0f). Kept for any caller
    /// that only ever needs ground-level detection.
    /// </summary>
    public Vector3 GetSelectedMapPosition()
    {
        return GetSelectedMapPositionAtHeight(0f);
    }

    /// <summary>
    /// Returns the mouse's projected position on a horizontal plane at the
    /// given world-space height.
    /// 
    /// MULTI-LEVEL: When build height changes (Page Up/Down) without the
    /// mouse moving on screen, the X/Z the cursor is "aiming at" on the NEW
    /// height's plane is generally different from the X/Z it was aiming at
    /// on the OLD plane, because the camera is pitched rather than a
    /// top-down orthographic view. Re-raycasting against the plane at the
    /// requested height (rather than only patching the Y of a previously
    /// cached position) keeps the preview aligned with the actual cursor
    /// position at every height.
    /// </summary>
    /// <param name="height">World-space Y to project onto.</param>
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