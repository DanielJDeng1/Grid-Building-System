using System;

/// <summary>
/// Identifies a single stateful registration in the nav obstacle contract -
/// currently used for NavLinks (stairs/elevators, Phase 2) and toggleable
/// obstacles like doors (Phase 4). Plain cell/edge obstacles coming through
/// GridData's occupancy events do NOT need one of these - see
/// INavObstacleChannel for why refcounting by key is sufficient there.
/// 
/// Deliberately a bare int wrapper, not a GUID: allocated via
/// INavObstacleChannel.AllocateId(), cheap to generate, cheap to compare,
/// and stable for the lifetime of whatever placed object holds it.
/// </summary>
public readonly struct NavObstacleId : IEquatable<NavObstacleId>
{
    public readonly int Value;

    public NavObstacleId(int value)
    {
        Value = value;
    }

    public static readonly NavObstacleId None = new NavObstacleId(-1);

    public bool IsValid => Value >= 0;

    public bool Equals(NavObstacleId other) => Value == other.Value;
    public override bool Equals(object obj) => obj is NavObstacleId other && Equals(other);
    public override int GetHashCode() => Value;
    public override string ToString() => $"NavObstacleId({Value})";

    public static bool operator ==(NavObstacleId a, NavObstacleId b) => a.Equals(b);
    public static bool operator !=(NavObstacleId a, NavObstacleId b) => !a.Equals(b);
}
