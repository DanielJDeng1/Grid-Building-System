using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Groups contiguous, same-orientation wall tiles into 1D runs for mesh chunking
/// Enables dynamic wall hiding by treating linear room boundaries as unified visual segments
/// Evaluates adjacency strictly by position and orientation rather than spatial bucketing or prefab type
/// </summary>
public class WallChunkManager : MonoBehaviour, IChunkOwner
{
    [Header("Chunking")]
    [Tooltip("Forces chunk boundaries on long walls to prevent excessive mesh rebuilds during local edits")]
    [SerializeField] private int _maxRunLength = 32;

    [Tooltip("Transform parent for generated chunk objects")]
    [SerializeField] private Transform _chunkParent;

    [Tooltip("Generates a single BoxCollider per contiguous linear run")]
    [SerializeField] private bool _generateColliders = true;

    private readonly struct RunKey : IEquatable<RunKey>
    {
        public readonly EdgeRotation rotation;
        public readonly int lockedCoordinate;
        public readonly int height;

        public RunKey(EdgeRotation rotation, int lockedCoordinate, int height)
        {
            this.rotation = rotation;
            this.lockedCoordinate = lockedCoordinate;
            this.height = height;
        }

        public bool Equals(RunKey other) =>
            rotation == other.rotation && lockedCoordinate == other.lockedCoordinate && height == other.height;

        public override bool Equals(object obj) => obj is RunKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(rotation, lockedCoordinate, height);
    }

    private class WallRun
    {
        public RunKey key;
        public Chunk meshChunk;

        // Decouples spatial adjacency lookups from mesh chunk data for fast split/merge evaluations
        public readonly SortedDictionary<int, int> coordinateToHandle = new();
    }

    private int _nextRunId = 0;
    private readonly Dictionary<int, WallRun> _runs = new();
    private readonly Dictionary<(RunKey key, int coordinate), int> _positionToRunId = new();
    private readonly Dictionary<int, (RunKey key, int coordinate)> _handleToPosition = new();
    private readonly HashSet<int> _dirtyRuns = new();

    private readonly Dictionary<int, int> _openingHandleToRunId = new();

    #region Public API

    /// <summary>
    /// Registers a wall placement and resolves run merging based on adjacent tiles
    /// </summary>
    public int AddEntry(GameObject prefab, Vector3 position, EdgeRotation rotation)
    {
        Vector3Int tile = Vector3Int.RoundToInt(position);

        RunKey key = rotation == EdgeRotation.Deg0
            ? new RunKey(EdgeRotation.Deg0, tile.z, tile.y)
            : new RunKey(EdgeRotation.Deg90, tile.x, tile.y);

        int coordinate = rotation == EdgeRotation.Deg0 ? tile.x : tile.z;

        bool leftOccupied = _positionToRunId.TryGetValue((key, coordinate - 1), out int leftRunId);
        bool rightOccupied = _positionToRunId.TryGetValue((key, coordinate + 1), out int rightRunId);

        int targetRunId = ResolveTargetRun(key, leftOccupied, leftRunId, rightOccupied, rightRunId);

        Matrix4x4 worldMatrix = ChunkRotationMath.GetEdgeObjectMatrix(position, rotation);
        int handle = ChunkHandleRegistry.Register(this);

        WallRun run = _runs[targetRunId];
        run.coordinateToHandle[coordinate] = handle;
        run.meshChunk.AddEntry(handle, new ChunkEntry(prefab, worldMatrix));

        _positionToRunId[(key, coordinate)] = targetRunId;
        _handleToPosition[handle] = (key, coordinate);

        MarkDirty(targetRunId);
        return handle;
    }

