using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Caches decomposed prefab mesh data to avoid redundant hierarchy traversal during batching.
/// </summary>
public static class PrefabMeshCache
{
    private static readonly Dictionary<GameObject, PrefabMeshData> _cache = new();

    /// <summary>
    /// Returns cached mesh parts for the prefab, parsing and caching on cache miss.
    /// </summary>
    public static PrefabMeshData Get(GameObject prefab)
    {
        if (_cache.TryGetValue(prefab, out PrefabMeshData cached))
            return cached;

        PrefabMeshData data = BuildMeshData(prefab);
        _cache[prefab] = data;
        return data;
    }

    /// <summary>
    /// Evicts cached entry when source prefab assets change.
    /// </summary>
    public static void Invalidate(GameObject prefab)
    {
        _cache.Remove(prefab);
    }

    private static PrefabMeshData BuildMeshData(GameObject prefab)
    {
        var parts = new List<PrefabMeshPart>();
        MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>(true);

        foreach (MeshFilter filter in filters)
        {
            if (filter.sharedMesh == null)
                continue;

            MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
            if (renderer == null)
                continue;

            // Preserve root-level authored scale/rotation transformations
            Matrix4x4 rootRotationAndScale = Matrix4x4.TRS(Vector3.zero, prefab.transform.localRotation, prefab.transform.localScale);
            Matrix4x4 childRelativeToRoot = prefab.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            Matrix4x4 localMatrix = rootRotationAndScale * childRelativeToRoot;

            Material[] materials = renderer.sharedMaterials;
            int subMeshCount = filter.sharedMesh.subMeshCount;

            for (int sub = 0; sub < subMeshCount; sub++)
            {
                Material material = sub < materials.Length ? materials[sub] : materials[materials.Length - 1];
                parts.Add(new PrefabMeshPart(filter.sharedMesh, sub, material, localMatrix));
            }
        }

        return new PrefabMeshData(parts);
    }
}

/// <summary>
/// Submesh slice and its baked transform relative to the prefab root.
/// </summary>
public readonly struct PrefabMeshPart
{
    public readonly Mesh mesh;
    public readonly int subMeshIndex;
    public readonly Material material;
    public readonly Matrix4x4 localMatrix;

    public PrefabMeshPart(Mesh mesh, int subMeshIndex, Material material, Matrix4x4 localMatrix)
    {
        this.mesh = mesh;
        this.subMeshIndex = subMeshIndex;
        this.material = material;
        this.localMatrix = localMatrix;
    }
}

/// <summary>
/// Container for batch-ready prefab mesh parts.
/// </summary>
public class PrefabMeshData
{
    public readonly List<PrefabMeshPart> parts;

    public PrefabMeshData(List<PrefabMeshPart> parts)
    {
        this.parts = parts;
    }
}