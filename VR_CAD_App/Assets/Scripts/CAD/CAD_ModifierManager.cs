using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRCAD.Core
{
    public class CAD_ModifierManager : MonoBehaviour
    {
        [Header("Modifier Settings")]
        [SerializeField] private float defaultChamferSize = 0.02f; // 20mm
        [SerializeField] private float defaultFilletRadius = 0.03f; // 30mm
        [SerializeField] private int filletSegments = 4;

        public float DefaultChamferSize { get => defaultChamferSize; set => defaultChamferSize = value; }
        public float DefaultFilletRadius { get => defaultFilletRadius; set => defaultFilletRadius = value; }
        public int FilletSegments { get => filletSegments; set => filletSegments = Mathf.Clamp(value, 1, 16); }

        public bool ApplyChamferToEdge(CADObject cadObject, (int v1, int v2) edge, float chamferDistance)
        {
            if (cadObject == null || cadObject.MeshFilter.sharedMesh == null || edge.v1 < 0 || edge.v2 < 0)
            {
                return false;
            }

            Mesh mesh = cadObject.MeshFilter.sharedMesh;
            Vector3[] verts = mesh.vertices;
            int[] tris = mesh.triangles;
            Vector3[] normals = mesh.normals;

            if (edge.v1 >= verts.Length || edge.v2 >= verts.Length) return false;

            Vector3 p1 = verts[edge.v1];
            Vector3 p2 = verts[edge.v2];
            Vector3 edgeDir = (p2 - p1).normalized;

            // Find all triangles sharing this edge
            List<int> adjacentTris = new List<int>();
            for (int t = 0; t < tris.Length; t += 3)
            {
                int a = tris[t + 0];
                int b = tris[t + 1];
                int c = tris[t + 2];

                bool hasV1 = (a == edge.v1 || b == edge.v1 || c == edge.v1);
                bool hasV2 = (a == edge.v2 || b == edge.v2 || c == edge.v2);

                if (hasV1 && hasV2)
                {
                    adjacentTris.Add(t / 3);
                }
            }

            if (adjacentTris.Count < 2)
            {
                // Must have at least two adjacent faces to chamfer an edge
                return false;
            }

            // Split vertices along the edge for both adjacent faces
            List<Vector3> newVerts = new List<Vector3>(verts);
            List<int> newTris = new List<int>(tris);

            // Compute face normals of adjacent faces
            int triA = adjacentTris[0];
            int triB = adjacentTris[1];

            Vector3 normA = Vector3.Cross(verts[tris[triA * 3 + 1]] - verts[tris[triA * 3 + 0]], verts[tris[triA * 3 + 2]] - verts[tris[triA * 3 + 0]]).normalized;
            Vector3 normB = Vector3.Cross(verts[tris[triB * 3 + 1]] - verts[tris[triB * 3 + 0]], verts[tris[triB * 3 + 2]] - verts[tris[triB * 3 + 0]]).normalized;

            Vector3 bisector = ((normA + normB) * 0.5f).normalized;
            Vector3 cutOffset = -bisector * chamferDistance;

            // Add chamfer bevel plane vertices
            int c1 = newVerts.Count;
            newVerts.Add(p1 + cutOffset);
            int c2 = newVerts.Count;
            newVerts.Add(p2 + cutOffset);

            // Modify vertices in place
            newVerts[edge.v1] = p1 + Vector3.Cross(edgeDir, normA) * chamferDistance;
            newVerts[edge.v2] = p2 + Vector3.Cross(edgeDir, normA) * chamferDistance;

            // Add the chamfer bridge quad
            newTris.Add(edge.v1);
            newTris.Add(c1);
            newTris.Add(c2);

            newTris.Add(edge.v1);
            newTris.Add(c2);
            newTris.Add(edge.v2);

            Mesh modified = new Mesh { name = mesh.name + "_Chamfered" };
            modified.SetVertices(newVerts);
            modified.SetTriangles(newTris, 0);
            modified.RecalculateNormals();
            modified.RecalculateBounds();

            cadObject.SetMesh(modified);
            CADManagerHub.Instance?.OnMeshModified(cadObject);
            return true;
        }

        public bool ApplyFilletToEdge(CADObject cadObject, (int v1, int v2) edge, float radius, int segments)
        {
            if (cadObject == null || cadObject.MeshFilter.sharedMesh == null) return false;

            // Multi-segment iterative bevel for rounding
            float stepSize = radius / Mathf.Max(1, segments);
            bool success = false;
            for (int i = 0; i < segments; i++)
            {
                success = ApplyChamferToEdge(cadObject, edge, stepSize);
            }
            return success;
        }
    }
}
