# Grid Building System

A runtime building and pathfinding framework built in Unity and C#.

The project supports grid-based construction across multiple vertical levels, including floors, furniture, walls, wall openings, stairs, and other traversal links. Placement, navigation, rendering, and save/load are handled as separate systems rather than being concentrated in one placement script.

## Building System

Placement uses separate `IBuildingState` implementations for different construction modes.

Current states include:

- grid-aligned placement for floors, furniture, and ceilings
- edge placement for walls
- wall-opening placement for doors and similar openings
- multi-floor traversal placement for stairs and elevators
- corresponding removal states

`PlacementSystem` switches between these states and coordinates previews, validation, placement, and removal.

## Pathfinding

Navigation uses a custom A* implementation over the building grid.

The open set uses a binary min-heap with lazy deletion. Path requests are queued through `PathRequestManager`, which limits the number of searches processed per frame to avoid large frame-time spikes.

Multi-floor traversal objects register direct navigation links between levels. The pathfinder also distinguishes unreachable targets from searches that need a larger search budget.

Key files:

- `Assets/Scripts/Pathfinding/Pathfinding/AStarPathfinder.cs`
- `Assets/Scripts/Pathfinding/Pathfinding/BinaryHeap.cs`
- `Assets/Scripts/Pathfinding/Pathfinding/PathRequestManager.cs`
- `Assets/Scripts/Pathfinding/NavGrid.cs`

## Runtime Meshes

Placed floor and wall geometry is combined into runtime mesh chunks.

`FloorChunkManager` groups floor geometry by spatial chunk and build level. `WallChunkManager` combines compatible wall segments while limiting the size of long runs so local edits do not require rebuilding an entire facility wall.

Wall openings remain linked to their host wall geometry so deleting or changing a wall can restore the correct mesh.

## Save / Load

The save system serializes the active building state, including:

- grid-aligned objects
- wall and edge objects
- wall openings
- traversal objects

`SaveLoadManager` writes and restores `BuildingSaveData`, while `PlacementSystem` rebuilds the corresponding runtime state and navigation links.

## Project Layout

```text
Assets/Scripts/
├── BuildingSystem/
│   ├── BuildingStates/
│   ├── Chunking/
│   ├── ObjectPlacer.cs
│   └── PlacementSystem.cs
├── Pathfinding/
└── SaveSystem/
```

## Running the Project

The repository currently uses Unity `6000.3.16f1`.

Clone the repository and open it through Unity Hub:

```bash
git clone https://github.com/DanielJDeng1/Grid-Building-System.git
```

Open `Assets/Scenes/SampleScene.unity` and enter Play mode.
