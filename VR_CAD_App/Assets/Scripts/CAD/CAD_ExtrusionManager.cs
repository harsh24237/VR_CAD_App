using System.Collections.Generic;
using UnityEngine;

namespace VRCAD.Core
{
    public class CAD_ExtrusionManager : MonoBehaviour
    {
        [Header("Extrusion Settings")]
        [SerializeField] private float defaultExtrudeDistance = 0.1f; // 100mm default

        public float DefaultExtrudeDistance { get => defaultExtrudeDistance; set => defaultExtrudeDistance = value; }

        public bool ExtrudeSelectedFace(CADObject cadObject, int triangleIndex, float distance)
        {
            if (cadObject == null || cadObject.MeshFilter.sharedMesh == null || triangleIndex < 0)
            {
                return false;
            }

            Mesh originalMesh = cadObject.MeshFilter.sharedMesh;
            Vector3[] verts = originalMesh.vertices;
            int[] tris = originalMesh.triangles;
            Vector3[] normals = originalMesh.normals;
            Vector2[] uvs = originalMesh.uv;

            if (triangleIndex * 3 + 2 >= tris.Length) return false;

            int i0 = tris[triangleIndex * 3 + 0];
            int i1 = tris[triangleIndex * 3 + 1];
            int i2 = tris[triangleIndex * 3 + 2];

            Vector3 v0 = verts[i0];
            Vector3 v1 = verts[i1];
            Vector3 v2 = verts[i2];

            // Compute face normal
            Vector3 faceNormal = Vector3.Cross(v1 - v0, v2 - v0).normalized;
            if (faceNormal.sqrMagnitude < 0.001f) faceNormal = normals[i0];

            Vector3 offset = faceNormal * distance;

            // Collect all triangles that share the same normal & plane (coplanar face grouping)
            List<int> faceTriangles = FindCoplanarFaceTriangles(verts, tris, triangleIndex, faceNormal);

            // Extract vertices in this coplanar face
            HashSet<int> faceVertIndices = new HashSet<int>();
            foreach (int triIdx in faceTriangles)
            {
                faceVertIndices.Add(tris[triIdx * 3 + 0]);
                faceVertIndices.Add(tris[triIdx * 3 + 1]);
                faceVertIndices.Add(tris[triIdx * 3 + 2]);
            }

            // Find boundary edges of the coplanar face
            List<(int a, int b)> boundaryEdges = FindBoundaryEdges(tris, faceTriangles);

            // Reconstruct mesh
            List<Vector3> newVerts = new List<Vector3>(verts);
            List<Vector3> newNorms = new List<Vector3>(normals);
            List<Vector2> newUvs = new List<Vector2>(uvs);
            List<int> newTris = new List<int>(tris);

            // Map old vertex index -> new extruded vertex index
            Dictionary<int, int> oldToExtrudedMap = new Dictionary<int, int>();
            foreach (int vIdx in faceVertIndices)
            {
                int newIdx = newVerts.Count;
                newVerts.Add(verts[vIdx] + offset);
                newNorms.Add(faceNormal);
                newUvs.Add(uvs.Length > vIdx ? uvs[vIdx] : Vector2.zero);
                oldToExtrudedMap[vIdx] = newIdx;
            }

            // Update face triangles to use new extruded cap vertices
            foreach (int triIdx in faceTriangles)
            {
                newTris[triIdx * 3 + 0] = oldToExtrudedMap[tris[triIdx * 3 + 0]];
                newTris[triIdx * 3 + 1] = oldToExtrudedMap[tris[triIdx * 3 + 1]];
                newTris[triIdx * 3 + 2] = oldToExtrudedMap[tris[triIdx * 3 + 2]];
            }

            // Create side quad skirts along boundary edges
            foreach (var edge in boundaryEdges)
            {
                int botA = edge.a;
                int botB = edge.b;
                int topA = oldToExtrudedMap[edge.a];
                int topB = oldToExtrudedMap[edge.b];

                // Quad side 1
                newTris.Add(botA);
                newTris.Add(topA);
                newTris.Add(topB);

                // Quad side 2
                newTris.Add(botA);
                newTris.Add(topB);
                newTris.Add(botB);
            }

            // Build new mesh
            Mesh extrudedMesh = new Mesh
            {
                name = originalMesh.name + "_Extruded"
            };

            extrudedMesh.SetVertices(newVerts);
            extrudedMesh.SetTriangles(newTris, 0);
            extrudedMesh.SetUVs(0, newUvs);
            extrudedMesh.RecalculateNormals();
            extrudedMesh.RecalculateBounds();
            extrudedMesh.RecalculateTangents();

            cadObject.SetMesh(extrudedMesh);
            CADManagerHub.Instance?.OnMeshModified(cadObject);
            return true;
        }

        private List<int> FindCoplanarFaceTriangles(Vector3[] verts, int[] tris, int startTri, Vector3 normal)
        {
            List<int> result = new List<int> { startTri };
            int totalTris = tris.Length / 3;

            for (int i = 0; i < totalTris; i++)
            {
                if (i == startTri) continue;

                Vector3 a = verts[tris[i * 3 + 0]];
                Vector3 b = verts[tris[i * 3 + 1]];
                Vector3 c = verts[tris[i * 3 + 2]];
                Vector3 n = Vector3.Cross(b - a, c - a).normalized;

                if (Vector3.Dot(n, normal) > 0.98f)
                {
                    // Check if coplanar
                    float planeDist = Mathf.Abs(Vector3.Dot(a - verts[tris[startTri * 3 + 0]], normal));
                    if (planeDist < 0.005f)
                    {
                        result.Add(i);
                    }
                }
            }

            return result;
        }

        private List<(int a, int b)> FindBoundaryEdges(int[] tris, List<int> faceTriangles)
        {
            Dictionary<(int, int), int> edgeCounts = new Dictionary<(int, int), int>();

            foreach (int triIdx in faceTriangles)
            {
                int i0 = tris[triIdx * 3 + 0];
                int i1 = tris[triIdx * 3 + 1];
                int i2 = tris[triIdx * 3 + 2];

                AddDirectedEdge(edgeCounts, i0, i1);
                AddDirectedEdge(edgeCounts, i1, i2);
                AddDirectedEdge(edgeCounts, i2, i0);
            }

            List<(int a, int b)> boundary = new List<(int a, int b)>();
            foreach (var kvp in edgeCounts)
            {
                if (kvp.Value == 1)
                {
                    boundary.Add(kvp.Key);
                }
            }

            return boundary;
        }

        private void AddDirectedEdge(Dictionary<(int, int), int> edgeCounts, int a, int b)
        {
            var edge = (a, b);
            var reverse = (b, a);

            if (edgeCounts.ContainsKey(reverse))
            {
                edgeCounts[reverse]++;
            }
            else if (edgeCounts.ContainsKey(edge))
            {
                edgeCounts[edge]++;
            }
            else
            {
                edgeCounts[edge] = 1;
            }
        }
    }
}
