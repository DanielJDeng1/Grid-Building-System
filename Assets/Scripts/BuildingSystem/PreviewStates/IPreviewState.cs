using UnityEngine;

/// <summary>
/// Contract for mode-specific placement and removal previews driven by PreviewSystem.
/// </summary>
public interface IPreviewState
{
    /// <summary>
    /// Spawns or enables the preview visual at the target world position.
    /// </summary>
    void StartShowingPreview(GameObject prefab, Vector3 position);

    /// <summary>
    /// Updates transform position and toggles valid/invalid placement materials.
    /// </summary>
    void UpdatePosition(Vector3 position, bool isValid);

    /// <summary>
    /// Rotates preview geometry around the specified pivot point.
    /// </summary>
    void RotatePreview(Vector3 pivot);

    /// <summary>
    /// Deactivates and cleans up active preview visual instances.
    /// </summary>
    void StopShowingPreview();
}