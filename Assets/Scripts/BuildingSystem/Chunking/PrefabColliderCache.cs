using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Computes a single combined local-space AABB from all BoxColliders on a prefab, expressed
/// relative to the prefab's root transform. Used to derive wall-opening cutout dimensions
/// directly from a prefab's collider instead of authoring separate metadata - see
/// WallOpeningCutPlanner and WallSegmentCutCache.
///
/// WHY COLLIDER, NOT MESH: mesh bounds pull in trim/decorative geometry that can extend past
/// the wall's actual blocking volume (overhangs, skirting, corner brackets), which would throw
/// off cut placement. The collider is already the authoritative "solid volume" for this system
/// (Chunk generates its own physics colliders from mesh bounds today, but that's a runtime
/// chunk-level box, not a reusable per-prefab local-space one - this is the per-prefab
/// equivalent, read from hand-authored colliders instead).
///
/// BOX COLLIDERS ONLY: consistent with the rest of this system (see Chunk.ColliderMode and
/// EncapsulateBounds) - every rotation here is a 90-degree multiple, so axis-aligned boxes are
/// always an exact fit. Non-box colliders are ignored; a prefab needing a different collider
/// shape for other purposes (e.g. a door leaf's own trigger) should carry a dedicated
/// BoxCollider sized to the desired cutout instead.
///
/// Results are cached per-prefab like PrefabMeshCache, since prefab asset hierarchies never
/// change at runtime.
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

    /// <summary>Clears cached bounds for a specific prefab - call if its colliders are ever swapped at runtime.</summary>
    public static void Invalidate(GameObject prefab)
    {
        _cache.Remove(prefab);
    }

    private static PrefabColliderBounds BuildBounds(GameObject prefab)
    {
        BoxCollider[] colliders = prefab.GetComponentsInChildren<BoxCollider>(true);

        bool hasBounds = false;
        Vector3 min = Vector3.zero, max = Vector3.zero;

        // Same root rotation/scale reintroduction as PrefabMeshCache.BuildMeshData - a corrective
        // scale/rotation baked onto the prefab's ROOT still applies at Instantiate() time, and
        // has to be reintroduced here once per prefab or it silently disappears from the bounds.
        // See PrefabMeshCache's comment on this for the full explanation.
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

/// <summary>Combined local-space AABB (relative to prefab root) from a prefab's BoxColliders.</summary>
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
