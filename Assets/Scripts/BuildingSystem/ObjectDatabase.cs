using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "ObjectDatabase", menuName = "Scriptable Objects/ObjectDatabase")]
public class ObjectDatabase : ScriptableObject
{
    public List<ObjectData> objectsData;

    public void Awake()
    {
        int id = 0;
        foreach (var obj in objectsData)
        {
            obj.ID = id++;
        }
    }

}

[Serializable]

public class ObjectData
{
    [field: SerializeField] public string name;
    [field: SerializeField] public string description;

    [field: SerializeField] public BuildType type;

    [field: SerializeField] public Vector2Int size = Vector2Int.one;

    [field: SerializeField] public Vector2Int buildPivot = Vector2Int.zero;

    [field: SerializeField] public GameObject prefab;

    [field: SerializeField] public GameObject preview;

    [field: SerializeField] public int movementPenalty;

    //[field: SerializeField] public PlacedObject objData;

    //[field: SerializeField] public PlacedEdge edgeData;

    [field: SerializeField] public Sprite image;

    [field: SerializeField] public int edgeLength;

    public int ID;

}

public enum BuildType
{
    floor = 0,
    furniture = 1,
    ceiling = 2,
    wall = 3,
    ceilingFurniture = 4
}
