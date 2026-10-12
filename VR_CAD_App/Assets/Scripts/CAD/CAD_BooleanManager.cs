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

    public class CAD_BooleanManager : MonoBehaviour
    {
        [Header("CSG Settings")]
        [SerializeField] private float tolerance = 0.0001f;

        public bool PerformBoolean(CADObject targetObj, CADObject toolObj, BooleanOperation operation)
        {
            if (targetObj == null || toolObj == null) return false;

            Mesh meshA = targetObj.MeshFilter.sharedMesh;
            Mesh meshB = toolObj.MeshFilter.sharedMesh;

            if (meshA == null || meshB == null) return false;

            Transform transA = targetObj.transform;
            Transform transB = toolObj.transform;

            Matrix4x4 toolToTarget = transA.worldToLocalMatrix * transB.localToWorldMatrix;

            // Check bounding overlap for non-overlapping subtract
            Bounds boundsA = meshA.bounds;
            Bounds boundsB = TransformBounds(meshB.bounds, toolToTarget);

            if (!boundsA.Intersects(boundsB))
            {
                if (operation == BooleanOperation.Subtract)
                {
                    Debug.Log("[CAD_BooleanManager] Operands do not overlap; subtract skipped to preserve cutter.");
                    CADManagerHub.Instance?.EmitStatus("Subtract aborted: shapes do not overlap. Cutter preserved.");
                    return false;
                }
                else if (operation == BooleanOperation.Intersect)
                {
                    Debug.Log("[CAD_BooleanManager] Operands do not overlap; intersection is empty.");
                    CADManagerHub.Instance?.EmitStatus("Intersect aborted: shapes do not overlap.");
                    return false;
                }
                // For Union, disjoint meshes will be merged into a single multi-body object below
            }

            Mesh resultMesh = ExecuteBooleanOperation(meshA, transA, meshB, transB, operation);
            if (resultMesh != null)
            {
                var validation = CSGModel.ValidateMesh(resultMesh);
                if (!validation.isValid)
                {
                    Debug.LogWarning($"[CAD_BooleanManager] Rejected invalid Boolean result: {validation.message}");
                    CADManagerHub.Instance?.EmitStatus($"Boolean rejected: {validation.message}");
                    return false;
                }

                targetObj.SetMesh(resultMesh);
                CADManagerHub.Instance?.OnMeshModified(targetObj);

                // Deactivate and unregister tool only on successful modification
                if (operation == BooleanOperation.Subtract || operation == BooleanOperation.Union)
                {
                    toolObj.gameObject.SetActive(false);
                    CADManagerHub.Instance?.ShapeManager?.UnregisterObject(toolObj);
                }

                return true;
            }

            return false;
        }

        public bool CutHole(CADObject targetObj, Vector3 centerWorld, Vector3 axisWorld, float radius, float depth, int radialSegments = 24)
        {
            if (targetObj == null) return false;

            Mesh holeCylinder = CAD_ShapeManager.CreateCylinderMesh(radius, depth, radialSegments);
            GameObject tempTool = new GameObject("Temp_HoleTool");
            tempTool.transform.position = centerWorld;
            tempTool.transform.rotation = Quaternion.FromToRotation(Vector3.up, axisWorld);
            tempTool.transform.localScale = Vector3.one;

            Mesh resultMesh = ExecuteBooleanOperation(
                targetObj.MeshFilter.sharedMesh,
                targetObj.transform,
                holeCylinder,
                tempTool.transform,
                BooleanOperation.Subtract
            );

            Destroy(tempTool);

            if (resultMesh != null)
            {
                var validation = CSGModel.ValidateMesh(resultMesh);
                if (!validation.isValid)
                {
                    Debug.LogWarning($"[CAD_BooleanManager] CutHole validation failed: {validation.message}");
                    return false;
                }

                targetObj.SetMesh(resultMesh);
                CADManagerHub.Instance?.OnMeshModified(targetObj);
                return true;
            }

            return false;
        }

        public Mesh ExecuteBooleanOperation(Mesh meshA, Transform transA, Mesh meshB, Transform transB, BooleanOperation op)
        {
            if (meshA == null || meshB == null) return null;

            Matrix4x4 toolToTarget = transA.worldToLocalMatrix * transB.localToWorldMatrix;

            // Handle trivial disjoint Union by combining sub-meshes
            Bounds boundsA = meshA.bounds;
            Bounds boundsB = TransformBounds(meshB.bounds, toolToTarget);
            if (!boundsA.Intersects(boundsB) && op == BooleanOperation.Union)
            {
                return CombineDisjointMeshes(meshA, meshB, toolToTarget);
            }

            CSGModel modelA = CSGModel.FromMesh(meshA, Matrix4x4.identity, 0);
            CSGModel modelB = CSGModel.FromMesh(meshB, toolToTarget, 1);

            CSGModel resultModel = op switch
            {
                BooleanOperation.Union => CSGModel.Union(modelA, modelB),
                BooleanOperation.Subtract => CSGModel.Subtract(modelA, modelB),
                BooleanOperation.Intersect => CSGModel.Intersect(modelA, modelB),
                _ => null
            };

            if (resultModel == null || resultModel.polygons == null || resultModel.polygons.Count == 0)
            {
                return null;
            }

            Mesh resultMesh = resultModel.ToMesh($"CSG_{op}_Result");
            if (resultMesh != null)
            {
                resultMesh.RecalculateNormals();
                resultMesh.RecalculateBounds();
                resultMesh.RecalculateTangents();
            }

            return resultMesh;
        }

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
    }
}
