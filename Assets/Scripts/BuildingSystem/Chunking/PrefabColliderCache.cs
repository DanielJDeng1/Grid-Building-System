using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Caches combined local-space AABBs derived from a prefab's BoxColliders.
/// Used for precise wall cutout dimensions without relying on decorative mesh bounds.
/// </summary>
public static class PrefabColliderCache
{
    private static readonly Dictionary<GameObject, PrefabColliderBounds> _cache = new();

    public static PrefabColliderBounds Get(GameObject prefab)
    {
        if (_cache.TryGetValue(prefab, out PrefabColliderBounds cached))
            return cached;

        PrefabColliderBounds bounds = BuildBounds(prefab);
        _cache[prefab] = bounds;
        return bounds;
    }

    /// <summary>
    /// Evicts cached bounds for a prefab asset.
    /// </summary>
    public static void Invalidate(GameObject prefab)
    {
        _cache.Remove(prefab);
    }

    private static PrefabColliderBounds BuildBounds(GameObject prefab)
    {
        BoxCollider[] colliders = prefab.GetComponentsInChildren<BoxCollider>(true);

        bool hasBounds = false;
        Vector3 min = Vector3.zero, max = Vector3.zero;

        // Reintroduce root transform scale/rotation to match instance-time matrix evaluation.
        Matrix4x4 rootRotationAndScale = Matrix4x4.TRS(Vector3.zero, prefab.transform.localRotation, prefab.transform.localScale);

        foreach (BoxCollider collider in colliders)
        {
            Matrix4x4 childRelativeToRoot = prefab.transform.worldToLocalMatrix * collider.transform.localToWorldMatrix;
            Matrix4x4 localMatrix = rootRotationAndScale * childRelativeToRoot;

            Vector3 c = collider.center;
            Vector3 e = collider.size * 0.5f;

            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3(
                    c.x + ((i & 1) == 0 ? -e.x : e.x),
                    c.y + ((i & 2) == 0 ? -e.y : e.y),
                    c.z + ((i & 4) == 0 ? -e.z : e.z)
                );

                Vector3 rootCorner = localMatrix.MultiplyPoint3x4(corner);

                if (!hasBounds)
                {
                    min = rootCorner;
                    max = rootCorner;
                    hasBounds = true;
                }
                else
                {
                    min = Vector3.Min(min, rootCorner);
                    max = Vector3.Max(max, rootCorner);
                }
            }
        }

        return new PrefabColliderBounds(hasBounds, min, max);
    }
}

/// <summary>
/// Prefab-root-relative AABB bounds derived from component BoxColliders.
/// </summary>
public readonly struct PrefabColliderBounds
{
    public readonly bool hasBounds;
    public readonly Vector3 min;
    public readonly Vector3 max;

    public PrefabColliderBounds(bool hasBounds, Vector3 min, Vector3 max)
    {
        this.hasBounds = hasBounds;
        this.min = min;
        this.max = max;
    }

    public float SizeX => max.x - min.x;
    public float SizeY => max.y - min.y;
    public float SizeZ => max.z - min.z;
}