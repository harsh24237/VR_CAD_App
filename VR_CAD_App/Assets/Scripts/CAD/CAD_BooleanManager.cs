using System;
using System.Collections.Generic;
using UnityEngine;
using VRCAD.Core.CSG;

namespace VRCAD.Core
{
    public enum BooleanOperation
    {
        Union,
        Subtract,   // Hole cutting / Difference
        Intersect
    }

    public enum BooleanResultStatus
    {
        Success,
        Empty,
        Unchanged,
        Failure
    }

    public struct BooleanResult
    {
        public BooleanResultStatus status;
        public Mesh resultMesh;
        public CSGValidationResult validation;
        public string message;
    }

    public enum HoleType
    {
        Blind,
        Through
    }

    public struct HoleOptions
    {
        public HoleType holeType;
        public float radius;
        public float depth;
        public int radialSegments;
        public float overcutMargin;

        public static HoleOptions Default => new HoleOptions
        {
            holeType = HoleType.Through,
            radius = 0.03f,
            depth = 0.25f,
            radialSegments = 24,
            overcutMargin = CSGTolerance.ThroughHoleOvercutMargin
        };
    }

    public class CAD_BooleanManager : MonoBehaviour
    {
        [Header("CSG Settings")]
        [SerializeField] private float tolerance = CSGTolerance.DistanceEpsilon;

        public float Tolerance => tolerance;

        /// <summary>
        /// Rich Boolean execution that returns explicit status, validation, and diagnostics without mutating object state.
        /// </summary>
        public BooleanResult ExecuteBoolean(CADObject targetObj, CADObject toolObj, BooleanOperation operation)
        {
            var res = new BooleanResult
            {
                status = BooleanResultStatus.Failure,
                resultMesh = null,
                validation = default,
                message = ""
            };

            if (targetObj == null || toolObj == null)
            {
                res.message = "Operands must not be null.";
                return res;
            }

            Mesh meshA = targetObj.MeshFilter.sharedMesh;
            Mesh meshB = toolObj.MeshFilter.sharedMesh;

            if (meshA == null || meshB == null)
            {
                res.message = "One or both operands have null meshes.";
                return res;
            }

            Transform transA = targetObj.transform;
            Transform transB = toolObj.transform;

            Matrix4x4 toolToTarget = transA.worldToLocalMatrix * transB.localToWorldMatrix;

            // Check bounding overlap
            Bounds boundsA = meshA.bounds;
            Bounds boundsB = TransformBounds(meshB.bounds, toolToTarget);
            bool boundsOverlap = boundsA.Intersects(boundsB);

            // 1. Disjoint cases
            if (!boundsOverlap)
            {
                if (operation == BooleanOperation.Subtract)
                {
                    // A - B = A (mathematically unchanged no-op)
                    res.status = BooleanResultStatus.Unchanged;
                    res.resultMesh = meshA;
                    res.message = "Subtract unchanged: shapes do not overlap. Cutter preserved.";
                    return res;
                }
                else if (operation == BooleanOperation.Intersect)
                {
                    // A ∩ B = ∅ (mathematically empty result)
                    res.status = BooleanResultStatus.Empty;
                    res.message = "Intersection produced empty geometry: shapes do not overlap.";
                    return res;
                }
                else if (operation == BooleanOperation.Union)
                {
                    // Disjoint Union: combine disconnected closed bodies into a single multi-body CAD mesh
                    Mesh combinedMesh = CombineDisjointMeshes(meshA, meshB, toolToTarget);
                    var val = CSGModel.ValidateMesh(combinedMesh);
                    if (val.isValid)
                    {
                        res.status = BooleanResultStatus.Success;
                        res.resultMesh = combinedMesh;
                        res.validation = val;
                        res.message = $"Union completed: merged disjoint bodies ({val.triangleCount} triangles).";
                        return res;
                    }
                }
            }

            // 2. CSG Model Evaluation
            CSGModel modelA = CSGModel.FromMesh(meshA, Matrix4x4.identity, 0);
            CSGModel modelB = CSGModel.FromMesh(meshB, toolToTarget, 1);

            CSGModel resultModel = operation switch
            {
                BooleanOperation.Union => CSGModel.Union(modelA, modelB),
                BooleanOperation.Subtract => CSGModel.Subtract(modelA, modelB),
                BooleanOperation.Intersect => CSGModel.Intersect(modelA, modelB),
                _ => null
            };

            // 3. Handle Empty Results
            if (resultModel == null || resultModel.polygons == null || resultModel.polygons.Count == 0)
            {
                if (operation == BooleanOperation.Subtract)
                {
                    // Cutter completely engulfed target: A - B = ∅
                    res.status = BooleanResultStatus.Empty;
                    res.message = "Subtract produced empty geometry: cutter completely contains target.";
                    return res;
                }
                else if (operation == BooleanOperation.Intersect)
                {
                    // Non-overlapping or surface-touching intersection: A ∩ B = ∅
                    res.status = BooleanResultStatus.Empty;
                    res.message = "Intersection produced empty geometry.";
                    return res;
                }
                else
                {
                    res.status = BooleanResultStatus.Failure;
                    res.message = "Boolean operation produced zero polygons.";
                    return res;
                }
            }

            // 4. Mesh Synthesis & Strict Validation
            Mesh candidateMesh = resultModel.ToMesh($"CSG_{operation}_Result");
            if (candidateMesh == null)
            {
                res.status = BooleanResultStatus.Failure;
                res.message = "Failed to triangulate CSG polygon geometry.";
                return res;
            }

            // Validate result mesh strictly for manifold closed geometry
            var validation = CSGModel.ValidateMesh(candidateMesh);
            res.validation = validation;

            if (!validation.isValid)
            {
                res.status = BooleanResultStatus.Failure;
                res.message = validation.message;
                Debug.LogWarning($"[CSG_EXEC] {operation} validation failed: {validation.message}");
                return res;
            }

            res.status = BooleanResultStatus.Success;
            res.resultMesh = candidateMesh;
            res.message = $"{operation} completed successfully ({validation.triangleCount} triangles).";
            return res;
        }

