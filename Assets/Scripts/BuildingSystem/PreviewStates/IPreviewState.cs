using UnityEngine;

/// <summary>
/// State pattern interface for preview visualization.
/// Each concrete implementation handles preview display for a specific building mode:
/// - GridPreview: Tile-based object placement
/// - EdgePreview: Edge-based object placement (walls, fences)
/// - GridRemovalPreview: Tile-based object removal indicator
/// - EdgeRemovalPreview: Edge-based object removal indicator
/// 
/// ARCHITECTURE:
/// PreviewSystem acts as the context, delegating to the active IPreviewState implementation.
/// States manage their own preview GameObjects and visual feedback independently.
/// </summary>
public interface IPreviewState
{
    /// <summary>
    /// Initializes and displays the preview at the specified position.
    /// </summary>
    /// <param name="prefab">GameObject to preview (can be null for removal previews)</param>
    /// <param name="position">World position for the preview</param>
    void StartShowingPreview(GameObject prefab, Vector3 position);

    /// <summary>
    /// Updates the preview position and validity indicator.
    /// </summary>
    /// <param name="position">New world position</param>
    /// <param name="isValid">Whether the current placement is valid (affects material/color)</param>
    void UpdatePosition(Vector3 position, bool isValid);

    /// <summary>
    /// Rotates the preview around the specified pivot point.
    /// Rotation amount depends on object type (90° for grid objects, toggle for edges).
    /// </summary>
    /// <param name="pivot">World position to rotate around</param>
    void RotatePreview(Vector3 pivot);

    /// <summary>
    /// Destroys the preview GameObject and cleans up resources.
    /// </summary>
    void StopShowingPreview();
}