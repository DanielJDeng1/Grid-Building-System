using System.Collections.Generic;

/// <summary>
/// Implemented by managers that own chunked entries, enabling unified removal dispatch.
/// </summary>
public interface IChunkOwner
{
    void RemoveEntry(int handle);
}

/// <summary>
/// Allocates globally unique negative handles and routes removals across multiple chunk owners.
/// </summary>
public static class ChunkHandleRegistry
{
    // Starts at -2 to reserve -1 as the standard "not found" sentinel value.
    private static int _nextHandle = -2;
    private static readonly Dictionary<int, IChunkOwner> _owners = new();

    /// <summary>
    /// Allocates a unique negative handle for the specified owner.
    /// </summary>
    public static int Register(IChunkOwner owner)
    {
        int handle = _nextHandle--;
        _owners[handle] = owner;
        return handle;
    }

    /// <summary>
    /// Dispatches removal to the registered owner for the given handle.
    /// </summary>
    public static void Remove(int handle)
    {
        if (_owners.TryGetValue(handle, out IChunkOwner owner))
        {
            owner.RemoveEntry(handle);
            _owners.Remove(handle);
        }
    }

    /// <summary>
    /// Returns true if the handle was allocated by this registry.
    /// </summary>
    public static bool IsChunkedHandle(int handle) => handle < 0;

    /// <summary>
    /// Resets all registered handles without notifying owners (for scene teardown).
    /// </summary>
    public static void ClearAll()
    {
        _owners.Clear();
    }
}