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

    GridRotation currentRotation = GridRotation.Deg0;

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
        
        int index = objectPlacer.PlaceObject(database.objectsData[selectedObjectIndex].prefab, grid.CellToWorld(gridPosition), currentRotation);

        selectedData.AddObjectAt(gridPosition, database.objectsData[selectedObjectIndex].positionsFilled, database.objectsData[selectedObjectIndex].ID, index, currentRotation);
    }

    public void UpdateState(Vector3Int gridPosition)
    {
        previewSystem.UpdatePosition(grid.CellToWorld(gridPosition));
    }

    public void Rotate(Vector3Int gridPosition)
    {
        currentRotation = (GridRotation)(((int)currentRotation + 1) % 4);
        /*
        switch
        {
            GridRotation.Deg0 => 0f,
            GridRotation.Deg90 => 90f,
            GridRotation.Deg180 => 180f,
            GridRotation.Deg270 => 270f,
            _ => 0f
        });
        */
    }

    public void OnHold(Vector3Int mousePosition)
    {
        
    }

    private bool CheckPlacementValidity(Vector3Int gridPosition, int selectedObjectIndex)
    {
        return selectedData.CanPlaceObjectAt(gridPosition, database.objectsData[selectedObjectIndex].positionsFilled, currentRotation);
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

public enum GridRotation
{
    Deg0,
    Deg90,
    Deg180,
    Deg270
}