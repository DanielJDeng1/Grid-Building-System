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

    [field: SerializeField] public List<Vector2Int> positionsFilled = new List<Vector2Int>{Vector2Int.one};

    [field: SerializeField] public GameObject prefab;

    [field: SerializeField] public int ID;

}