    /// <summary>
    /// Invoked via ChunkHandleRegistry to shrink or split a run upon tile removal
    /// </summary>
    public void RemoveEntry(int handle)
    {

         if (_openingHandleToRunId.TryGetValue(handle, out int openingRunId))
        {
            if (_runs.TryGetValue(openingRunId, out WallRun openingRun))
            {
                openingRun.meshChunk.RemoveEntry(handle);
                MarkDirty(openingRunId);
            }
            _openingHandleToRunId.Remove(handle);
            return;
        }
        
        if (!_handleToPosition.TryGetValue(handle, out (RunKey key, int coordinate) location))
            return;

        _handleToPosition.Remove(handle);

        if (!_positionToRunId.TryGetValue((location.key, location.coordinate), out int runId))
            return;

        _positionToRunId.Remove((location.key, location.coordinate));

        if (!_runs.TryGetValue(runId, out WallRun run))
            return;

        run.coordinateToHandle.Remove(location.coordinate);
        run.meshChunk.RemoveEntry(handle);
        MarkDirty(runId);

        if (run.coordinateToHandle.Count == 0)
        {
            run.meshChunk.DestroySelf();
            _runs.Remove(runId);
            _dirtyRuns.Remove(runId);
            return;
        }

        bool leftStillThere = run.coordinateToHandle.ContainsKey(location.coordinate - 1);
        bool rightStillThere = run.coordinateToHandle.ContainsKey(location.coordinate + 1);

        // Interior removal requires splitting the run
        if (leftStillThere && rightStillThere)
        {
            SplitRun(runId, location.coordinate);
        }
    }

    #endregion

    #region Add-Time Resolution (extend / merge / cap)

    private int ResolveTargetRun(RunKey key, bool leftOccupied, int leftRunId, bool rightOccupied, int rightRunId)
    {
        if (!leftOccupied && !rightOccupied)
            return CreateRun(key);

        if (leftOccupied && !rightOccupied)
            return HasRoom(leftRunId) ? leftRunId : CreateRun(key);

        if (!leftOccupied && rightOccupied)
            return HasRoom(rightRunId) ? rightRunId : CreateRun(key);

        // Contiguity invariant guarantees occupied neighbors belong to separate runs
        return BridgeTwoRuns(leftRunId, rightRunId, key);
    }

    /// <summary>
    /// In-place prefab swap for wall openings and procedural cuts without rebuilding run topology
    /// </summary>
    public bool TrySetTilePrefab(EdgeRotation rotation, Vector3Int tile, GameObject prefab)
    {
        RunKey key = rotation == EdgeRotation.Deg0
            ? new RunKey(EdgeRotation.Deg0, tile.z, tile.y)
            : new RunKey(EdgeRotation.Deg90, tile.x, tile.y);

        int coordinate = rotation == EdgeRotation.Deg0 ? tile.x : tile.z;

        if (!_positionToRunId.TryGetValue((key, coordinate), out int runId))
            return false;

        WallRun run = _runs[runId];
        if (!run.coordinateToHandle.TryGetValue(coordinate, out int handle))
            return false;

        if (!run.meshChunk.TryGetEntry(handle, out ChunkEntry existing))
            return false;

        run.meshChunk.AddEntry(handle, new ChunkEntry(prefab, existing.worldMatrix));
        MarkDirty(runId);
        return true;
    }

    /// <summary>
    /// Injects an opening mesh into a wall run's chunk without polluting tile adjacency state
    /// </summary>
    public int AttachOpeningEntry(GameObject prefab, Vector3 worldPosition, EdgeRotation rotation, Vector3Int anchorTile)
    {
        RunKey key = rotation == EdgeRotation.Deg0
            ? new RunKey(EdgeRotation.Deg0, anchorTile.z, anchorTile.y)
            : new RunKey(EdgeRotation.Deg90, anchorTile.x, anchorTile.y);

        int coordinate = rotation == EdgeRotation.Deg0 ? anchorTile.x : anchorTile.z;

        if (!_positionToRunId.TryGetValue((key, coordinate), out int runId))
        {
            Debug.LogError("WallChunkManager: AttachOpeningEntry called with no wall run at anchorTile - caller must validate a wall exists first.");
            return 0;
        }

        Matrix4x4 worldMatrix = ChunkRotationMath.GetEdgeObjectMatrix(worldPosition, rotation);
        int handle = ChunkHandleRegistry.Register(this);

        _runs[runId].meshChunk.AddEntry(handle, new ChunkEntry(prefab, worldMatrix));
        _openingHandleToRunId[handle] = runId;

        MarkDirty(runId);
        return handle;
    }

