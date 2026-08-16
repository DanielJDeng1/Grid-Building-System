using UnityEngine;

/// <summary>
/// Configuration settings for two-tier A* pathfinding, region graph fallback budgets, and edge cost jitter.
/// </summary>
[CreateAssetMenu(fileName = "PathfindingSettings", menuName = "Scriptable Objects/PathfindingSettings")]
public class PathfindingSettings : ScriptableObject
{
    [Header("Chunking")]
    [Tooltip("NavGrid chunk width/depth in cells.")]
    [SerializeField] private int _chunkSize = 32;

    [Header("Tier 1 - Fast Search")]
    [Tooltip("Heuristic multiplier for the primary search pass.")]
    [SerializeField] private float _heuristicWeight = 1.3f;
    [Tooltip("Maximum node expansions allowed for Tier 1 before falling back.")]
    [SerializeField] private int _tier1ExpansionBudget = 2000;

    [Header("Tier 2 - Fallback Search")]
    [Tooltip("Heuristic multiplier for the fallback search pass.")]
    [SerializeField] private float _tier2HeuristicWeight = 1.0f;
    [Tooltip("Maximum node expansions allowed for Tier 2 search.")]
    [SerializeField] private int _tier2ExpansionBudget = 50000;

    [Header("Natural Movement")]
    [Tooltip("Max multiplicative jitter applied to edge cost for agent path variation.")]
    [SerializeField] private float _jitterRange = 0.1f;

    public int ChunkSize => _chunkSize;
    public float HeuristicWeight => _heuristicWeight;
    public int Tier1ExpansionBudget => _tier1ExpansionBudget;
    public float Tier2HeuristicWeight => _tier2HeuristicWeight;
    public int Tier2ExpansionBudget => _tier2ExpansionBudget;
    public float JitterRange => _jitterRange;
}