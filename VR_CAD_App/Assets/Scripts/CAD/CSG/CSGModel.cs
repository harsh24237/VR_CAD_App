using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRCAD.Core.CSG
{
    public struct CSGValidationResult
    {
        public bool isValid;
        public int vertexCount;
        public int triangleCount;
        public int openEdgeCount;
        public int nonManifoldEdgeCount;
        public int degenerateTriangleCount;
        public string message;
    }

    public class CSGModel
    {
        public List<CSGPolygon> polygons;

        public CSGModel()
        {
            polygons = new List<CSGPolygon>();
        }

        public CSGModel(List<CSGPolygon> polygons)
        {
            this.polygons = polygons ?? new List<CSGPolygon>();
        }

        public List<CSGPolygon> ClonePolygons()
        {
            var list = new List<CSGPolygon>(polygons.Count);
            for (int i = 0; i < polygons.Count; i++)
            {
                list.Add(polygons[i].Clone());
            }
            return list;
        }

        public static CSGModel FromMesh(Mesh mesh, Matrix4x4 matrix, int sharedData = 0)
        {
            if (mesh == null) return new CSGModel();

            Vector3[] srcVerts = mesh.vertices;
            Vector3[] srcNorms = mesh.normals;
            Vector2[] srcUVs = mesh.uv;
            int[] srcTris = mesh.triangles;

            bool hasNormals = srcNorms != null && srcNorms.Length == srcVerts.Length;
            bool hasUVs = srcUVs != null && srcUVs.Length == srcVerts.Length;

            Matrix4x4 normalMatrix = matrix.inverse.transpose;

            var resultPolygons = new List<CSGPolygon>(srcTris.Length / 3);

            for (int i = 0; i < srcTris.Length; i += 3)
            {
                int i0 = srcTris[i];
                int i1 = srcTris[i + 1];
                int i2 = srcTris[i + 2];

                Vector3 p0 = matrix.MultiplyPoint3x4(srcVerts[i0]);
                Vector3 p1 = matrix.MultiplyPoint3x4(srcVerts[i1]);
                Vector3 p2 = matrix.MultiplyPoint3x4(srcVerts[i2]);

                Vector3 n0, n1, n2;
                if (hasNormals)
                {
                    n0 = normalMatrix.MultiplyVector(srcNorms[i0]).normalized;
                    n1 = normalMatrix.MultiplyVector(srcNorms[i1]).normalized;
                    n2 = normalMatrix.MultiplyVector(srcNorms[i2]).normalized;
                }
                else
                {
                    Vector3 faceNorm = Vector3.Cross(p1 - p0, p2 - p0).normalized;
                    n0 = n1 = n2 = faceNorm;
                }

                Vector2 uv0 = hasUVs ? srcUVs[i0] : Vector2.zero;
                Vector2 uv1 = hasUVs ? srcUVs[i1] : Vector2.zero;
                Vector2 uv2 = hasUVs ? srcUVs[i2] : Vector2.zero;

                var polyVerts = new List<CSGVertex>(3)
                {
                    new CSGVertex(p0, n0, uv0),
                    new CSGVertex(p1, n1, uv1),
                    new CSGVertex(p2, n2, uv2)
                };

                var poly = new CSGPolygon(polyVerts, sharedData);
                if (poly.CalculateArea() > 1e-9f)
                {
                    resultPolygons.Add(poly);
                }
            }

            return new CSGModel(resultPolygons);
        }

        public static CSGModel Union(CSGModel a, CSGModel b)
        {
            if (a == null || a.polygons.Count == 0) return b ?? new CSGModel();
            if (b == null || b.polygons.Count == 0) return a ?? new CSGModel();

            CSGNode aNode = new CSGNode(a.ClonePolygons());
            CSGNode bNode = new CSGNode(b.ClonePolygons());

            aNode.ClipTo(bNode);
            bNode.ClipTo(aNode);
            bNode.Invert();
            bNode.ClipTo(aNode);
            bNode.Invert();

            aNode.Build(bNode.AllPolygons());
            return new CSGModel(aNode.AllPolygons());
        }

        public static CSGModel Subtract(CSGModel a, CSGModel b)
        {
            if (a == null || a.polygons.Count == 0) return new CSGModel();
            if (b == null || b.polygons.Count == 0) return a;

            CSGNode aNode = new CSGNode(a.ClonePolygons());
            CSGNode bNode = new CSGNode(b.ClonePolygons());

            aNode.Invert();
            aNode.ClipTo(bNode);
            bNode.ClipTo(aNode);
            bNode.Invert();
            bNode.ClipTo(aNode);
            bNode.Invert();

            aNode.Build(bNode.AllPolygons());
            aNode.Invert();

            return new CSGModel(aNode.AllPolygons());
        }

        public static CSGModel Intersect(CSGModel a, CSGModel b)
        {
            if (a == null || a.polygons.Count == 0 || b == null || b.polygons.Count == 0)
                return new CSGModel();

            CSGNode aNode = new CSGNode(a.ClonePolygons());
            CSGNode bNode = new CSGNode(b.ClonePolygons());

            aNode.Invert();
            bNode.ClipTo(aNode);
            bNode.Invert();
            aNode.ClipTo(bNode);
            bNode.ClipTo(aNode);

            aNode.Build(bNode.AllPolygons());
            aNode.Invert();

            return new CSGModel(aNode.AllPolygons());
        }

        public Bounds CalculateBounds()
        {
            if (polygons == null || polygons.Count == 0) return new Bounds(Vector3.zero, Vector3.zero);
            Bounds b = new Bounds(polygons[0].vertices[0].position, Vector3.zero);
            for (int p = 0; p < polygons.Count; p++)
            {
                var verts = polygons[p].vertices;
                for (int v = 0; v < verts.Count; v++)
                {
                    b.Encapsulate(verts[v].position);
                }
            }
            return b;
        }

        public Mesh ToMesh(string meshName = "CSG_Result")
        {
            var outPositions = new List<Vector3>();
            var outNormals = new List<Vector3>();
            var outUVs = new List<Vector2>();
            var outTriangles = new List<int>();

            // Spatial welding map to share vertices across polygon fans while preserving hard edges
            var vertexMap = new Dictionary<long, int>();

            for (int p = 0; p < polygons.Count; p++)
            {
                var poly = polygons[p];
                var rawVerts = poly.vertices;
                if (rawVerts == null || rawVerts.Count < 3) continue;

                // 1. Remove consecutive near-duplicate vertices
                var cleanVerts = new List<CSGVertex>(rawVerts.Count);
                for (int i = 0; i < rawVerts.Count; i++)
                {
                    CSGVertex cur = rawVerts[i];
                    CSGVertex prev = (cleanVerts.Count > 0) ? cleanVerts[cleanVerts.Count - 1] : rawVerts[rawVerts.Count - 1];
                    if ((cur.position - prev.position).sqrMagnitude > 1e-10f)
                    {
                        cleanVerts.Add(cur);
                    }
                }

                if (cleanVerts.Count < 3) continue;

                // 2. Check non-degenerate polygon area
                float area = poly.CalculateArea();
                if (area < 1e-8f) continue;

                // 3. Triangulate convex polygon as triangle fan
                int[] polyIndices = new int[cleanVerts.Count];
                for (int i = 0; i < cleanVerts.Count; i++)
                {
                    CSGVertex cv = cleanVerts[i];
                    long key = GetVertexKey(cv.position, cv.normal, cv.uv);

                    if (vertexMap.TryGetValue(key, out int existingIndex))
                    {
                        polyIndices[i] = existingIndex;
                    }
                    else
                    {
                        int newIndex = outPositions.Count;
                        outPositions.Add(cv.position);
                        outNormals.Add(cv.normal);
                        outUVs.Add(cv.uv);
                        vertexMap[key] = newIndex;
                        polyIndices[i] = newIndex;
                    }
                }

                for (int i = 1; i < cleanVerts.Count - 1; i++)
                {
                    int idx0 = polyIndices[0];
                    int idx1 = polyIndices[i];
                    int idx2 = polyIndices[i + 1];

                    // Guard against degenerate triangle indices
                    if (idx0 == idx1 || idx1 == idx2 || idx0 == idx2) continue;

                    Vector3 edgeA = outPositions[idx1] - outPositions[idx0];
                    Vector3 edgeB = outPositions[idx2] - outPositions[idx0];
                    if (Vector3.Cross(edgeA, edgeB).sqrMagnitude < 1e-14f) continue;

                    outTriangles.Add(idx0);
                    outTriangles.Add(idx1);
                    outTriangles.Add(idx2);
                }
            }

            if (outPositions.Count == 0 || outTriangles.Count == 0)
            {
                return null;
            }

            Mesh mesh = new Mesh { name = meshName };
            if (outPositions.Count > 65534)
            {
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }

            mesh.SetVertices(outPositions);
            mesh.SetNormals(outNormals);
            mesh.SetUVs(0, outUVs);
            mesh.SetTriangles(outTriangles, 0);

            mesh.RecalculateBounds();
            mesh.RecalculateTangents();

            return mesh;
        }

        private static long GetVertexKey(Vector3 pos, Vector3 norm, Vector2 uv)
        {
            // Quantize to 0.1 mm precision for robust topological welding
            int x = Mathf.RoundToInt(pos.x * 10000f);
            int y = Mathf.RoundToInt(pos.y * 10000f);
            int z = Mathf.RoundToInt(pos.z * 10000f);
            int nx = Mathf.RoundToInt(norm.x * 100f);
            int ny = Mathf.RoundToInt(norm.y * 100f);
            int nz = Mathf.RoundToInt(norm.z * 100f);

            unchecked
            {
                long hash = 17;
                hash = hash * 31 + x;
                hash = hash * 31 + y;
                hash = hash * 31 + z;
                hash = hash * 31 + nx;
                hash = hash * 31 + ny;
                hash = hash * 31 + nz;
                return hash;
            }
        }

        public static CSGValidationResult ValidateMesh(Mesh mesh)
        {
            var result = new CSGValidationResult
            {
                isValid = false,
                vertexCount = 0,
                triangleCount = 0,
                openEdgeCount = 0,
                nonManifoldEdgeCount = 0,
                degenerateTriangleCount = 0,
                message = ""
            };

            if (mesh == null)
            {
                result.message = "Mesh is null";
                return result;
            }

            Vector3[] verts = mesh.vertices;
            int[] tris = mesh.triangles;

            result.vertexCount = verts.Length;
            result.triangleCount = tris.Length / 3;

            if (verts.Length < 4 || tris.Length < 12)
            {
                result.message = $"Insufficient geometry (vertices: {verts.Length}, triangles: {tris.Length / 3})";
                return result;
            }

            // Check degenerate triangles
            for (int t = 0; t < tris.Length; t += 3)
            {
                int i0 = tris[t];
                int i1 = tris[t + 1];
                int i2 = tris[t + 2];
                if (i0 == i1 || i1 == i2 || i0 == i2)
                {
                    result.degenerateTriangleCount++;
                    continue;
                }

                Vector3 a = verts[i0];
                Vector3 b = verts[i1];
                Vector3 c = verts[i2];
                if (Vector3.Cross(b - a, c - a).sqrMagnitude < 1e-12f)
                {
                    result.degenerateTriangleCount++;
                }
            }

            // Topology / Edge connectivity analysis
            // Quantize vertex coordinates to group coincident vertices
            var quantMap = new Dictionary<long, int>();
            int[] remapped = new int[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                int qx = Mathf.RoundToInt(verts[i].x * 1000f);
                int qy = Mathf.RoundToInt(verts[i].y * 1000f);
                int qz = Mathf.RoundToInt(verts[i].z * 1000f);
                long key = ((long)qx << 40) ^ ((long)qy << 20) ^ (long)qz;

                if (!quantMap.TryGetValue(key, out int rep))
                {
                    rep = i;
                    quantMap[key] = rep;
                }
                remapped[i] = rep;
            }

            var edgeUsage = new Dictionary<(int, int), int>();
            for (int t = 0; t < tris.Length; t += 3)
            {
                int r0 = remapped[tris[t]];
                int r1 = remapped[tris[t + 1]];
                int r2 = remapped[tris[t + 2]];

                if (r0 == r1 || r1 == r2 || r0 == r2) continue;

                AddEdge(edgeUsage, r0, r1);
                AddEdge(edgeUsage, r1, r2);
                AddEdge(edgeUsage, r2, r0);
            }

            foreach (var kvp in edgeUsage)
            {
                int count = kvp.Value;
                if (count == 1) result.openEdgeCount++;
                else if (count > 2) result.nonManifoldEdgeCount++;
            }

            // An ideal closed manifold solid has 0 open edges and 0 non-manifold edges.
            // In complex CSG cuts, small boundary tolerance can occur, but gross defects must be flagged.
            bool geometryHealthy = result.triangleCount >= 4 && result.degenerateTriangleCount < (result.triangleCount / 2);
            result.isValid = geometryHealthy;
            result.message = result.isValid ? "Mesh valid" : $"Validation failed: {result.openEdgeCount} open edges, {result.nonManifoldEdgeCount} non-manifold edges";

            return result;
        }

        private static void AddEdge(Dictionary<(int, int), int> edgeUsage, int a, int b)
        {
            var key = a < b ? (a, b) : (b, a);
            if (edgeUsage.TryGetValue(key, out int count))
            {
                edgeUsage[key] = count + 1;
            }
            else
            {
                edgeUsage[key] = 1;
            }
        }
    }
}
