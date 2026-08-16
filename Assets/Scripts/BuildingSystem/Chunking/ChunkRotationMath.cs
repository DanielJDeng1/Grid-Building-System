using UnityEngine;

/// <summary>
/// Bakes world transformation matrices for chunked grid and edge meshes without instantiating GameObjects
/// Maintains parity with ObjectPlacer rotation logic to prevent visual displacement in combined meshes
/// </summary>
public static class ChunkRotationMath
{
    /// <summary>
    /// Bakes center-pivot tile rotation into matrix to match ObjectPlacer's child rotation behavior
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
    /// Bakes edge origin translation and orthogonal Y-axis rotation into world matrix
    /// </summary>
    public static Matrix4x4 GetEdgeObjectMatrix(Vector3 position, EdgeRotation rotation)
    {
        float angle = rotation == EdgeRotation.Deg0 ? 0f : -90f;

        return Matrix4x4.Translate(position) * Matrix4x4.Rotate(Quaternion.AngleAxis(angle, Vector3.up));
    }
}