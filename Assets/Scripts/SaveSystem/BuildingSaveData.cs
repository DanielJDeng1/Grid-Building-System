using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Minimal save schema for placed objects. Stores base placement parameters required to replay build sequences
/// and regenerate derived runtime state (GameObjects, meshes, nav obstacles). Uses database IDs instead of list indices
/// to preserve data stability across ScriptableObject ordering changes.
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
    /// Primary cell anchor required to reconstruct the edge. end2 is derived during replay and kept for debugging.
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
    /// Schema version used by PlacementSystem.LoadSaveData to handle file migrations.
    /// </summary>
    public const int CurrentVersion = 1;

    public int saveVersion = CurrentVersion;

    /// <summary>Grid-aligned objects (floors, furniture, ceilings) resolved by GridState.</summary>
    public List<PlacedObjectSaveEntry> gridObjects = new();

    /// <summary>Vertical traversal objects (stairs, elevators) processed via TraversalState to bind NavLinks.</summary>
    public List<PlacedObjectSaveEntry> traversalObjects = new();

    /// <summary>Edge-aligned structures (walls, fences, railings) resolved by EdgeState.</summary>
    public List<PlacedEdgeSaveEntry> edges = new();

    /// <summary>Wall openings (doors, windows) replayed last to validate against established wall tiles.</summary>
    public List<WallOpeningSaveEntry> openings = new();
}