        /// <summary>
        /// Legacy Boolean execution method. Returns true if the operation succeeded or was applied.
        /// </summary>
        public bool PerformBoolean(CADObject targetObj, CADObject toolObj, BooleanOperation operation)
        {
            BooleanResult result = ExecuteBoolean(targetObj, toolObj, operation);

            switch (result.status)
            {
                case BooleanResultStatus.Success:
                    targetObj.SetMesh(result.resultMesh);
                    CADManagerHub.Instance?.OnMeshModified(targetObj);
                    toolObj.gameObject.SetActive(false);
                    CADManagerHub.Instance?.ShapeManager?.UnregisterObject(toolObj);
                    CADManagerHub.Instance?.EmitStatus(result.message);
                    return true;

                case BooleanResultStatus.Empty:
                    // Deactivate and unregister target to represent empty result safely
                    targetObj.gameObject.SetActive(false);
                    CADManagerHub.Instance?.ShapeManager?.UnregisterObject(targetObj);
                    toolObj.gameObject.SetActive(false);
                    CADManagerHub.Instance?.ShapeManager?.UnregisterObject(toolObj);
                    CADManagerHub.Instance?.EmitStatus(result.message);
                    return true;

                case BooleanResultStatus.Unchanged:
                    CADManagerHub.Instance?.EmitStatus(result.message);
                    return false;

                case BooleanResultStatus.Failure:
                default:
                    Debug.LogWarning($"[CAD_BooleanManager] Operation failed: {result.message}");
                    CADManagerHub.Instance?.EmitStatus($"Boolean failed: {result.message}");
                    return false;
            }
        }

        #region CAD Hole Cutting

