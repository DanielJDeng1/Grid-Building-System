using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Computes local-space cutout regions for wall tiles spanned by wall openings (doors/windows).
/// Operates in shared run-space X coordinates and re-bases cut ranges relative to individual tile origins.
/// </summary>
public static class WallOpeningCutPlanner
{
    public readonly struct TileCut
    {
        /// <summary>Tile offset from opening base along the wall run (matches positionsFilled entry)</summary>
        public readonly int tileOffset;

        /// <summary>
        /// Tile-local, origin-relative X cut range. Unclamped to allow cutting across corner-overlap geometry.
        /// </summary>
        public readonly Vector2 localXRange;

        /// <summary>Uniform height cut range shared across all tiles in the plan</summary>
        public readonly Vector2 localYRange;

        public TileCut(int tileOffset, Vector2 localXRange, Vector2 localYRange)
        {
            this.tileOffset = tileOffset;
            this.localXRange = localXRange;
            this.localYRange = localYRange;
        }
    }

    /// <summary>
    /// Generates a TileCut plan for each grid offset whose domain intersects the opening's footprint bounds
    /// </summary>
    public static List<TileCut> BuildPlan(List<int> positionsFilled, PrefabColliderBounds openingBounds)
    {
        var plan = new List<TileCut>(positionsFilled.Count);

        if (!openingBounds.hasBounds)
            return plan;

        Vector2 yRange = new Vector2(openingBounds.min.y, openingBounds.max.y);

        foreach (int offset in positionsFilled)
        {
            float tileMinX = offset;
            float tileMaxX = offset + 1f;

            // Check if opening footprint reaches this tile's domain
            if (openingBounds.max.x <= tileMinX || openingBounds.min.x >= tileMaxX)
                continue; 

            // Re-base global opening span to tile-local origin without clamping
            Vector2 localX = new Vector2(openingBounds.min.x - tileMinX, openingBounds.max.x - tileMinX);

            plan.Add(new TileCut(offset, localX, yRange));
        }

        return plan;
    }
}