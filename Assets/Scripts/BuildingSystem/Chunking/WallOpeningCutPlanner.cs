using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Computes, for each wall tile an opening's footprint touches, the local-space rectangle
/// (along the wall's own X/length and Y/height axes) that should be cut out of that tile's wall
/// geometry.
///
/// COORDINATE MODEL: wall prefabs are authored with local X = offset along the wall's run
/// direction (see ObjectPlacer's ROTATION FIX note - "a 1-tile wall sits at local x = 0.5, a
/// 2-tile wall at local x = 1.0"), i.e. a single wall tile's solid volume occupies
/// approximately local X [0,1]. Opening prefabs are placed with the identical rotation/pivot
/// math (ObjectPlacer.PlaceEdge is shared by walls and openings alike), so a wall opening's
/// own collider bounds live in that same run-space X axis: a 1-tile door's collider sits in
/// local X [0,1], a 2-tile french door's spans local X [0,2]. This planner works entirely in
/// that shared run-space X axis, then re-expresses each wall tile's contribution relative to
/// that tile's own local origin - which is what WallSegmentCutCache actually needs, since it
/// operates on one wall tile's ChunkEntry at a time.
///
/// NOT CLAMPED TO [0,1] - CORNER OVERLAP: a tile's own mesh commonly extends a little past its
/// nominal [0,1] domain on purpose, overlapping into the NEXT tile's space to avoid a visible
/// seam at corners (see WallSegmentCutCache's class doc). This planner deliberately does NOT
/// clamp the range it hands back to [0,1] - only re-bases it to the tile's own origin - so a
/// wide opening spanning multiple tiles can still reach and cut that overlap sliver on the tile
/// it actually belongs to. An earlier version DID clamp here, which silently excluded that
/// overlap region from ever being cut (the range was capped at exactly 1.0 relative, regardless
/// of how much further the opening's true footprint extended) - the tile-boundary participation
/// check below is a separate, non-clamping test, so this omission never happens again.
/// WallSegmentCutCache's per-part clipping (against each mesh part's own true bounds) is what
/// makes an out-of-[0,1] range safe to hand it - it simply clips further to whatever that part's
/// real geometry covers.
///
/// HEIGHT (Y) is assumed uniform across every tile the opening touches - true for every
/// realistic door/window shape and far simpler than per-tile height variation, which nothing
/// in this system's art pipeline would ever produce anyway.
/// </summary>
public static class WallOpeningCutPlanner
{
    public readonly struct TileCut
    {
        /// <summary>Offset from the opening's base tile, along the run - matches one entry in positionsFilled.</summary>
        public readonly int tileOffset;

        /// <summary>
        /// This tile's local, origin-relative cut range on X. NOT clamped to [0,1] - may extend
        /// negative or past 1 when the opening's true footprint reaches into this tile's own
        /// corner-overlap geometry (see class doc). WallSegmentCutCache clips this further,
        /// per mesh part, against that part's own true bounds.
        /// </summary>
        public readonly Vector2 localXRange;

        /// <summary>Cut range on Y (height), shared across every tile in the plan.</summary>
        public readonly Vector2 localYRange;

        public TileCut(int tileOffset, Vector2 localXRange, Vector2 localYRange)
        {
            this.tileOffset = tileOffset;
            this.localXRange = localXRange;
            this.localYRange = localYRange;
        }
    }

    /// <summary>
    /// Builds one TileCut per integer offset in positionsFilled whose tile the opening's
    /// collider bounds actually reach. An offset the bounds don't reach (e.g. a footprint tile
    /// reserved for validity/linking purposes only, with no visible cutout there) is simply
    /// omitted from the result - the caller should leave that tile's wall geometry untouched.
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

            // Participation test only - does the opening's true footprint reach this tile's
            // canonical domain at all? Deliberately NOT used to clamp the range handed back
            // below (see class doc "NOT CLAMPED TO [0,1]") - a tile whose corner-overlap mesh
            // pokes into the NEXT tile still needs to see the opening's real extent to cut that
            // overlap correctly, not just the portion within its own nominal [0,1].
            if (openingBounds.max.x <= tileMinX || openingBounds.min.x >= tileMaxX)
                continue; // opening's footprint doesn't actually reach this tile - leave it whole.

            // Re-base to this tile's own local origin WITHOUT clamping - can legitimately go
            // negative or past 1, which is exactly what lets a wide opening reach a tile's own
            // corner-overlap geometry (see class doc).
            Vector2 localX = new Vector2(openingBounds.min.x - tileMinX, openingBounds.max.x - tileMinX);

            plan.Add(new TileCut(offset, localX, yRange));
        }

        return plan;
    }
}