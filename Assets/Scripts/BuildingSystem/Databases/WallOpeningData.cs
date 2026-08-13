using UnityEngine;
using System;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "WallOpeningDatabase", menuName = "Scriptable Objects/WallOpeningDatabase")]
public class WallOpeningDatabase : ScriptableObject
{
    public List<WallOpeningData> openingData;
}

/// <summary>
/// A placeable that embeds into an already-placed wall edge and cuts a hole in it (doors,
/// windows, etc). Deliberately a SEPARATE type from EdgeData rather than another EdgeData flag:
/// an EdgeData/wall entry OWNS its own GridData occupancy and lifecycle; a WallOpeningData
/// entry only LINKS to a wall that must already exist, and is cascade-deleted when that wall
/// goes away - see WallOpeningLinkService. Folding that relationship into EdgeData would mean
/// every consumer of EdgeData (EdgeState, EdgeRemovalState, WallChunkManager) would need to
/// start caring about a distinction that's actually irrelevant to them.
/// </summary>
[Serializable]
public class WallOpeningData
{
    [field: SerializeField] public string name;

    [field: SerializeField] public GameObject prefab;

    [field: SerializeField] public int ID;

    [field: SerializeField]
    [field: Tooltip("Which GridData layer (Floor/Furniture/Ceiling) the underlying wall lives on - must match the target wall EdgeData's buildType, or validation will always fail.")]
    public ObjectBuildType buildType;

    [field: SerializeField]
    [field: Tooltip("Tile offsets (along the wall's run axis) this opening spans, matching EdgeData.positionsFilled semantics. {0} for a single-tile door/window. EVERY offset must land on an existing chunked wall tile or placement is rejected.")]
    public List<int> positionsFilled = new List<int> { 0 };

    [field: SerializeField]
    [field: Tooltip("If true, mesh-chunked into the host wall's combined mesh (no individual GameObject) - use for static, non-animated openings like windows. If false, instantiated individually via ObjectPlacer's free-list path - required for anything needing an Animator or other live-Transform behaviour, e.g. doors.")]
    public bool shouldChunk = false;
}