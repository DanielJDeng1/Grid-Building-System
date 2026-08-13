using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns the link between a placed wall opening (door/window) and the wall edge(s) it embeds
/// into. Three responsibilities:
///
/// 1. CASCADE DELETE: subscribes to GridData.OnEdgeOccupancyChanged on every layer a wall can
///    live on. When a linked wall edge is removed OR OVERRIDDEN (EdgeState's
///    ClearEdgesInFootprint fires the same false-then-true occupancy sequence a plain removal
///    does), the opening it hosted is removed too. This is exactly the extension point
///    GridData's own doc comment describes - "any future consumer... can subscribe to the same
///    events without GridData needing to change at all" - the nav bridge is the other existing
///    consumer of this same event, and neither knows the other exists.
///
/// 2. OCCUPANCY QUERY: lets WallOpeningState reject placing a second opening on a wall tile
///    that already has one, and lets WallOpeningRemovalState find what a click should remove.
///
/// 3. INTEGRATION HOOK: fires OnOpeningRegistered/OnOpeningRemoved so a future consumer (the
///    nav/pathfinding bridge for doors) can react to opening lifecycle without this service
///    ever needing a reference to it - same pattern as GridData's own occupancy events.
///
/// RESTORING THE HOST WALL'S CUT:
/// Every removal path - explicit (RemoveOpening) and cascade (HandleEdgeOccupancyChanged) -
/// restores each linked tile's original (uncut) prefab via WallChunkManager.TrySetTilePrefab.
/// This is NOT redundant for the tile whose own removal triggered the cascade: for a
/// MULTI-TILE opening, a cascade can be triggered by just ONE of its host tiles being removed
/// or overridden while the others stay standing (each wall tile is placed/removed
/// independently in GridData - see EdgeState). Unlink() removes the ONLY record pointing back
/// to every one of this opening's tiles, so any tile we don't restore here becomes permanently
/// stuck showing cut geometry with no way to undo it. For the specific tile that IS actually
/// being removed, restoring it first is a harmless no-op - WallChunkManager.RemoveEntry tears
/// its ChunkEntry down completely moments after this call returns (see EdgeState/
/// EdgeRemovalState's call order), so this just costs one redundant MarkDirty on a run that's
/// about to be rebuilt anyway.
/// </summary>
public class WallOpeningLinkService
{
    private readonly WallChunkManager _wallChunkManager;
    private readonly ObjectPlacer _objectPlacer;
    private readonly GridData[] _subscribedLayers;

    private readonly Dictionary<Edge, OpeningRecord> _byWallEdge = new();

    /// <summary>
    /// Fired after an opening is fully registered - handle plus every wall edge it's linked to.
    /// No subscribers today; reserved for the future door/nav bridge (see class doc, point 3).
    /// </summary>
    public event Action<int, List<Edge>> OnOpeningRegistered;

    /// <summary>
    /// Fired right before an opening's link is torn down, for either removal path (explicit
    /// click-to-remove, or cascade from its host wall going away). No subscribers today;
    /// reserved for the future door/nav bridge (see class doc, point 3).
    /// </summary>
    public event Action<int, List<Edge>> OnOpeningRemoved;

    public WallOpeningLinkService(WallChunkManager wallChunkManager, ObjectPlacer objectPlacer, params GridData[] wallHostLayers)
    {
        _wallChunkManager = wallChunkManager;
        _objectPlacer = objectPlacer;
        _subscribedLayers = wallHostLayers;

        foreach (GridData layer in _subscribedLayers)
        {
            if (layer != null)
                layer.OnEdgeOccupancyChanged += HandleEdgeOccupancyChanged;
        }
    }

    /// <summary>
    /// Call when this service's owner (e.g. PlacementSystem) is destroyed, to avoid leaking
    /// subscriptions onto GridData instances that may outlive it.
    /// </summary>
    public void Dispose()
    {
        foreach (GridData layer in _subscribedLayers)
        {
            if (layer != null)
                layer.OnEdgeOccupancyChanged -= HandleEdgeOccupancyChanged;
        }
    }

    /// <summary>True if the given wall edge already hosts an opening.</summary>
    public bool HasOpeningAt(Edge wallEdge) => _byWallEdge.ContainsKey(wallEdge);

    /// <summary>
    /// Registers a newly-placed opening's link to the wall edges/tiles it embeds into. Call
    /// once, immediately after placing the opening via ObjectPlacer/WallChunkManager.
    /// </summary>
    public void Register(int openingHandle, List<Edge> wallEdges, List<(EdgeRotation rotation, Vector3Int tile, GameObject originalWallPrefab)> wallTiles)
    {
        var record = new OpeningRecord(openingHandle, wallEdges, wallTiles);

        foreach (Edge edge in wallEdges)
            _byWallEdge[edge] = record;

        OnOpeningRegistered?.Invoke(openingHandle, wallEdges);
    }

    /// <summary>
    /// Explicit removal - the user clicked the opening itself via WallOpeningRemovalState.
    /// Restores every affected wall tile's original (uncut) prefab, since the wall itself is
    /// staying. Accepts any one of the opening's linked edges (for a multi-tile opening, every
    /// linked edge resolves to the same record).
    /// </summary>
    public void RemoveOpening(Edge anyLinkedWallEdge)
    {
        if (!_byWallEdge.TryGetValue(anyLinkedWallEdge, out OpeningRecord record))
            return;

        RestoreHostTiles(record);

        _objectPlacer.RemoveEdgeAt(record.openingHandle);
        OnOpeningRemoved?.Invoke(record.openingHandle, record.wallEdges);
        Unlink(record);
    }

    private void HandleEdgeOccupancyChanged(Edge edge, bool occupied)
    {
        if (occupied)
            return; // placement never creates an opening by itself - only removal/override cascades.

        if (!_byWallEdge.TryGetValue(edge, out OpeningRecord record))
            return;

        // See class doc "RESTORING THE HOST WALL'S CUT" - this must run for every host tile,
        // not just the one that triggered this event, or a multi-tile opening's other tiles
        // are stranded with permanently cut geometry once Unlink() below removes the only
        // record that ever pointed back to them.
        RestoreHostTiles(record);

        _objectPlacer.RemoveEdgeAt(record.openingHandle);
        OnOpeningRemoved?.Invoke(record.openingHandle, record.wallEdges);
        Unlink(record); // removes ALL of this record's edges, so a multi-tile opening's second
                         // occupancy-changed event (if the wall removal spans several tiles at
                         // once) finds nothing left to do - see class doc.
    }

    private void RestoreHostTiles(OpeningRecord record)
    {
        foreach (var (rotation, tile, originalPrefab) in record.wallTiles)
        {
            _wallChunkManager.TrySetTilePrefab(rotation, tile, originalPrefab);
        }
    }

    private void Unlink(OpeningRecord record)
    {
        foreach (Edge edge in record.wallEdges)
            _byWallEdge.Remove(edge);
    }

    private class OpeningRecord
    {
        public readonly int openingHandle;
        public readonly List<Edge> wallEdges;
        public readonly List<(EdgeRotation rotation, Vector3Int tile, GameObject originalWallPrefab)> wallTiles;

        public OpeningRecord(int openingHandle, List<Edge> wallEdges, List<(EdgeRotation, Vector3Int, GameObject)> wallTiles)
        {
            this.openingHandle = openingHandle;
            this.wallEdges = wallEdges;
            this.wallTiles = wallTiles;
        }
    }
}