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
        public int duplicateTriangleCount;
        public int inconsistentWindingCount;
        public int tJunctionCount;
        public int connectedComponentCount;
        public bool hasNonFiniteCoordinates;
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
            bool isMirrored = matrix.determinant < 0f;

            var resultPolygons = new List<CSGPolygon>(srcTris.Length / 3);

            for (int i = 0; i < srcTris.Length; i += 3)
            {
                // Invert winding if the matrix has a negative determinant (mirrored/reflected transform)
                int i0 = srcTris[i];
                int i1 = isMirrored ? srcTris[i + 2] : srcTris[i + 1];
                int i2 = isMirrored ? srcTris[i + 1] : srcTris[i + 2];

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
                if (poly.CalculateArea() > CSGTolerance.TriangleAreaEpsilon)
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

        #region Spatial Vertex Welding & Mesh Generation

        private struct RenderVertexKey : IEquatable<RenderVertexKey>
        {
            public readonly int x;
            public readonly int y;
            public readonly int z;

            public RenderVertexKey(int x, int y, int z)
            {
                this.x = x;
                this.y = y;
                this.z = z;
            }

            public RenderVertexKey(Vector3 pos, float cellSize)
            {
                x = Mathf.FloorToInt(pos.x / cellSize);
                y = Mathf.FloorToInt(pos.y / cellSize);
                z = Mathf.FloorToInt(pos.z / cellSize);
            }

            public bool Equals(RenderVertexKey other) => x == other.x && y == other.y && z == other.z;

            public override bool Equals(object obj) => obj is RenderVertexKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 31 + x;
                    hash = hash * 31 + y;
                    hash = hash * 31 + z;
                    return hash;
                }
            }
        }

        private static void ResolveTJunctions(List<CSGPolygon> polyList)
        {
            if (polyList == null || polyList.Count < 2) return;

            float epsSqr = CSGTolerance.DistanceEpsilonSqr;

            // 1. Collect all distinct vertex positions from all polygons
            var candidatePoints = new List<Vector3>();
            for (int p = 0; p < polyList.Count; p++)
            {
                var vList = polyList[p].vertices;
                if (vList == null) continue;
                for (int i = 0; i < vList.Count; i++)
                {
                    Vector3 pt = vList[i].position;
                    bool exists = false;
                    for (int c = 0; c < candidatePoints.Count; c++)
                    {
                        if ((candidatePoints[c] - pt).sqrMagnitude <= epsSqr)
                        {
                            exists = true;
                            break;
                        }
                    }
                    if (!exists)
                    {
                        candidatePoints.Add(pt);
                    }
                }
            }

            // 2. For each polygon, check each edge and insert collinear candidate vertices
            for (int p = 0; p < polyList.Count; p++)
            {
                var poly = polyList[p];
                var verts = poly.vertices;
                if (verts == null || verts.Count < 3) continue;

                var newVerts = new List<CSGVertex>(verts.Count + 4);

                for (int i = 0; i < verts.Count; i++)
                {
                    CSGVertex vCurrent = verts[i];
                    CSGVertex vNext = verts[(i + 1) % verts.Count];
                    newVerts.Add(vCurrent);

                    Vector3 pA = vCurrent.position;
                    Vector3 pB = vNext.position;
                    Vector3 ab = pB - pA;
                    float abLenSqr = ab.sqrMagnitude;
                    if (abLenSqr < 1e-8f) continue;
                    float abLen = Mathf.Sqrt(abLenSqr);

                    var edgeSplits = new List<(float t, Vector3 pt)>();

                    for (int k = 0; k < candidatePoints.Count; k++)
                    {
                        Vector3 pt = candidatePoints[k];

                        // Check distance to endpoints
                        if ((pt - pA).sqrMagnitude <= epsSqr || (pt - pB).sqrMagnitude <= epsSqr)
                            continue;

                        // Must be coplanar with the polygon's surface
                        if (Mathf.Abs(Vector3.Dot(poly.plane.normal, pt) - poly.plane.w) > CSGTolerance.DistanceEpsilon)
                            continue;

                        // Check if projection lies strictly interior to segment by physical distance tolerance
                        float t = Vector3.Dot(pt - pA, ab) / abLenSqr;
                        float distA = t * abLen;
                        float distB = (1f - t) * abLen;

                        if (distA > CSGTolerance.DistanceEpsilon && distB > CSGTolerance.DistanceEpsilon)
                        {
                            Vector3 proj = pA + t * ab;
                            if ((pt - proj).sqrMagnitude <= epsSqr)
                            {
                                bool alreadyAdded = false;
                                for (int s = 0; s < edgeSplits.Count; s++)
                                {
                                    if ((edgeSplits[s].pt - proj).sqrMagnitude <= epsSqr || Mathf.Abs(edgeSplits[s].t - t) < 1e-4f)
                                    {
                                        alreadyAdded = true;
                                        break;
                                    }
                                }
                                if (!alreadyAdded)
                                {
                                    edgeSplits.Add((t, proj));
                                }
                            }
                        }
                    }

                    if (edgeSplits.Count > 0)
                    {
                        edgeSplits.Sort((x, y) => x.t.CompareTo(y.t));
                        for (int s = 0; s < edgeSplits.Count; s++)
                        {
                            CSGVertex interp = CSGVertex.Lerp(vCurrent, vNext, edgeSplits[s].t);
                            interp.position = edgeSplits[s].pt; // exact collinear edge projection
                            newVerts.Add(interp);
                        }
                    }
                }

                poly.vertices = newVerts;
            }
        }

        public Mesh ToMesh(string meshName = "CSG_Result")
        {
            if (polygons == null || polygons.Count == 0) return null;

            ResolveTJunctions(polygons);

            var outPositions = new List<Vector3>();
            var outNormals = new List<Vector3>();
            var outUVs = new List<Vector2>();
            var outTriangles = new List<int>();

            // Collision-safe spatial welding bucket map
            float cellSize = 0.002f;
            var spatialBuckets = new Dictionary<RenderVertexKey, List<int>>();
            var geomIds = new List<int>();
            var distinctGeomPositions = new List<Vector3>();
            var geomBuckets = new Dictionary<RenderVertexKey, List<int>>();

            int GetGeomId(Vector3 pos)
            {
                RenderVertexKey key = new RenderVertexKey(pos, cellSize);
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            RenderVertexKey testKey = new RenderVertexKey(key.x + dx, key.y + dy, key.z + dz);
                            if (geomBuckets.TryGetValue(testKey, out var list))
                            {
                                for (int b = 0; b < list.Count; b++)
                                {
                                    int id = list[b];
                                    if ((pos - distinctGeomPositions[id]).sqrMagnitude <= CSGTolerance.DistanceEpsilonSqr)
                                        return id;
                                }
                            }
                        }
                    }
                }
                int newId = distinctGeomPositions.Count;
                distinctGeomPositions.Add(pos);
                if (!geomBuckets.TryGetValue(key, out var bucket))
                {
                    bucket = new List<int>(4);
                    geomBuckets[key] = bucket;
                }
                bucket.Add(newId);
                return newId;
            }

            int FindOrAddVertex(CSGVertex cv)
            {
                RenderVertexKey centerKey = new RenderVertexKey(cv.position, cellSize);

                // Search center cell and 26 neighbor cells for exact Euclidean match
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            RenderVertexKey testKey = new RenderVertexKey(centerKey.x + dx, centerKey.y + dy, centerKey.z + dz);

                            if (spatialBuckets.TryGetValue(testKey, out var bucket))
                            {
                                for (int b = 0; b < bucket.Count; b++)
                                {
                                    int existingIdx = bucket[b];
                                    Vector3 ep = outPositions[existingIdx];
                                    Vector3 en = outNormals[existingIdx];
                                    Vector2 euv = outUVs[existingIdx];

                                    // Attribute compatibility check: position, normal (hard edge guard), and UV seam guard
                                    if ((cv.position - ep).sqrMagnitude <= CSGTolerance.DistanceEpsilonSqr &&
                                        Vector3.Dot(cv.normal, en) >= CSGTolerance.NormalDotEpsilon &&
                                        (cv.uv - euv).sqrMagnitude <= (CSGTolerance.UVEpsilon * CSGTolerance.UVEpsilon))
                                    {
                                        return existingIdx;
                                    }
                                }
                            }
                        }
                    }
                }

                int newIndex = outPositions.Count;
                outPositions.Add(cv.position);
                outNormals.Add(cv.normal);
                outUVs.Add(cv.uv);
                geomIds.Add(GetGeomId(cv.position));

                if (!spatialBuckets.TryGetValue(centerKey, out var list))
                {
                    list = new List<int>(4);
                    spatialBuckets[centerKey] = list;
                }
                list.Add(newIndex);

                return newIndex;
            }

            var seenGeomTriangles = new HashSet<(int, int, int)>();

            void EmitTriangle(int i0, int i1, int i2)
            {
                if (i0 == i1 || i1 == i2 || i0 == i2) return;

                int g0 = geomIds[i0];
                int g1 = geomIds[i1];
                int g2 = geomIds[i2];
                if (g0 == g1 || g1 == g2 || g0 == g2) return; // Geometrically collapsed!

                int minG = Mathf.Min(g0, Mathf.Min(g1, g2));
                int maxG = Mathf.Max(g0, Mathf.Max(g1, g2));
                int midG = (g0 + g1 + g2) - minG - maxG;
                if (!seenGeomTriangles.Add((minG, midG, maxG)))
                {
                    return; // Duplicate geometric triangle! Filter it out!
                }

                outTriangles.Add(i0);
                outTriangles.Add(i1);
                outTriangles.Add(i2);
            }

            for (int p = 0; p < polygons.Count; p++)
            {
                var poly = polygons[p];
                var rawVerts = poly.vertices;
                if (rawVerts == null || rawVerts.Count < 3) continue;

                // 1. Filter out consecutive near-coincident vertices
                var cleanVerts = new List<CSGVertex>(rawVerts.Count);
                for (int i = 0; i < rawVerts.Count; i++)
                {
                    CSGVertex cur = rawVerts[i];
                    CSGVertex prev = (cleanVerts.Count > 0) ? cleanVerts[cleanVerts.Count - 1] : rawVerts[rawVerts.Count - 1];
                    if ((cur.position - prev.position).sqrMagnitude > 1e-12f)
                    {
                        cleanVerts.Add(cur);
                    }
                }

                // Also check last vs first
                if (cleanVerts.Count >= 3)
                {
                    if ((cleanVerts[cleanVerts.Count - 1].position - cleanVerts[0].position).sqrMagnitude <= 1e-12f)
                    {
                        cleanVerts.RemoveAt(cleanVerts.Count - 1);
                    }
                }

                if (cleanVerts.Count < 3) continue;

                // 2. Register vertices through collision-safe welding
                var rawIndices = new List<int>(cleanVerts.Count);
                for (int i = 0; i < cleanVerts.Count; i++)
                {
                    rawIndices.Add(FindOrAddVertex(cleanVerts[i]));
                }

                // Clean rawIndices: remove consecutive duplicates and backtracking spikes (A -> B -> A)
                bool modified = true;
                while (modified && rawIndices.Count >= 3)
                {
                    modified = false;
                    // Remove consecutive duplicates (including loop closure)
                    for (int i = 0; i < rawIndices.Count; i++)
                    {
                        int next = rawIndices[(i + 1) % rawIndices.Count];
                        if (rawIndices[i] == next)
                        {
                            rawIndices.RemoveAt((i + 1) % rawIndices.Count);
                            modified = true;
                            break;
                        }
                    }
                    if (modified) continue;

                    // Remove backtracking spikes (A -> B -> A)
                    for (int i = 0; i < rawIndices.Count; i++)
                    {
                        int prev = rawIndices[(i - 1 + rawIndices.Count) % rawIndices.Count];
                        int next = rawIndices[(i + 1) % rawIndices.Count];
                        if (prev == next)
                        {
                            rawIndices.RemoveAt(i);
                            modified = true;
                            break;
                        }
                    }
                }

                if (rawIndices.Count < 3) continue;
                int[] polyIndices = rawIndices.ToArray();

                // 3. Triangulate planar polygon using optimal-anchor fan, or ear-clipping if edges are split
                int n = polyIndices.Length;
                if (n == 3)
                {
                    int i0 = polyIndices[0];
                    int i1 = polyIndices[1];
                    int i2 = polyIndices[2];
                    if (i0 != i1 && i1 != i2 && i0 != i2)
                    {
                        Vector3 e1 = outPositions[i1] - outPositions[i0];
                        Vector3 e2 = outPositions[i2] - outPositions[i0];
                        Vector3 norm = Vector3.Cross(e1, e2);
                        if (norm.sqrMagnitude >= CSGTolerance.TriangleAreaEpsilon * CSGTolerance.TriangleAreaEpsilon * 4f)
                        {
                            if (Vector3.Dot(norm, poly.plane.normal) > 0f)
                            {
                                EmitTriangle(i0, i1, i2);
                            }
                            else
                            {
                                EmitTriangle(i0, i2, i1);
                            }
                        }
                    }
                    continue;
                }

                // For n >= 4, first check if any anchor provides all strictly positive-area triangles
                int bestAnchor = -1;
                float bestMinArea = -1f;
                float areaThresholdSqr = CSGTolerance.TriangleAreaEpsilon * CSGTolerance.TriangleAreaEpsilon * 4f;

                for (int a = 0; a < n; a++)
                {
                    float minArea = float.MaxValue;
                    Vector3 pa = outPositions[polyIndices[a]];

                    for (int i = 1; i < n - 1; i++)
                    {
                        Vector3 pb = outPositions[polyIndices[(a + i) % n]];
                        Vector3 pc = outPositions[polyIndices[(a + i + 1) % n]];
                        Vector3 cross = Vector3.Cross(pb - pa, pc - pa);
                        if (Vector3.Dot(cross, poly.plane.normal) <= 0f)
                        {
                            minArea = -1f;
                            break;
                        }
                        float areaSqr = cross.sqrMagnitude;
                        if (areaSqr < minArea) minArea = areaSqr;
                    }

                    if (minArea > areaThresholdSqr && minArea > bestMinArea)
                    {
                        bestMinArea = minArea;
                        bestAnchor = a;
                    }
                }

                if (bestAnchor >= 0)
                {
                    int idx0 = polyIndices[bestAnchor];
                    for (int i = 1; i < n - 1; i++)
                    {
                        int idx1 = polyIndices[(bestAnchor + i) % n];
                        int idx2 = polyIndices[(bestAnchor + i + 1) % n];
                        EmitTriangle(idx0, idx1, idx2);
                    }
                }
                else
                {
                    // Planar ear-clipping triangulation for n-gons with collinear split edges or concavities.
                    // Connects existing vertices without inserting phantom centroid vertices or flipping inverted fan triangles.
                    Vector3 normPlane = poly.plane.normal.normalized;
                    Vector3 uAxis = Mathf.Abs(normPlane.x) > 0.9f
                        ? Vector3.Cross(normPlane, Vector3.up).normalized
                        : Vector3.Cross(normPlane, Vector3.right).normalized;
                    Vector3 vAxis = Vector3.Cross(normPlane, uAxis).normalized;

                    var p2D = new Vector2[n];
                    for (int i = 0; i < n; i++)
                    {
                        Vector3 pos = outPositions[polyIndices[i]];
                        p2D[i] = new Vector2(Vector3.Dot(pos, uAxis), Vector3.Dot(pos, vAxis));
                    }

                    // Compute 2D signed area to ensure counter-clockwise order relative to (uAxis, vAxis)
                    float signedArea = 0f;
                    for (int i = 0; i < n; i++)
                    {
                        Vector2 a = p2D[i];
                        Vector2 b = p2D[(i + 1) % n];
                        signedArea += (a.x * b.y - b.x * a.y);
                    }

                    var vList = new List<int>(n);
                    if (signedArea >= 0f)
                    {
                        for (int i = 0; i < n; i++) vList.Add(i);
                    }
                    else
                    {
                        for (int i = n - 1; i >= 0; i--) vList.Add(i);
                    }

                    int maxIterations = n * 4;
                    int iter = 0;
                    float areaThreshold = CSGTolerance.TriangleAreaEpsilon * 2f;

                    while (vList.Count > 3 && iter++ < maxIterations)
                    {
                        int count = vList.Count;
                        int bestEarIndex = -1;
                        float bestEarCross = 0f;

                        for (int i = 0; i < count; i++)
                        {
                            int prevIdx = vList[(i - 1 + count) % count];
                            int curIdx = vList[i];
                            int nextIdx = vList[(i + 1) % count];

                            Vector2 a = p2D[prevIdx];
                            Vector2 b = p2D[curIdx];
                            Vector2 c = p2D[nextIdx];

                            float cross = (b.x - a.x) * (c.y - b.y) - (b.y - a.y) * (c.x - b.x);
                            if (cross <= areaThreshold) continue;

                            bool containsOther = false;
                            for (int j = 0; j < count; j++)
                            {
                                int testIdx = vList[j];
                                if (testIdx == prevIdx || testIdx == curIdx || testIdx == nextIdx) continue;
                                Vector2 testPt = p2D[testIdx];
                                if (PointInTriangleOrOnDiagonal2D(testPt, a, b, c))
                                {
                                    containsOther = true;
                                    break;
                                }
                            }

                            if (!containsOther)
                            {
                                if (cross > bestEarCross)
                                {
                                    bestEarCross = cross;
                                    bestEarIndex = i;
                                }
                            }
                        }

                        if (bestEarIndex >= 0)
                        {
                            int prevIdx = vList[(bestEarIndex - 1 + count) % count];
                            int curIdx = vList[bestEarIndex];
                            int nextIdx = vList[(bestEarIndex + 1) % count];

                            int idx0 = polyIndices[prevIdx];
                            int idx1 = polyIndices[curIdx];
                            int idx2 = polyIndices[nextIdx];

                            Vector3 norm = Vector3.Cross(outPositions[idx1] - outPositions[idx0], outPositions[idx2] - outPositions[idx0]);
                            if (Vector3.Dot(norm, normPlane) > 0f) EmitTriangle(idx0, idx1, idx2);
                            else EmitTriangle(idx0, idx2, idx1);

                            vList.RemoveAt(bestEarIndex);
                        }
                        else
                        {
                            // Numerical fallback: pick the vertex with largest positive cross product
                            int fallbackIndex = -1;
                            float maxCross = 0f;
                            for (int i = 0; i < count; i++)
                            {
                                int prevIdx = vList[(i - 1 + count) % count];
                                int curIdx = vList[i];
                                int nextIdx = vList[(i + 1) % count];
                                float cross = (p2D[curIdx].x - p2D[prevIdx].x) * (p2D[nextIdx].y - p2D[curIdx].y) -
                                              (p2D[curIdx].y - p2D[prevIdx].y) * (p2D[nextIdx].x - p2D[curIdx].x);
                                if (cross > maxCross)
                                {
                                    maxCross = cross;
                                    fallbackIndex = i;
                                }
                            }

                            if (fallbackIndex >= 0)
                            {
                                int prevIdx = vList[(fallbackIndex - 1 + count) % count];
                                int curIdx = vList[fallbackIndex];
                                int nextIdx = vList[(fallbackIndex + 1) % count];

                                int idx0 = polyIndices[prevIdx];
                                int idx1 = polyIndices[curIdx];
                                int idx2 = polyIndices[nextIdx];

                                Vector3 norm = Vector3.Cross(outPositions[idx1] - outPositions[idx0], outPositions[idx2] - outPositions[idx0]);
                                if (Vector3.Dot(norm, normPlane) > 0f) EmitTriangle(idx0, idx1, idx2);
                                else EmitTriangle(idx0, idx2, idx1);

                                vList.RemoveAt(fallbackIndex);
                            }
                            else
                            {
                                break;
                            }
                        }
                    }

                    if (vList.Count == 3)
                    {
                        int idx0 = polyIndices[vList[0]];
                        int idx1 = polyIndices[vList[1]];
                        int idx2 = polyIndices[vList[2]];

                        Vector3 norm = Vector3.Cross(outPositions[idx1] - outPositions[idx0], outPositions[idx2] - outPositions[idx0]);
                        if (norm.sqrMagnitude >= CSGTolerance.TriangleAreaEpsilon * CSGTolerance.TriangleAreaEpsilon * 4f)
                        {
                            if (Vector3.Dot(norm, normPlane) > 0f) EmitTriangle(idx0, idx1, idx2);
                            else EmitTriangle(idx0, idx2, idx1);
                        }
                    }
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

        private static bool PointInTriangleOrOnDiagonal2D(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float c1 = (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
            float c2 = (c.x - b.x) * (p.y - b.y) - (c.y - b.y) * (p.x - b.x);
            float c3 = (a.x - c.x) * (p.y - c.y) - (a.y - c.y) * (p.x - c.x);

            // Strictly inside the counter-clockwise triangle
            if (c1 > 1e-7f && c2 > 1e-7f && c3 > 1e-7f) return true;

            // Collinear with diagonal (c, a) and between c and a
            if (Mathf.Abs(c3) <= 1e-7f && c1 > 1e-7f && c2 > 1e-7f)
            {
                float dot = (p.x - a.x) * (c.x - a.x) + (p.y - a.y) * (c.y - a.y);
                float lenSqr = (c - a).sqrMagnitude;
                if (dot > 1e-7f && dot < lenSqr - 1e-7f) return true;
            }

            return false;
        }

        #endregion

        #region Strict Mesh Validation

        public static CSGValidationResult ValidateMesh(Mesh mesh, bool allowOpenBoundaries = false)
        {
            var result = new CSGValidationResult
            {
                isValid = false,
                vertexCount = 0,
                triangleCount = 0,
                openEdgeCount = 0,
                nonManifoldEdgeCount = 0,
                degenerateTriangleCount = 0,
                duplicateTriangleCount = 0,
                inconsistentWindingCount = 0,
                tJunctionCount = 0,
                connectedComponentCount = 0,
                hasNonFiniteCoordinates = false,
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

            if (verts.Length < 4 || tris.Length < 12 || (tris.Length % 3 != 0))
            {
                result.message = $"Insufficient geometry for closed solid (vertices: {verts.Length}, triangles: {tris.Length / 3})";
                return result;
            }

            // 1. Check for non-finite coordinates (NaN / Infinity) and out-of-range indices
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 v = verts[i];
                if (float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
                    float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z))
                {
                    result.hasNonFiniteCoordinates = true;
                    result.message = $"Non-finite vertex coordinate detected at index {i}";
                    return result;
                }
            }

            for (int i = 0; i < tris.Length; i++)
            {
                int idx = tris[i];
                if (idx < 0 || idx >= verts.Length)
                {
                    result.message = $"Triangle index {idx} out of range [0, {verts.Length - 1}]";
                    return result;
                }
            }

            // 2. Identify Geometric Vertices (cluster coincident render vertices within scale-aware tolerance)
            float geomTolerance = CSGTolerance.GetScaleAwareDistanceTolerance(mesh.bounds);
            float geomTolSqr = geomTolerance * geomTolerance;
            float geomCellSize = Mathf.Max(geomTolerance * 2f, 0.002f);

            var geomBuckets = new Dictionary<RenderVertexKey, List<int>>();
            int[] remappedGeom = new int[verts.Length];
            var distinctGeomPositions = new List<Vector3>();

            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 p = verts[i];
                RenderVertexKey centerKey = new RenderVertexKey(p, geomCellSize);
                int geomId = -1;

                for (int dx = -1; dx <= 1 && geomId < 0; dx++)
                {
                    for (int dy = -1; dy <= 1 && geomId < 0; dy++)
                    {
                        for (int dz = -1; dz <= 1 && geomId < 0; dz++)
                        {
                            RenderVertexKey testKey = new RenderVertexKey(centerKey.x + dx, centerKey.y + dy, centerKey.z + dz);

                            if (geomBuckets.TryGetValue(testKey, out var bucket))
                            {
                                for (int b = 0; b < bucket.Count; b++)
                                {
                                    int candidateGeomId = bucket[b];
                                    if ((p - distinctGeomPositions[candidateGeomId]).sqrMagnitude <= geomTolSqr)
                                    {
                                        geomId = candidateGeomId;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }

                if (geomId < 0)
                {
                    geomId = distinctGeomPositions.Count;
                    distinctGeomPositions.Add(p);

                    if (!geomBuckets.TryGetValue(centerKey, out var list))
                    {
                        list = new List<int>(4);
                        geomBuckets[centerKey] = list;
                    }
                    list.Add(geomId);
                }

                remappedGeom[i] = geomId;
            }

            // 3. Degenerate Triangles and Duplicate Triangles Analysis
            var seenTriangles = new HashSet<(int, int, int)>();

            for (int t = 0; t < tris.Length; t += 3)
            {
                int g0 = remappedGeom[tris[t]];
                int g1 = remappedGeom[tris[t + 1]];
                int g2 = remappedGeom[tris[t + 2]];

                // Geometric collapse
                if (g0 == g1 || g1 == g2 || g0 == g2)
                {
                    result.degenerateTriangleCount++;
                    continue;
                }

                // Geometric zero-area check
                Vector3 a = distinctGeomPositions[g0];
                Vector3 b = distinctGeomPositions[g1];
                Vector3 c = distinctGeomPositions[g2];
                if (Vector3.Cross(b - a, c - a).sqrMagnitude < (CSGTolerance.TriangleAreaEpsilon * CSGTolerance.TriangleAreaEpsilon * 4f))
                {
                    result.degenerateTriangleCount++;
                    continue;
                }

                // Canonical triangle key for duplicate detection
                int minG = Mathf.Min(g0, Mathf.Min(g1, g2));
                int maxG = Mathf.Max(g0, Mathf.Max(g1, g2));
                int midG = (g0 + g1 + g2) - minG - maxG;
                var triKey = (minG, midG, maxG);

                if (!seenTriangles.Add(triKey))
                {
                    result.duplicateTriangleCount++;
                }
            }

            // 4. Undirected Edge Connectivity & Directed Winding Analysis
            var edgeIncidentHalfEdges = new Dictionary<(int, int), List<(int from, int to, int triIdx)>>();

            for (int t = 0; t < tris.Length; t += 3)
            {
                int g0 = remappedGeom[tris[t]];
                int g1 = remappedGeom[tris[t + 1]];
                int g2 = remappedGeom[tris[t + 2]];

                if (g0 == g1 || g1 == g2 || g0 == g2) continue;

                AddDirectedEdge(edgeIncidentHalfEdges, g0, g1, t);
                AddDirectedEdge(edgeIncidentHalfEdges, g1, g2, t);
                AddDirectedEdge(edgeIncidentHalfEdges, g2, g0, t);
            }

            foreach (var kvp in edgeIncidentHalfEdges)
            {
                var halfEdges = kvp.Value;
                int faceCount = halfEdges.Count;

                if (faceCount == 1)
                {
                    result.openEdgeCount++;
                    if (result.openEdgeCount <= 6)
                    {
                        var e = halfEdges[0];
                        Debug.LogWarning($"[OPEN_EDGE] between {distinctGeomPositions[kvp.Key.Item1]:F4} and {distinctGeomPositions[kvp.Key.Item2]:F4}, incident tri {e.triIdx}: {distinctGeomPositions[remappedGeom[tris[e.triIdx]]]:F4} -> {distinctGeomPositions[remappedGeom[tris[e.triIdx+1]]]:F4} -> {distinctGeomPositions[remappedGeom[tris[e.triIdx+2]]]:F4}");
                    }
                }
                else if (faceCount > 2)
                {
                    result.nonManifoldEdgeCount++;
                    if (result.nonManifoldEdgeCount <= 3)
                    {
                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine($"[CSG_DEBUG] Non-manifold edge between {distinctGeomPositions[kvp.Key.Item1]:F4} and {distinctGeomPositions[kvp.Key.Item2]:F4}, faceCount={faceCount}:");
                        for (int h = 0; h < halfEdges.Count; h++)
                        {
                            int triIdx = halfEdges[h].triIdx;
                            int ta = remappedGeom[tris[triIdx]];
                            int tb = remappedGeom[tris[triIdx + 1]];
                            int tc = remappedGeom[tris[triIdx + 2]];
                            sb.AppendLine($"  Tri {h}: ({distinctGeomPositions[ta]:F4}) -> ({distinctGeomPositions[tb]:F4}) -> ({distinctGeomPositions[tc]:F4})");
                        }
                        Debug.LogWarning(sb.ToString());
                    }
                }
                else if (faceCount == 2)
                {
                    // For a manifold closed solid, the two incident faces must traverse the edge in opposite directions
                    var e1 = halfEdges[0];
                    var e2 = halfEdges[1];
                    if (e1.from == e2.from && e1.to == e2.to)
                    {
                        // Both traverse in the same direction -> inconsistent winding / normal inversion!
                        result.inconsistentWindingCount++;
                    }
                }
            }

            // 5. T-Junction Analysis
            foreach (var kvp in edgeIncidentHalfEdges)
            {
                int edgeV0 = kvp.Key.Item1;
                int edgeV1 = kvp.Key.Item2;
                Vector3 pA = distinctGeomPositions[edgeV0];
                Vector3 pB = distinctGeomPositions[edgeV1];
                Vector3 ab = pB - pA;
                float abLenSqr = ab.sqrMagnitude;
                if (abLenSqr < 1e-8f) continue;

                for (int v = 0; v < distinctGeomPositions.Count; v++)
                {
                    if (v == edgeV0 || v == edgeV1) continue;

                    Vector3 pV = distinctGeomPositions[v];
                    float tParam = Vector3.Dot(pV - pA, ab) / abLenSqr;
                    if (tParam > 0.02f && tParam < 0.98f) // Strictly interior
                    {
                        Vector3 proj = pA + tParam * ab;
                        if ((pV - proj).sqrMagnitude <= geomTolSqr)
                        {
                            result.tJunctionCount++;
                        }
                    }
                }
            }

            // 6. Connected Components Count (via face adjacency)
            result.connectedComponentCount = CountConnectedComponents(tris, remappedGeom);

            // 7. Strict Acceptance Criteria
            bool openEdgesAcceptable = allowOpenBoundaries || result.openEdgeCount == 0;
            bool isCleanSolid = !result.hasNonFiniteCoordinates &&
                               openEdgesAcceptable &&
                               result.nonManifoldEdgeCount == 0 &&
                               result.degenerateTriangleCount == 0 &&
                               result.duplicateTriangleCount == 0 &&
                               result.inconsistentWindingCount == 0 &&
                               result.triangleCount >= 4;

            result.isValid = isCleanSolid;

            if (result.isValid)
            {
                result.message = $"Mesh valid closed solid ({result.triangleCount} tris, {result.connectedComponentCount} component(s))";
            }
            else
            {
                var defects = new List<string>();
                if (result.hasNonFiniteCoordinates) defects.Add("non-finite coordinates");
                if (result.openEdgeCount > 0) defects.Add($"{result.openEdgeCount} boundary/open edges");
                if (result.nonManifoldEdgeCount > 0) defects.Add($"{result.nonManifoldEdgeCount} non-manifold edges");
                if (result.degenerateTriangleCount > 0) defects.Add($"{result.degenerateTriangleCount} degenerate triangles");
                if (result.duplicateTriangleCount > 0) defects.Add($"{result.duplicateTriangleCount} duplicate triangles");
                if (result.inconsistentWindingCount > 0) defects.Add($"{result.inconsistentWindingCount} inconsistent winding edges");
                if (result.tJunctionCount > 0) defects.Add($"{result.tJunctionCount} T-junctions");
                result.message = "Boolean validation failed: " + string.Join(", ", defects);
            }

            return result;
        }

        private static void AddDirectedEdge(Dictionary<(int, int), List<(int from, int to, int triIdx)>> edgeDict, int from, int to, int triIdx)
        {
            var undirectedKey = from < to ? (from, to) : (to, from);
            if (!edgeDict.TryGetValue(undirectedKey, out var list))
            {
                list = new List<(int from, int to, int triIdx)>(2);
                edgeDict[undirectedKey] = list;
            }
            list.Add((from, to, triIdx));
        }

        private static int CountConnectedComponents(int[] tris, int[] remappedGeom)
        {
            int triCount = tris.Length / 3;
            if (triCount == 0) return 0;

            // Map each geometric vertex to incident triangles
            var vertToTris = new Dictionary<int, List<int>>();
            for (int t = 0; t < triCount; t++)
            {
                for (int c = 0; c < 3; c++)
                {
                    int g = remappedGeom[tris[t * 3 + c]];
                    if (!vertToTris.TryGetValue(g, out var list))
                    {
                        list = new List<int>();
                        vertToTris[g] = list;
                    }
                    list.Add(t);
                }
            }

            var visitedTris = new bool[triCount];
            int components = 0;

            for (int t = 0; t < triCount; t++)
            {
                if (visitedTris[t]) continue;

                components++;
                var queue = new Queue<int>();
                queue.Enqueue(t);
                visitedTris[t] = true;

                while (queue.Count > 0)
                {
                    int curTri = queue.Dequeue();
                    for (int c = 0; c < 3; c++)
                    {
                        int g = remappedGeom[tris[curTri * 3 + c]];
                        if (vertToTris.TryGetValue(g, out var neighbors))
                        {
                            for (int n = 0; n < neighbors.Count; n++)
                            {
                                int nbr = neighbors[n];
                                if (!visitedTris[nbr])
                                {
                                    visitedTris[nbr] = true;
                                    queue.Enqueue(nbr);
                                }
                            }
                        }
                    }
                }
            }

            return components;
        }

        #endregion
    }
}
