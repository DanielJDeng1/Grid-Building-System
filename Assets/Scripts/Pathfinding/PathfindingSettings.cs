using UnityEngine;

[CreateAssetMenu(fileName = "PathfindingSettings", menuName = "Scriptable Objects/PathfindingSettings")]
public class PathfindingSettings : ScriptableObject
{
    [Header("Chunking")]
    [Tooltip("NavGrid chunk width/depth in cells. Larger chunks mean fewer allocations but a more expensive flood-fill per dirty rebuild.")]
    [SerializeField] private int _chunkSize = 32;

    [Header("Tier 1 - fast search (the common case)")]
    [Tooltip("Heuristic multiplier. >1 trades path optimality for a smaller search - the default favors speed per the project's stated priority.")]
    [SerializeField] private float _heuristicWeight = 1.3f;
    [Tooltip("Hard cap on node expansions for the fast attempt. If exceeded, Tier 2 only runs if the region graph confirms a path exists at all.")]
    [SerializeField] private int _tier1ExpansionBudget = 2000;

    [Header("Tier 2 - guaranteed-completion fallback (rare)")]
    [Tooltip("Heuristic multiplier for the fallback pass, relaxed toward 1 for a more complete (still not perfectly optimal unless 1.0) search.")]
    [SerializeField] private float _tier2HeuristicWeight = 1.0f;
    [Tooltip("Expansion cap for Tier 2. Much larger than Tier 1 - only reached for the rare request Tier 1 wasn't enough for.")]
    [SerializeField] private int _tier2ExpansionBudget = 50000;

    [Header("Natural movement")]
    [Tooltip("Max multiplicative jitter applied to edge cost, seeded per-agent so it stays stable across replans. 0.1 = up to 10% cost variation.")]
    [SerializeField] private float _jitterRange = 0.1f;

    public int ChunkSize => _chunkSize;
    public float HeuristicWeight => _heuristicWeight;
    public int Tier1ExpansionBudget => _tier1ExpansionBudget;
    public float Tier2HeuristicWeight => _tier2HeuristicWeight;
    public int Tier2ExpansionBudget => _tier2ExpansionBudget;
    public float JitterRange => _jitterRange;
}
