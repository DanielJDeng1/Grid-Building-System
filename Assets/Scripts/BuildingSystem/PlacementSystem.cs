using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

public class PlacementSystem : MonoBehaviour
{
    [SerializeField] private InputManager inputManager;

    [SerializeField] private Grid grid;

    [SerializeField] private ObjectDatabase objectDatabase;

    [SerializeField] private EdgeDatabase edgeDatabase;

    private int selectedObjectIndex = -1;

    [SerializeField] private GameObject gridVisualization;

    [SerializeField] private PreviewSystem preview;

    private GridData floorData, furnitureData, ceilingData;

    [SerializeField] private ObjectPlacer objectPlacer;

    private Vector3Int lastDetectedPosition = Vector3Int.zero;

    private void Start()
    {
        StopPlacement();
        floorData = new();
        furnitureData = new();
        ceilingData = new();
    }

    public void StartPlacement(int ID)
    {
        StopPlacement();
        selectedObjectIndex = objectDatabase.objectsData.FindIndex(data => data.ID == ID);
        
        if (selectedObjectIndex < 0){
            Debug.LogError($"No ID found {ID}");
            return;
        }

        gridVisualization.SetActive(true);
        preview.StartShowingPlacementPreview(objectDatabase.objectsData[selectedObjectIndex].prefab);
        inputManager.OnMouseRelease += PlaceStructure;
        inputManager.OnExit += StopPlacement;
    }

    public void StartEdgePlacement(int ID)
    {
        StopPlacement();
        selectedObjectIndex = edgeDatabase.edgeData.FindIndex(data => data.ID == ID);
        
        if (selectedObjectIndex < 0){
            Debug.LogError($"No ID found {ID}");
            return;
        }

        gridVisualization.SetActive(true);

        inputManager.OnMouseRelease += PlaceEdge;
        inputManager.OnExit += StopPlacement;
    }

    private void PlaceStructure()
    {
        if (inputManager.IsPointerOverUI())
        {
            return;
        }
        Vector3 mousePosition = inputManager.GetSelectedMapPosition();
        Vector3Int gridPosition = grid.WorldToCell(mousePosition);

        bool placementValidity = CheckPlacementValidity(gridPosition, selectedObjectIndex);
        if (!placementValidity)
            return;
        
        int index = objectPlacer.PlaceObject(objectDatabase.objectsData[selectedObjectIndex].prefab, grid.CellToWorld(gridPosition));

        GridData selectedData = GetSelectedData(selectedObjectIndex);

        selectedData.AddObjectAt(gridPosition, objectDatabase.objectsData[selectedObjectIndex].positionsFilled, objectDatabase.objectsData[selectedObjectIndex].ID, index);
        
    }

    private void PlaceEdge()
    {
        
    }

    private bool CheckPlacementValidity(Vector3Int gridPosition, int selectedObjectIndex)
    {
        GridData selectedData = GetSelectedData(selectedObjectIndex);
        
        return selectedData.CanPlaceObjectAt(gridPosition, objectDatabase.objectsData[selectedObjectIndex].positionsFilled);
        
    }

    private GridData GetSelectedData(int selectedObjectIndex)
    {
        GridData selectedData = floorData;
        if (objectDatabase.objectsData[selectedObjectIndex].buildType == ObjectBuildType.Furniture)
            selectedData = furnitureData;
        else if (objectDatabase.objectsData[selectedObjectIndex].buildType == ObjectBuildType.Ceiling)
            selectedData = ceilingData;
        return selectedData;
    }

    private void StopPlacement()
    {
        selectedObjectIndex = -1;
        gridVisualization.SetActive(false);
        preview.StopShowingPreview();
        inputManager.OnMouseRelease -= PlaceEdge;
        inputManager.OnMouseRelease -= PlaceStructure;
        inputManager.OnExit -= StopPlacement;
        lastDetectedPosition = Vector3Int.zero;
    }

    private void Update()
    {
        if (selectedObjectIndex < 0)
            return;

        Vector3 mousePosition = inputManager.GetSelectedMapPosition();
        Vector3Int gridPosition = grid.WorldToCell(mousePosition);

        if (lastDetectedPosition != gridPosition)
            preview.UpdatePosition(grid.CellToWorld(gridPosition));
    }

}