        /// <summary>
        /// Advanced face-based hole cutting supporting blind holes, through-holes, scale overcuts, and arbitrary orientations.
        /// </summary>
        public BooleanResult CutHoleAdvanced(CADObject targetObj, Vector3 surfacePointWorld, Vector3 normalWorld, HoleOptions options)
        {
            var res = new BooleanResult
            {
                status = BooleanResultStatus.Failure,
                resultMesh = null,
                validation = default,
                message = ""
            };

            if (targetObj == null || targetObj.MeshFilter.sharedMesh == null)
            {
                res.message = "Target object or mesh is null.";
                return res;
            }

            // Parameter validation
            if (options.radius <= 0.0001f || float.IsNaN(options.radius) || float.IsInfinity(options.radius))
            {
                res.message = $"Invalid hole radius: {options.radius}";
                return res;
            }

            if (options.depth <= 0.0001f || float.IsNaN(options.depth) || float.IsInfinity(options.depth))
            {
                res.message = $"Invalid hole depth: {options.depth}";
                return res;
            }

            if (options.radialSegments < 3)
            {
                options.radialSegments = 24;
            }

            if (normalWorld.sqrMagnitude < 1e-6f || float.IsNaN(normalWorld.x))
            {
                res.message = "Invalid normal vector for hole cutting.";
                return res;
            }

            Vector3 outwardNormal = normalWorld.normalized;
            Vector3 cutDirection = -outwardNormal; // Cut into the solid

            float overcut = Mathf.Max(options.overcutMargin, 0.002f);
            float cylinderLength;
            Vector3 cylinderCenter;

            if (options.holeType == HoleType.Through)
            {
                // Compute target's extent along the cut axis to ensure complete penetration
                Mesh targetMesh = targetObj.MeshFilter.sharedMesh;
                float thickness = ComputeTargetThicknessAlongAxis(targetMesh, targetObj.transform, surfacePointWorld, cutDirection);
                if (thickness <= 0.001f)
                {
                    thickness = Mathf.Max(targetMesh.bounds.size.x, Mathf.Max(targetMesh.bounds.size.y, targetMesh.bounds.size.z));
                }

                cylinderLength = thickness + 2f * overcut;
                cylinderCenter = surfacePointWorld + cutDirection * (thickness * 0.5f);
            }
            else
            {
                // Blind hole: penetrate by exact requested depth
                cylinderLength = options.depth + overcut;
                cylinderCenter = surfacePointWorld + cutDirection * ((options.depth - overcut) * 0.5f);
            }

            // Construct cutting cylinder in world space
            Mesh holeCylinderMesh = CAD_ShapeManager.CreateCylinderMesh(options.radius, cylinderLength, options.radialSegments);
            GameObject tempCutter = new GameObject("Temp_HoleCutter");
            tempCutter.transform.position = cylinderCenter;
            Quaternion cutterRotation;
            if (Vector3.Dot(Vector3.up, cutDirection) < -0.9999f)
            {
                cutterRotation = Quaternion.AngleAxis(180f, Vector3.right);
            }
            else if (Vector3.Dot(Vector3.up, cutDirection) > 0.9999f)
            {
                cutterRotation = Quaternion.identity;
            }
            else
            {
                cutterRotation = Quaternion.FromToRotation(Vector3.up, cutDirection);
            }
            tempCutter.transform.rotation = cutterRotation;
            tempCutter.transform.localScale = Vector3.one;

            CADObject cutterCad = tempCutter.AddComponent<CADObject>();
            cutterCad.SetMesh(holeCylinderMesh);

            res = ExecuteBoolean(targetObj, cutterCad, BooleanOperation.Subtract);
            if (Application.isPlaying)
            {
                Destroy(tempCutter);
            }
            else
            {
                DestroyImmediate(tempCutter);
            }

            if (res.status == BooleanResultStatus.Success)
            {
                res.message = $"Hole cut completed ({(options.holeType == HoleType.Through ? "Through" : "Blind")}, r={options.radius * 1000f:F1}mm, d={options.depth * 1000f:F1}mm)";
            }

            return res;
        }

        private float ComputeTargetThicknessAlongAxis(Mesh mesh, Transform transform, Vector3 entryPointWorld, Vector3 cutDirWorld)
        {
            Vector3[] verts = mesh.vertices;
            if (verts == null || verts.Length == 0) return 1f;

            float minProj = float.MaxValue;
            float maxProj = float.MinValue;

            Vector3 cutDirLocal = transform.InverseTransformDirection(cutDirWorld).normalized;
            Vector3 entryLocal = transform.InverseTransformPoint(entryPointWorld);

            float entryProj = Vector3.Dot(entryLocal, cutDirLocal);

            for (int i = 0; i < verts.Length; i++)
            {
                float proj = Vector3.Dot(verts[i], cutDirLocal);
                if (proj < minProj) minProj = proj;
                if (proj > maxProj) maxProj = proj;
            }

            // Thickness is the positive forward span from entry along cut direction
            float extent = maxProj - minProj;
            return Mathf.Max(extent, 0.05f);
        }

