using UnityEngine;
using System;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "WallOpeningDatabase", menuName = "Scriptable Objects/WallOpeningDatabase")]
public class WallOpeningDatabase : ScriptableObject
{
    public List<WallOpeningData> openingData;
}

/// <summary>
/// Configuration for wall-embedded assets (doors, windows) that link to existing walls rather than owning independent edge occupancy.
/// </summary>
[Serializable]
public class WallOpeningData
{
    [field: SerializeField] public string name;

    [field: SerializeField] public GameObject prefab;

    [field: SerializeField] public int ID;

    [field: SerializeField]
    [field: Tooltip("Grid layer of the target wall (must match target EdgeData buildType).")]
    public ObjectBuildType buildType;

    [field: SerializeField]
    [field: Tooltip("Tile offsets along the wall axis spanned by this opening.")]
    public List<int> positionsFilled = new List<int> { 0 };

    [field: SerializeField]
    [field: Tooltip("True merges into host wall mesh (static windows); false keeps individual GameObject identity (animated doors).")]
    public bool shouldChunk = false;
}