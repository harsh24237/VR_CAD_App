using System;
using System.Collections.Generic;
using UnityEngine;

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

            Mesh resultMesh = ExecuteBooleanOperation(meshA, transA, meshB, transB, operation);
            if (resultMesh != null)
            {
                targetObj.SetMesh(resultMesh);
                CADManagerHub.Instance?.OnMeshModified(targetObj);

                // If subtract or union, optionally remove or deactivate tool
                if (operation == BooleanOperation.Subtract || operation == BooleanOperation.Union)
                {
                    CADManagerHub.Instance?.ShapeManager?.DeleteObject(toolObj);
                }

                return true;
            }

            return false;
        }

        public bool CutHole(CADObject targetObj, Vector3 centerWorld, Vector3 axisWorld, float radius, float depth, int radialSegments = 24)
        {
            if (targetObj == null) return false;

            // Generate a tool cylinder representing the hole volume
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
                targetObj.SetMesh(resultMesh);
                CADManagerHub.Instance?.OnMeshModified(targetObj);
                return true;
            }

            return false;
        }

        private Mesh ExecuteBooleanOperation(Mesh meshA, Transform transA, Mesh meshB, Transform transB, BooleanOperation op)
        {
            // Transform Mesh B into A's local space
            Vector3[] vertsA = meshA.vertices;
            int[] trisA = meshA.triangles;

            Vector3[] vertsB = meshB.vertices;
            int[] trisB = meshB.triangles;

            Vector3[] transformedVertsB = new Vector3[vertsB.Length];
            for (int i = 0; i < vertsB.Length; i++)
            {
                Vector3 worldPt = transB.TransformPoint(vertsB[i]);
                transformedVertsB[i] = transA.InverseTransformPoint(worldPt);
            }

            Bounds boundsA = meshA.bounds;
            Bounds boundsB = new Bounds(transformedVertsB[0], Vector3.zero);
            foreach (var p in transformedVertsB) boundsB.Encapsulate(p);

            // If no bounding overlap, handle trivial cases
            if (!boundsA.Intersects(boundsB))
            {
                if (op == BooleanOperation.Union)
                {
                    return CombineDisjointMeshes(vertsA, trisA, transformedVertsB, trisB);
                }
                if (op == BooleanOperation.Subtract)
                {
                    return UnityEngine.Object.Instantiate(meshA);
                }
                if (op == BooleanOperation.Intersect)
                {
                    return new Mesh { name = "Empty_Intersection" };
                }
            }

            // Perform robust spatial volumetric partition
            return ExecuteVolumetricCSG(vertsA, trisA, transformedVertsB, trisB, boundsA, boundsB, op);
        }

        private Mesh ExecuteVolumetricCSG(
            Vector3[] vertsA, int[] trisA,
            Vector3[] vertsB, int[] trisB,
            Bounds boundsA, Bounds boundsB,
            BooleanOperation op)
        {
            List<Vector3> outVerts = new List<Vector3>();
            List<int> outTris = new List<int>();

            // Approximate volumetric signed distance / containment classifier
            bool IsInsideB(Vector3 pt)
            {
                if (!boundsB.Contains(pt)) return false;
                // Fast ray casting parity test
                int hits = 0;
                Ray ray = new Ray(pt, Vector3.up);
                for (int t = 0; t < trisB.Length; t += 3)
                {
                    if (RayIntersectsTriangle(ray, vertsB[trisB[t]], vertsB[trisB[t + 1]], vertsB[trisB[t + 2]], out float dist))
                    {
                        if (dist > 0.0001f) hits++;
                    }
                }
                return (hits % 2) == 1;
            }

            bool IsInsideA(Vector3 pt)
            {
                if (!boundsA.Contains(pt)) return false;
                int hits = 0;
                Ray ray = new Ray(pt, Vector3.up);
                for (int t = 0; t < trisA.Length; t += 3)
                {
                    if (RayIntersectsTriangle(ray, vertsA[trisA[t]], vertsA[trisA[t + 1]], vertsA[trisA[t + 2]], out float dist))
                    {
                        if (dist > 0.0001f) hits++;
                    }
                }
                return (hits % 2) == 1;
            }

            // Process Mesh A triangles
            for (int t = 0; t < trisA.Length; t += 3)
            {
                Vector3 v0 = vertsA[trisA[t + 0]];
                Vector3 v1 = vertsA[trisA[t + 1]];
                Vector3 v2 = vertsA[trisA[t + 2]];
                Vector3 centroid = (v0 + v1 + v2) / 3f;

                bool insideB = IsInsideB(centroid);

                bool keepA = op switch
                {
                    BooleanOperation.Union => !insideB,
                    BooleanOperation.Subtract => !insideB,
                    BooleanOperation.Intersect => insideB,
                    _ => true
                };

                if (keepA)
                {
                    int start = outVerts.Count;
                    outVerts.AddRange(new[] { v0, v1, v2 });
                    outTris.AddRange(new[] { start, start + 1, start + 2 });
                }
            }

            // Process Mesh B triangles
            for (int t = 0; t < trisB.Length; t += 3)
            {
                Vector3 v0 = vertsB[trisB[t + 0]];
                Vector3 v1 = vertsB[trisB[t + 1]];
                Vector3 v2 = vertsB[trisB[t + 2]];
                Vector3 centroid = (v0 + v1 + v2) / 3f;

                bool insideA = IsInsideA(centroid);

                bool keepB = op switch
                {
                    BooleanOperation.Union => !insideA,
                    BooleanOperation.Subtract => insideA, // Invert normals for subtract cavity
                    BooleanOperation.Intersect => insideA,
                    _ => false
                };

                if (keepB)
                {
                    int start = outVerts.Count;
                    outVerts.AddRange(new[] { v0, v1, v2 });

                    if (op == BooleanOperation.Subtract)
                    {
                        // Invert winding order for interior walls
                        outTris.AddRange(new[] { start, start + 2, start + 1 });
                    }
                    else
                    {
                        outTris.AddRange(new[] { start, start + 1, start + 2 });
                    }
                }
            }

            Mesh result = new Mesh { name = $"CSG_{op}_Result" };
            if (outVerts.Count > 65535)
            {
                result.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }

            result.SetVertices(outVerts);
            result.SetTriangles(outTris, 0);
            result.RecalculateNormals();
            result.RecalculateBounds();
            result.RecalculateTangents();

            return result;
        }

        private Mesh CombineDisjointMeshes(Vector3[] vA, int[] tA, Vector3[] vB, int[] tB)
        {
            Mesh mesh = new Mesh { name = "Combined_Disjoint" };
            List<Vector3> verts = new List<Vector3>(vA);
            List<int> tris = new List<int>(tA);

            int offset = verts.Count;
            verts.AddRange(vB);
            for (int i = 0; i < tB.Length; i++)
            {
                tris.Add(tB[i] + offset);
            }

            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private bool RayIntersectsTriangle(Ray ray, Vector3 v0, Vector3 v1, Vector3 v2, out float distance)
        {
            distance = 0f;
            Vector3 e1 = v1 - v0;
            Vector3 e2 = v2 - v0;
            Vector3 pvec = Vector3.Cross(ray.direction, e2);
            float det = Vector3.Dot(e1, pvec);

            if (Mathf.Abs(det) < 1e-8f) return false;

            float invDet = 1.0f / det;
            Vector3 tvec = ray.origin - v0;
            float u = Vector3.Dot(tvec, pvec) * invDet;
            if (u < 0.0f || u > 1.0f) return false;

            Vector3 qvec = Vector3.Cross(tvec, e1);
            float v = Vector3.Dot(ray.direction, qvec) * invDet;
            if (v < 0.0f || u + v > 1.0f) return false;

            distance = Vector3.Dot(e2, qvec) * invDet;
            return distance > 0.0f;
        }
    }
}