        /// <summary>
        /// Backward-compatible CutHole method.
        /// </summary>
        public bool CutHole(CADObject targetObj, Vector3 centerWorld, Vector3 axisWorld, float radius, float depth, int radialSegments = 24)
        {
            var options = new HoleOptions
            {
                holeType = depth >= 1.0f ? HoleType.Through : HoleType.Blind,
                radius = radius,
                depth = depth,
                radialSegments = radialSegments,
                overcutMargin = CSGTolerance.ThroughHoleOvercutMargin
            };

            // In legacy API, axisWorld was the cylinder axis direction. We cut along axisWorld, so outward normal is -axisWorld.
            Vector3 normalWorld = -axisWorld.normalized;
            BooleanResult res = CutHoleAdvanced(targetObj, centerWorld, normalWorld, options);

            if (res.status == BooleanResultStatus.Success)
            {
                targetObj.SetMesh(res.resultMesh);
                CADManagerHub.Instance?.OnMeshModified(targetObj);
                return true;
            }

            return false;
        }

        #endregion

        #region Helpers

        private Mesh CombineDisjointMeshes(Mesh meshA, Mesh meshB, Matrix4x4 transformB)
        {
            Vector3[] vA = meshA.vertices;
            Vector3[] nA = meshA.normals;
            Vector2[] uvA = meshA.uv;
            int[] tA = meshA.triangles;

            Vector3[] vB = meshB.vertices;
            Vector3[] nB = meshB.normals;
            Vector2[] uvB = meshB.uv;
            int[] tB = meshB.triangles;

            Matrix4x4 normalMatrixB = transformB.inverse.transpose;

            var outVerts = new List<Vector3>(vA.Length + vB.Length);
            var outNorms = new List<Vector3>(vA.Length + vB.Length);
            var outUVs = new List<Vector2>(vA.Length + vB.Length);
            var outTris = new List<int>(tA.Length + tB.Length);

            outVerts.AddRange(vA);
            outNorms.AddRange(nA != null && nA.Length == vA.Length ? nA : new Vector3[vA.Length]);
            outUVs.AddRange(uvA != null && uvA.Length == vA.Length ? uvA : new Vector2[vA.Length]);
            outTris.AddRange(tA);

            int offset = vA.Length;
            for (int i = 0; i < vB.Length; i++)
            {
                outVerts.Add(transformB.MultiplyPoint3x4(vB[i]));
                Vector3 norm = nB != null && i < nB.Length ? normalMatrixB.MultiplyVector(nB[i]).normalized : Vector3.up;
                outNorms.Add(norm);
                outUVs.Add(uvB != null && i < uvB.Length ? uvB[i] : Vector2.zero);
            }

            for (int i = 0; i < tB.Length; i++)
            {
                outTris.Add(tB[i] + offset);
            }

            Mesh result = new Mesh { name = "Combined_Disjoint_Mesh" };
            if (outVerts.Count > 65534)
            {
                result.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }

            result.SetVertices(outVerts);
            result.SetNormals(outNorms);
            result.SetUVs(0, outUVs);
            result.SetTriangles(outTris, 0);

            result.RecalculateBounds();
            result.RecalculateTangents();
            return result;
        }

        public static Bounds TransformBounds(Bounds localBounds, Matrix4x4 matrix)
        {
            Vector3 center = matrix.MultiplyPoint3x4(localBounds.center);
            Vector3 extents = localBounds.extents;
            Vector3 axisX = matrix.MultiplyVector(new Vector3(extents.x, 0, 0));
            Vector3 axisY = matrix.MultiplyVector(new Vector3(0, extents.y, 0));
            Vector3 axisZ = matrix.MultiplyVector(new Vector3(0, 0, extents.z));
            Vector3 newExtents = new Vector3(
                Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
                Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
                Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z)
            );
            return new Bounds(center, newExtents * 2f);
        }

        #endregion
    }
}