    private bool HasRoom(int runId) => _runs[runId].coordinateToHandle.Count < _maxRunLength;

    private int BridgeTwoRuns(int leftRunId, int rightRunId, RunKey key)
    {
        WallRun leftRun = _runs[leftRunId];
        WallRun rightRun = _runs[rightRunId];

        int combinedSize = leftRun.coordinateToHandle.Count + rightRun.coordinateToHandle.Count + 1;

        if (combinedSize <= _maxRunLength)
            return MergeRuns(leftRunId, rightRunId);

        // Enforce length cap by extending one side and maintaining a chunk boundary
        if (HasRoom(leftRunId))
            return leftRunId;
        if (HasRoom(rightRunId))
            return rightRunId;

        // Isolate bridging tile into a new run to respect capacity limits
        return CreateRun(key);
    }

    private int MergeRuns(int survivingRunId, int absorbedRunId)
    {
        WallRun survivor = _runs[survivingRunId];
        WallRun absorbed = _runs[absorbedRunId];

        foreach (KeyValuePair<int, int> kvp in absorbed.coordinateToHandle)
        {
            int coord = kvp.Key;
            int movedHandle = kvp.Value;

            if (absorbed.meshChunk.TryGetEntry(movedHandle, out ChunkEntry entry))
                survivor.meshChunk.AddEntry(movedHandle, entry);

            survivor.coordinateToHandle[coord] = movedHandle;
            _positionToRunId[(survivor.key, coord)] = survivingRunId;
            _handleToPosition[movedHandle] = (survivor.key, coord);
        }

        absorbed.meshChunk.DestroySelf();
        _runs.Remove(absorbedRunId);
        _dirtyRuns.Remove(absorbedRunId);

        MarkDirty(survivingRunId);
        return survivingRunId;
    }

    #endregion

    #region Remove-Time Resolution (split)

    /// <summary>
    /// Migrates tiles right of the removal gap into a new chunk
    /// </summary>
    private void SplitRun(int runId, int gapCoordinate)
    {
        WallRun run = _runs[runId];

        var rightCoordinates = new List<int>();
        foreach (int coord in run.coordinateToHandle.Keys)
        {
            if (coord > gapCoordinate)
                rightCoordinates.Add(coord);
        }

        if (rightCoordinates.Count == 0)
            return;

        int newRunId = CreateRun(run.key);
        WallRun newRun = _runs[newRunId];

        foreach (int coord in rightCoordinates)
        {
            int movedHandle = run.coordinateToHandle[coord];

            if (run.meshChunk.TryGetEntry(movedHandle, out ChunkEntry entry))
            {
                run.meshChunk.RemoveEntry(movedHandle);
                newRun.meshChunk.AddEntry(movedHandle, entry);
            }

            run.coordinateToHandle.Remove(coord);
            newRun.coordinateToHandle[coord] = movedHandle;

            _positionToRunId[(run.key, coord)] = newRunId;
            _handleToPosition[movedHandle] = (newRun.key, coord);
        }

        MarkDirty(runId);
        MarkDirty(newRunId);
    }

    #endregion

    #region Run Bookkeeping

    private int CreateRun(RunKey key)
    {
        int id = _nextRunId++;
        Transform parent = _chunkParent != null ? _chunkParent : transform;
        string debugName = $"WallRun_{id}_{key.rotation}_{key.lockedCoordinate}_{key.height}";
        ColliderMode colliderMode = _generateColliders ? ColliderMode.AggregateBox : ColliderMode.None;

        var run = new WallRun
        {
            key = key,
            meshChunk = new Chunk(debugName, parent, colliderMode)
        };

        _runs[id] = run;
        return id;
    }

    private void MarkDirty(int runId)
    {
        _dirtyRuns.Add(runId);
    }

    #endregion

    #region Batched Rebuild

    private void LateUpdate()
    {
        if (_dirtyRuns.Count == 0)
            return;

        foreach (int runId in _dirtyRuns)
        {
            if (_runs.TryGetValue(runId, out WallRun run))
                run.meshChunk.Rebuild();
        }

        _dirtyRuns.Clear();
    }

    #endregion
}