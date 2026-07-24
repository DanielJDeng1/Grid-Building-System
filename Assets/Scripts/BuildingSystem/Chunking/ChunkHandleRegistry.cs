using System.Collections.Generic;

/// <summary>
/// Implemented by anything that hands out chunked-entry handles (FloorChunkManager,
/// WallChunkManager, ...) so ChunkHandleRegistry can route a removal call back to the
/// correct owner.
/// </summary>
public interface IChunkOwner
{
    void RemoveEntry(int handle);
}

/// <summary>
/// Central allocator for chunked-entry handles, shared by every chunking system.
///
/// WHY THIS EXISTS:
/// ObjectPlacer's own instantiated-object free list only ever produces non-negative indices,
/// so originally "negative handle = chunked" was enough to route removal correctly with a
/// single chunking system. Now that Floor grid objects and Wall objects are chunked by two
/// completely independent managers (spatial buckets vs. contiguous runs), sign alone can no
/// longer say WHICH manager owns a given handle. This registry hands out globally-unique
/// negative handles and remembers the owner, so removal is a single dispatch call no matter
/// how many chunked categories exist - useful if another category (e.g. fences) is chunked
/// independently later.
/// </summary>
public static class ChunkHandleRegistry
{
    // Starts at -2, NOT -1: -1 is the standard "not found" sentinel used throughout this
    // codebase (e.g. GridData's GetEdgeRepresentationIndex/GetRepresentationIndex return -1
    // when a position isn't occupied, and EdgeState/GridState check `existingIndex != -1`).
    // If the very first handle ever issued were -1, that one entry would be indistinguishable
    // from "nothing here" to any code doing that check - it would place correctly, render
    // correctly, but never be removable, since removal logic would always read back -1 and
    // conclude there was nothing to remove.
    private static int _nextHandle = -2;
    private static readonly Dictionary<int, IChunkOwner> _owners = new();

    /// <summary>
    /// Allocates a new globally-unique negative handle for the given owner.
    /// Call this from within AddEntry, once the entry has been placed into the owner's
    /// own bookkeeping.
    /// </summary>
    public static int Register(IChunkOwner owner)
    {
        int handle = _nextHandle--;
        _owners[handle] = owner;
        return handle;
    }

    /// <summary>
    /// Routes a removal call to whichever owner registered this handle. No-op if the handle
    /// is unknown (already removed, or never existed).
    /// </summary>
    public static void Remove(int handle)
    {
        if (_owners.TryGetValue(handle, out IChunkOwner owner))
        {
            owner.RemoveEntry(handle);
            _owners.Remove(handle);
        }
    }

    /// <summary>True for any handle allocated by this registry (chunked entries are always negative).</summary>
    public static bool IsChunkedHandle(int handle) => handle < 0;

    /// <summary>
    /// Clears all registered handles without notifying owners. Intended for scene-transition
    /// cleanup if chunk managers are torn down/recreated between scenes; call BEFORE the old
    /// managers are destroyed if you need per-owner cleanup instead.
    /// </summary>
    public static void ClearAll()
    {
        _owners.Clear();
    }
}