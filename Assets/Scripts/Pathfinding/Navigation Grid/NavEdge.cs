using System;
using UnityEngine;

/// <summary>
/// The navigation system's own edge type - deliberately NOT a reference to
/// GridData.Edge, per the decoupling decision recorded in the design doc
/// (§13, decision 3). A few lines of duplication versus a genuine
/// compile-time boundary between the two systems.
/// 
/// Same bidirectional-equality technique as GridData.Edge: (A,B) and (B,A)
/// represent the same physical edge, and hash the same way regardless of
/// endpoint order.
/// </summary>
public readonly struct NavEdge : IEquatable<NavEdge>
{
    public readonly Vector3Int A;
    public readonly Vector3Int B;

    public NavEdge(Vector3Int a, Vector3Int b)
    {
        A = a;
        B = b;
    }

    public bool Equals(NavEdge other) =>
        (A == other.A && B == other.B) || (A == other.B && B == other.A);

    public override bool Equals(object obj) => obj is NavEdge other && Equals(other);

    public override int GetHashCode()
    {
        int hashA = A.GetHashCode();
        int hashB = B.GetHashCode();
        return hashA < hashB ? HashCode.Combine(hashA, hashB) : HashCode.Combine(hashB, hashA);
    }

    public override string ToString() => $"NavEdge({A} <-> {B})";

    public static bool operator ==(NavEdge x, NavEdge y) => x.Equals(y);
    public static bool operator !=(NavEdge x, NavEdge y) => !x.Equals(y);
}
