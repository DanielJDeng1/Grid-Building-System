using System;

/// <summary>
/// Unique handle for stateful navigation registrations like dynamic links and doors.
/// Lightweight integer wrapper allocated via INavObstacleChannel to avoid GUID generation overhead.
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