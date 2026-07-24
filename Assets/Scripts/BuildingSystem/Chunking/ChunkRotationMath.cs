using UnityEngine;

/// <summary>
/// Computes the same placement/rotation transforms ObjectPlacer applies at runtime via
/// Transform.RotateAround / Transform.Rotate, but as a baked Matrix4x4.
///
/// This exists because chunked entries (Floor grid objects, all Edge/wall objects) are never
/// instantiated as GameObjects - there is no live Transform for them, so their final world
/// transform has to be computed directly as a matrix for use in Mesh.CombineMeshes.
///
/// IMPORTANT: The math here is intentionally kept in lockstep with ObjectPlacer.PlaceObject's
/// child-RotateAround logic and ObjectPlacer.PlaceEdge's parent-Rotate logic. If either of
/// those change, mirror the change here or chunked and non-chunked placements will visually
/// diverge for the same ID/rotation.
/// </summary>
public static class ChunkRotationMath
{
    /// <summary>
    /// Mirrors PlaceObject's behavior: the object itself is never rotated - instead each child
    /// is rotated around a pivot at the tile center. Rotating every child of a rigid prefab
    /// around the same external pivot is equivalent to rotating the whole prefab's content
    /// around that pivot, so this returns that single equivalent world matrix.
    /// </summary>
    public static Matrix4x4 GetGridObjectMatrix(Vector3 position, GridRotation rotation)
    {
        Vector3 pivot = new Vector3(position.x + 0.5f, position.y, position.z + 0.5f);
        float angle = (int)rotation * 90f;

        Matrix4x4 pivotRotation =
            Matrix4x4.Translate(pivot) *
            Matrix4x4.Rotate(Quaternion.AngleAxis(angle, Vector3.up)) *
            Matrix4x4.Translate(-pivot);

        Matrix4x4 baseMatrix = Matrix4x4.Translate(position);

        return pivotRotation * baseMatrix;
    }

    /// <summary>
    /// Mirrors PlaceEdge's behavior: position is set, then the object is rotated in place
    /// (around its own origin, which sits at `position`) by 0 or -90 degrees.
    /// </summary>
    public static Matrix4x4 GetEdgeObjectMatrix(Vector3 position, EdgeRotation rotation)
    {
        float angle = rotation == EdgeRotation.Deg0 ? 0f : -90f;

        return Matrix4x4.Translate(position) * Matrix4x4.Rotate(Quaternion.AngleAxis(angle, Vector3.up));
    }
}
