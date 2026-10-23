using System;
using UnityEngine;

namespace VRCAD.Core.CSG
{
    /// <summary>
    /// Centralized, scale-aware tolerance constants and utilities for the CSG Boolean engine.
    /// Provides consistent numerical thresholds across plane splitting, vertex welding,
    /// triangle degenerate checks, and topology validation.
    /// </summary>
    public static class CSGTolerance
    {
        /// <summary>
        /// Distance epsilon for geometric vertex coincidence (0.8 mm in standard Unity meter units).
        /// </summary>
        public const float DistanceEpsilon = 8e-4f;

        /// <summary>
        /// Distance epsilon squared for fast distance comparisons.
        /// </summary>
        public const float DistanceEpsilonSqr = DistanceEpsilon * DistanceEpsilon;

        /// <summary>
        /// Plane classification threshold for BSP splitting (determines Front, Back, or Coplanar).
        /// </summary>
        public const float PlaneEpsilon = 5e-5f;

        /// <summary>
        /// Minimum triangle area below which a polygon or triangle is classified as degenerate.
        /// </summary>
        public const float TriangleAreaEpsilon = 1e-8f;

        /// <summary>
        /// Threshold for normal dot product to treat two normals as identical (preserves sharp edges).
        /// Dot > 0.999 corresponds to an angular difference &lt; ~2.5 degrees.
        /// </summary>
        public const float NormalDotEpsilon = 0.999f;

        /// <summary>
        /// UV distance threshold for preserving UV texture seams during vertex welding.
        /// </summary>
        public const float UVEpsilon = 1e-3f;

        /// <summary>
        /// Overcut margin (in meters) for through-hole cutting cylinders to prevent coplanar end caps.
        /// </summary>
        public const float ThroughHoleOvercutMargin = 0.005f; // 5 mm

        /// <summary>
        /// Computes a scale-adjusted distance tolerance based on the object's characteristic dimension.
        /// </summary>
        public static float GetScaleAwareDistanceTolerance(Bounds bounds)
        {
            float maxDim = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            if (maxDim <= 0.001f) maxDim = 1f;
            return Mathf.Clamp(maxDim * DistanceEpsilon, DistanceEpsilon, 2e-3f);
        }
    }
}
