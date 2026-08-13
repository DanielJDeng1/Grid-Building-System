using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plain-data save format for the building system. Deliberately holds ONLY what a placement
/// needs to be replayed - an ID (resolving prefab/config via ObjectDatabase/EdgeDatabase/
/// WallOpeningDatabase), a base position, and a rotation. Everything else the building system
/// produces at placement time (GameObjects, chunk mesh data, ObjectPlacer handles, nav obstacle
/// registrations, the nav graph itself) is derived state, rebuilt by replaying these entries
/// through the normal placement code paths (GridState/EdgeState/TraversalState/WallOpeningState's
/// PlaceDirect methods) rather than being serialized directly. See PlacementSystem.CaptureSaveData/
/// LoadSaveData for the capture/replay implementation.
/// 
/// IDs are saved rather than database list indices deliberately - ObjectDatabase/EdgeDatabase/
/// WallOpeningDatabase are ScriptableObject-backed Lists whose ORDER is not a stable contract
/// (reordering entries in the Inspector would silently resave old saves against the wrong
/// prefab if index were used instead). ID is the explicit stable key already present on every
/// ObjectData/EdgeData/WallOpeningData entry.
/// </summary>
[Serializable]
public struct PlacedObjectSaveEntry
{
    public int id;
    public Vector3Int basePosition;
    public GridRotation rotation;
}

[Serializable]
public struct PlacedEdgeSaveEntry
{
    public int id;

    /// <summary>
    /// The base edge's end1 - the tile position CalculateBaseEdge(tilePosition, rotation)
    /// needs to reconstruct the same edge. end2 is derivable from end1+rotation and is stored
    /// only for readability/debugging of the save file, not read during replay.
    /// </summary>
    public Vector3Int baseEdgeEnd1;
    public Vector3Int baseEdgeEnd2;
    public EdgeRotation rotation;
}

[Serializable]
public struct WallOpeningSaveEntry
{
    public int id;
    public Vector3Int basePosition;
    public EdgeRotation rotation;
}

[Serializable]
public class BuildingSaveData
{
    /// <summary>
    /// Bump this whenever BuildingSaveData's shape changes in a way that breaks old saves, and
    /// branch on it in PlacementSystem.LoadSaveData if a migration path is ever needed. Starts
    /// at 1 - there is no version 0.
    /// </summary>
    public const int CurrentVersion = 1;

    public int saveVersion = CurrentVersion;

    /// <summary>Floor/Furniture/Ceiling/CeilingFurniture entries - GridState resolves the correct layer internally from each id's buildType.</summary>
    public List<PlacedObjectSaveEntry> gridObjects = new();

    /// <summary>Stair/elevator entries - kept separate from gridObjects because replay goes through TraversalState, not GridState (different side effect: NavLink registration).</summary>
    public List<PlacedObjectSaveEntry> traversalObjects = new();

    /// <summary>Wall/fence/railing/door-as-edge entries across Floor/Furniture/Ceiling layers - EdgeState resolves the correct layer internally from each id's buildType, same as gridObjects.</summary>
    public List<PlacedEdgeSaveEntry> edges = new();

    /// <summary>Door/window entries - replayed last, since they validate against already-placed wall tiles.</summary>
    public List<WallOpeningSaveEntry> openings = new();
}