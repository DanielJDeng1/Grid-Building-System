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
    [field: Tooltip("True merges into wall runs without individual GameObjects. False preserves individual identity (e.g., doors, fixtures).")]
    public bool shouldChunk = true;
}