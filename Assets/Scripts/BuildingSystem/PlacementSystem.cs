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

    [SerializeField] private GameObject gridVisualization;

    [SerializeField] private PreviewSystem preview;

    private GridData floorData, furnitureData, ceilingData;

    [SerializeField] private ObjectPlacer objectPlacer;

    private Vector3Int lastDetectedPosition = Vector3Int.zero;

    IBuildingState buildingState;

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
        gridVisualization.SetActive(true);

        buildingState = new GridState(ID, grid, preview, objectDatabase, objectPlacer, floorData, furnitureData, ceilingData);
        
        inputManager.OnMouseRelease += PlaceStructure;
        inputManager.OnExit += StopPlacement;
    }

    public void StartEdgePlacement(int ID)
    {

    }

    private void PlaceStructure()
    {
        if (inputManager.IsPointerOverUI())
        {
            return;
        }
        Vector3 mousePosition = inputManager.GetSelectedMapPosition();
        Vector3Int gridPosition = grid.WorldToCell(mousePosition);

        buildingState.OnAction(gridPosition);
        
    }

    private void PlaceEdge()
    {
        
    }

    private void StopPlacement()
    {
        if (buildingState == null)
            return;

        gridVisualization.SetActive(false);
        buildingState.EndState();
        inputManager.OnMouseRelease -= PlaceEdge;
        inputManager.OnMouseRelease -= PlaceStructure;
        inputManager.OnExit -= StopPlacement;
        lastDetectedPosition = Vector3Int.zero;

        buildingState = null;
    }

    private void Update()
    {
        if (buildingState == null)
            return;

        Vector3 mousePosition = inputManager.GetSelectedMapPosition();
        Vector3Int gridPosition = grid.WorldToCell(mousePosition);

        if (lastDetectedPosition != gridPosition)
            preview.UpdatePosition(grid.CellToWorld(gridPosition));
    }

}

