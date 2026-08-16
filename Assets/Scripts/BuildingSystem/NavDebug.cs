using System.Diagnostics;
using UnityEngine;

/// <summary>
/// Conditional debug logger gated by NAV_DEBUG.
/// </summary>
public static class NavDebug
{
    [Conditional("NAV_DEBUG")]
    public static void Log(string message) => UnityEngine.Debug.Log(message);
}