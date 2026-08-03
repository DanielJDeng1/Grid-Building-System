using System.Diagnostics;
using UnityEngine;

/// <summary>
/// Thin wrapper around Debug.Log gated by the NAV_DEBUG scripting define
/// symbol. Deliberately NOT an `if (enabled) Debug.Log(...)` check - that
/// still evaluates the interpolated string/string.Join argument every call,
/// which is the actual cost at agent-request or per-chunk-rebuild
/// frequency. [Conditional] instead makes the compiler omit the call site
/// entirely (including argument evaluation) whenever NAV_DEBUG isn't
/// defined, so this is zero-cost in normal builds while staying available
/// for the next time nav behavior needs to be traced.
/// 
/// To re-enable: Project Settings > Player > Scripting Define Symbols,
/// add NAV_DEBUG (per-platform, so it's easy to leave off in builds while
/// on in the editor if desired).
/// </summary>
public static class NavDebug
{
    [Conditional("NAV_DEBUG")]
    public static void Log(string message) => UnityEngine.Debug.Log(message);
}