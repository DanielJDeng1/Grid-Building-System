/// <summary>
/// One fixed-size chunk of NavGrid storage. Lazily created by NavFloor on
/// first registration inside its bounds - an absent chunk means "unbuilt",
/// which is the same thing as "unwalkable" given floor presence is required
/// for walkability anyway.
/// 
/// Per-cell data is packed into a single byte: one walkable bit, four
/// cardinal edge-blocked bits. Diagonal-move legality (§8 corner-cutting) is
/// derived from these same four bits at query time - no separate storage.
/// 
/// _localRegionId is a parallel array used by NavRegionGraph for intra-chunk
/// connectivity (flood-fill result) - kept alongside the flags rather than
/// in a separate dictionary for locality, since both are indexed identically.
/// </summary>
public class NavChunk
{
    public const byte FlagWalkable = 1 << 0;
    public const byte FlagEdgeBlockedNorth = 1 << 1; // +Z
    public const byte FlagEdgeBlockedSouth = 1 << 2; // -Z
    public const byte FlagEdgeBlockedEast = 1 << 3;  // +X
    public const byte FlagEdgeBlockedWest = 1 << 4;  // -X

    public const int NoRegion = -1;

    public readonly int Size;

    private readonly byte[] _cellFlags;
    private readonly int[] _localRegionId;

    public NavChunk(int size)
    {
        Size = size;
        _cellFlags = new byte[size * size];
        _localRegionId = new int[size * size];
        for (int i = 0; i < _localRegionId.Length; i++)
            _localRegionId[i] = NoRegion;
    }

    private int Index(int localX, int localZ) => localX + localZ * Size;

    public byte GetFlags(int localX, int localZ) => _cellFlags[Index(localX, localZ)];
    public void SetFlags(int localX, int localZ, byte flags) => _cellFlags[Index(localX, localZ)] = flags;

    public bool IsWalkable(int localX, int localZ) =>
        (GetFlags(localX, localZ) & FlagWalkable) != 0;

    public void SetWalkable(int localX, int localZ, bool walkable)
    {
        byte flags = GetFlags(localX, localZ);
        flags = walkable ? (byte)(flags | FlagWalkable) : (byte)(flags & ~FlagWalkable);
        SetFlags(localX, localZ, flags);
    }

    public bool IsCardinalEdgeBlocked(int localX, int localZ, byte directionFlag) =>
        (GetFlags(localX, localZ) & directionFlag) != 0;

    public void SetCardinalEdgeBlocked(int localX, int localZ, byte directionFlag, bool blocked)
    {
        byte flags = GetFlags(localX, localZ);
        flags = blocked ? (byte)(flags | directionFlag) : (byte)(flags & ~directionFlag);
        SetFlags(localX, localZ, flags);
    }

    public int GetLocalRegionId(int localX, int localZ) => _localRegionId[Index(localX, localZ)];
    public void SetLocalRegionId(int localX, int localZ, int regionId) => _localRegionId[Index(localX, localZ)] = regionId;

    public void ClearAllRegionIds()
    {
        for (int i = 0; i < _localRegionId.Length; i++)
            _localRegionId[i] = NoRegion;
    }
}
