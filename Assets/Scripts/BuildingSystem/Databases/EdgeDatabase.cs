using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "EdgeDatabase", menuName = "Scriptable Objects/EdgeDatabase")]
public class EdgeDatabase : ScriptableObject
{
    public List<EdgeData> edgeData;
}

[Serializable]
public class EdgeData
{
    [field: SerializeField] public string name;

    [field: SerializeField] public List<int> positionsFilled = new List<int>{0};

    [field: SerializeField] public GameObject prefab;

    [field: SerializeField] public int ID;

    [field: SerializeField] public ObjectBuildType buildType;

    [field: SerializeField]
    [field: Tooltip("If true, this edge type is mesh-chunked (combined into contiguous wall runs, no individual GameObject). If false, it's instantiated individually like Furniture - use this for doors, edge-mounted furniture, or anything else that needs its own GameObject identity rather than being merged into a wall run.")]
    public bool shouldChunk = true;

}