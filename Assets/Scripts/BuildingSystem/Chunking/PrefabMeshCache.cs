using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Decomposes prefabs into flat lists of (mesh, submesh, material, local transform) parts,
/// suitable for feeding directly into Mesh.CombineMeshes without ever instantiating the prefab.
///
/// Wall/floor prefabs may have multiple child MeshFilters with different materials (e.g. a wall
/// with a separate trim mesh, or a floor tile with two material slots). Each (mesh, submesh)
/// pair becomes its own PrefabMeshPart so chunk rebuilding can group everything by material
/// correctly.
///
/// Results are cached per-prefab since this decomposition only needs to happen once - the
/// prefab asset's hierarchy never changes at runtime.
/// </summary>
public static class PrefabMeshCache
{
    private static readonly Dictionary<GameObject, PrefabMeshData> _cache = new();

    public static PrefabMeshData Get(GameObject prefab)
    {
        if (_cache.TryGetValue(prefab, out PrefabMeshData cached))
            return cached;

        PrefabMeshData data = BuildMeshData(prefab);
        _cache[prefab] = data;
        return data;
    }

    /// <summary>
    /// Clears cached data for a specific prefab. Call this if a prefab's meshes/materials
    /// are ever swapped at runtime (rare, but avoids stale combined chunk meshes).
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

            // Transform of this child relative to the prefab ROOT (not world space - the
            // prefab asset isn't placed in any scene). This alone discards the root's OWN
            // local rotation/scale (root.worldToLocal * root.localToWorld cancels to Identity
            // regardless of what the root's scale/rotation actually are), which is wrong: at
            // runtime, Instantiate(prefab) copies the root's authored rotation/scale along
            // with everything else, so any corrective scale/rotation baked onto the root
            // (common for multi-part prefabs combining differently-authored sub-meshes) still
            // applies. Placement (ChunkRotationMath) only ever supplies position + a placement
            // rotation - it has no notion of the root's own authored scale/rotation - so that
            // has to be reintroduced here instead, once per prefab, or it silently disappears
            // for any prefab whose root isn't already an identity transform.
            Matrix4x4 rootRotationAndScale = Matrix4x4.TRS(Vector3.zero, prefab.transform.localRotation, prefab.transform.localScale);
            Matrix4x4 childRelativeToRoot = prefab.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            Matrix4x4 localMatrix = rootRotationAndScale * childRelativeToRoot;

            Material[] materials = renderer.sharedMaterials;
            int subMeshCount = filter.sharedMesh.subMeshCount;

            for (int sub = 0; sub < subMeshCount; sub++)
            {
                // Defensive: if a mesh has more submeshes than assigned materials, fall back
                // to the last material rather than throwing, mirroring Unity's own behavior.
                Material material = sub < materials.Length ? materials[sub] : materials[materials.Length - 1];

                parts.Add(new PrefabMeshPart(filter.sharedMesh, sub, material, localMatrix));
            }
        }

        return new PrefabMeshData(parts);
    }
}

/// <summary>
/// One (mesh, submesh, material) piece of a prefab, with its transform relative to the prefab root.
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
/// All mesh parts making up a single prefab, cached by PrefabMeshCache.
/// </summary>
public class PrefabMeshData
{
    public readonly List<PrefabMeshPart> parts;

    public PrefabMeshData(List<PrefabMeshPart> parts)
    {
        this.parts = parts;
    }
}