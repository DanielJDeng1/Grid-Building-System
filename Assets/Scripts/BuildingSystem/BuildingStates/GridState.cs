using UnityEngine;

public class GridState : IBuildingState
{

    private int selectedObjectIndex = -1;
    int ID;
    Grid grid;
    PreviewSystem previewSystem;
    ObjectDatabase database;
    GridData floorData, furnitureData, ceilingData;
    ObjectPlacer objectPlacer;

    GridData selectedData;

    public GridState(int ID, Grid grid, PreviewSystem previewSystem, ObjectDatabase database, ObjectPlacer objectPlacer, 
                    GridData floorData, GridData furnitureData, GridData ceilingData)
    {
        selectedObjectIndex = database.objectsData.FindIndex(data => data.ID == ID);
        if (selectedObjectIndex < 0)
        {
            throw new System.Exception($"No object with ID {ID}");
        }
        this.floorData = floorData;
        this.furnitureData = furnitureData;
        this.ceilingData = ceilingData;
        this.database = database;
        this.ID = ID;
        this.previewSystem = previewSystem;
        this.objectPlacer = objectPlacer;
        this.grid = grid;

        selectedData = GetSelectedData(selectedObjectIndex);

        previewSystem.StartShowingPlacementPreview(database.objectsData[selectedObjectIndex].prefab);
    }

    public void EndState()
    {
        previewSystem.StopShowingPreview();
    }

    public void OnAction(Vector3Int gridPosition)
    {
        bool placementValidity = CheckPlacementValidity(gridPosition, selectedObjectIndex);

        if (!placementValidity)
            return;
        
        int index = objectPlacer.PlaceObject(database.objectsData[selectedObjectIndex].prefab, grid.CellToWorld(gridPosition));

        selectedData.AddObjectAt(gridPosition, database.objectsData[selectedObjectIndex].positionsFilled, database.objectsData[selectedObjectIndex].ID, index);
    }

    public void UpdateState(Vector3Int gridPosition)
    {
        previewSystem.UpdatePosition(grid.CellToWorld(gridPosition));
    }

    public void Rotate(Vector3Int gridPosition)
    {
        
    }

    public void OnHold(Vector3Int mousePosition)
    {
        
    }

    private bool CheckPlacementValidity(Vector3Int gridPosition, int selectedObjectIndex)
    {
        
        return selectedData.CanPlaceObjectAt(gridPosition, database.objectsData[selectedObjectIndex].positionsFilled);
        
    }

    private GridData GetSelectedData(int selectedObjectIndex)
    {
        GridData selectedData = floorData;
        if (database.objectsData[selectedObjectIndex].buildType == ObjectBuildType.Furniture)
            selectedData = furnitureData;
        else if (database.objectsData[selectedObjectIndex].buildType == ObjectBuildType.Ceiling)
            selectedData = ceilingData;
        return selectedData;
    }

}
