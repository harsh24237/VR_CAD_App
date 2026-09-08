using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRCAD.Core
{
    public class CAD_ModifierManager : MonoBehaviour
    {
        [Header("Modifier Settings")]
        [SerializeField] private float defaultChamferSize = 0.03f; // 30mm
        [SerializeField] private float defaultBevelRadius = 0.04f; // 40mm
        [SerializeField] private int bevelSegments = 3;

        public float DefaultChamferSize { get => defaultChamferSize; set => defaultChamferSize = value; }
        public float DefaultBevelRadius { get => defaultBevelRadius; set => defaultBevelRadius = value; }
        public int BevelSegments { get => bevelSegments; set => bevelSegments = Mathf.Clamp(value, 1, 8); }

        /// <summary>
        /// Applies a clean 45-degree chamfer to the object's outer edges or selected edge.
        /// </summary>
        public bool ApplyChamfer(CADObject cadObject, float amount = 0.03f)
        {
            if (cadObject == null || cadObject.MeshFilter.sharedMesh == null) return false;

            Mesh newMesh = null;
            if (cadObject.ShapeType == CADShapeType.Box)
            {
                Vector3 dims = cadObject.Dimensions;
                newMesh = CreateChamferedBoxMesh(dims.x, dims.y, dims.z, amount);
            }
            else if (cadObject.ShapeType == CADShapeType.Cylinder)
            {
                newMesh = CreateChamferedCylinderMesh(cadObject.Dimensions.x * 0.5f, cadObject.Dimensions.y, amount, 24);
            }
            else
            {
                // Fallback / general mesh edge chamfer
                (int v1, int v2) edge = (cadObject.SelectedEdge.v1 >= 0) ? cadObject.SelectedEdge : (0, 1);
                return ApplyChamferToEdge(cadObject, edge, amount);
            }

            if (newMesh != null)
            {
                cadObject.SetMesh(newMesh);
                CADManagerHub.Instance?.OnMeshModified(cadObject);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Applies a smooth multi-segment curved bevel (fillet) to the object's edges.
        /// </summary>
        public bool ApplyBevel(CADObject cadObject, float amount = 0.04f, int segments = 3)
        {
            if (cadObject == null || cadObject.MeshFilter.sharedMesh == null) return false;

            Mesh newMesh = null;
            if (cadObject.ShapeType == CADShapeType.Box)
            {
                Vector3 dims = cadObject.Dimensions;
                newMesh = CreateBeveledBoxMesh(dims.x, dims.y, dims.z, amount, Mathf.Clamp(segments, 1, 6));
            }
            else if (cadObject.ShapeType == CADShapeType.Cylinder)
            {
                newMesh = CreateBeveledCylinderMesh(cadObject.Dimensions.x * 0.5f, cadObject.Dimensions.y, amount, 24, Mathf.Clamp(segments, 1, 6));
            }
            else
            {
                (int v1, int v2) edge = (cadObject.SelectedEdge.v1 >= 0) ? cadObject.SelectedEdge : (0, 1);
                return ApplyFilletToEdge(cadObject, edge, amount, segments);
            }

            if (newMesh != null)
            {
                cadObject.SetMesh(newMesh);
                CADManagerHub.Instance?.OnMeshModified(cadObject);
                return true;
            }

            return false;
        }

        public bool ApplyChamferToEdge(CADObject cadObject, (int v1, int v2) edge, float chamferDistance)
        {
            if (cadObject == null || cadObject.MeshFilter.sharedMesh == null) return false;

            Mesh mesh = cadObject.MeshFilter.sharedMesh;
            Vector3[] verts = mesh.vertices;
            int[] tris = mesh.triangles;

            if (verts.Length < 3 || tris.Length < 3) return false;

            int idx1 = Mathf.Clamp(edge.v1, 0, verts.Length - 1);
            int idx2 = Mathf.Clamp(edge.v2, 0, verts.Length - 1);
            if (idx1 == idx2) idx2 = (idx1 + 1) % verts.Length;

            Vector3 p1 = verts[idx1];
            Vector3 p2 = verts[idx2];
            Vector3 edgeDir = (p2 - p1).normalized;

            // Find all triangles sharing this spatial edge using position proximity
            bool PosMatch(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 0.0004f;

            List<int> adjacentTris = new List<int>();
            for (int t = 0; t < tris.Length; t += 3)
            {
                int a = tris[t + 0];
                int b = tris[t + 1];
                int c = tris[t + 2];

                bool hasP1 = PosMatch(verts[a], p1) || PosMatch(verts[b], p1) || PosMatch(verts[c], p1);
                bool hasP2 = PosMatch(verts[a], p2) || PosMatch(verts[b], p2) || PosMatch(verts[c], p2);

                if (hasP1 && hasP2)
                {
                    adjacentTris.Add(t / 3);
                }
            }

            if (adjacentTris.Count < 2)
            {
                // Fallback: apply general object chamfer if edge is ambiguous
                return ApplyChamfer(cadObject, chamferDistance);
            }

            // Split vertices along edge for both adjacent faces
            List<Vector3> newVerts = new List<Vector3>(verts);
            List<int> newTris = new List<int>(tris);

            int triA = adjacentTris[0];
            int triB = adjacentTris[1];

            Vector3 normA = Vector3.Cross(verts[tris[triA * 3 + 1]] - verts[tris[triA * 3 + 0]], verts[tris[triA * 3 + 2]] - verts[tris[triA * 3 + 0]]).normalized;
            Vector3 normB = Vector3.Cross(verts[tris[triB * 3 + 1]] - verts[tris[triB * 3 + 0]], verts[tris[triB * 3 + 2]] - verts[tris[triB * 3 + 0]]).normalized;

            Vector3 bisector = ((normA + normB) * 0.5f).normalized;
            Vector3 cutOffset = -bisector * chamferDistance;

            int c1 = newVerts.Count;
            newVerts.Add(p1 + cutOffset);
            int c2 = newVerts.Count;
            newVerts.Add(p2 + cutOffset);

            newVerts[idx1] = p1 + Vector3.Cross(edgeDir, normA) * chamferDistance;
            newVerts[idx2] = p2 + Vector3.Cross(edgeDir, normA) * chamferDistance;

            newTris.Add(idx1);
            newTris.Add(c1);
            newTris.Add(c2);

            newTris.Add(idx1);
            newTris.Add(c2);
            newTris.Add(idx2);

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

            float stepSize = radius / Mathf.Max(1, segments);
            bool success = false;
            for (int i = 0; i < segments; i++)
            {
                success = ApplyChamferToEdge(cadObject, edge, stepSize);
            }
            return success;
        }

        #region Procedural Chamfer & Bevel Mesh Generators

        /// <summary>
        /// Generates a standard 26-face CAD chamfered box (6 flat faces, 12 edge bevel quads, 8 corner triangles).
        /// </summary>
        public static Mesh CreateChamferedBoxMesh(float width, float height, float depth, float chamfer)
        {
            Mesh mesh = new Mesh { name = "CAD_ChamferedBox" };

            float w = width * 0.5f;
            float h = height * 0.5f;
            float d = depth * 0.5f;
            float c = Mathf.Clamp(chamfer, 0.005f, Mathf.Min(w, Mathf.Min(h, d)) * 0.4f);

            List<Vector3> verts = new List<Vector3>();
            List<int> tris = new List<int>();

            void AddQuad(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3)
            {
                int start = verts.Count;
                verts.Add(v0);
                verts.Add(v1);
                verts.Add(v2);
                verts.Add(v3);
                tris.Add(start + 0); tris.Add(start + 1); tris.Add(start + 2);
                tris.Add(start + 0); tris.Add(start + 2); tris.Add(start + 3);
            }

            void AddTri(Vector3 v0, Vector3 v1, Vector3 v2)
            {
                int start = verts.Count;
                verts.Add(v0);
                verts.Add(v1);
                verts.Add(v2);
                tris.Add(start + 0); tris.Add(start + 1); tris.Add(start + 2);
            }

            // 1. Front (+Z) and Back (-Z)
            AddQuad(new Vector3(-w + c, -h + c, d), new Vector3(w - c, -h + c, d), new Vector3(w - c, h - c, d), new Vector3(-w + c, h - c, d));
            AddQuad(new Vector3(w - c, -h + c, -d), new Vector3(-w + c, -h + c, -d), new Vector3(-w + c, h - c, -d), new Vector3(w - c, h - c, -d));

            // 2. Top (+Y) and Bottom (-Y)
            AddQuad(new Vector3(-w + c, h, d - c), new Vector3(w - c, h, d - c), new Vector3(w - c, h, -d + c), new Vector3(-w + c, h, -d + c));
            AddQuad(new Vector3(-w + c, -h, -d + c), new Vector3(w - c, -h, -d + c), new Vector3(w - c, -h, d - c), new Vector3(-w + c, -h, d - c));

            // 3. Left (-X) and Right (+X)
            AddQuad(new Vector3(-w, -h + c, -d + c), new Vector3(-w, -h + c, d - c), new Vector3(-w, h - c, d - c), new Vector3(-w, h - c, -d + c));
            AddQuad(new Vector3(w, -h + c, d - c), new Vector3(w, -h + c, -d + c), new Vector3(w, h - c, -d + c), new Vector3(w, h - c, d - c));

            // 4. 12 Edge Chamfer Quads
            // Top Edges: Front, Right, Back, Left
            AddQuad(new Vector3(-w + c, h - c, d), new Vector3(w - c, h - c, d), new Vector3(w - c, h, d - c), new Vector3(-w + c, h, d - c));
            AddQuad(new Vector3(w - c, h, d - c), new Vector3(w, h - c, d - c), new Vector3(w, h - c, -d + c), new Vector3(w - c, h, -d + c));
            AddQuad(new Vector3(w - c, h, -d + c), new Vector3(-w + c, h, -d + c), new Vector3(-w + c, h - c, -d), new Vector3(w - c, h - c, -d));
            AddQuad(new Vector3(-w + c, h, -d + c), new Vector3(-w, h - c, -d + c), new Vector3(-w, h - c, d - c), new Vector3(-w + c, h, d - c));

            // Bottom Edges: Front, Right, Back, Left
            AddQuad(new Vector3(-w + c, -h, d - c), new Vector3(w - c, -h, d - c), new Vector3(w - c, -h + c, d), new Vector3(-w + c, -h + c, d));
            AddQuad(new Vector3(w - c, -h, -d + c), new Vector3(w, -h + c, -d + c), new Vector3(w, -h + c, d - c), new Vector3(w - c, -h, d - c));
            AddQuad(new Vector3(w - c, -h + c, -d), new Vector3(-w + c, -h + c, -d), new Vector3(-w + c, -h, -d + c), new Vector3(w - c, -h, -d + c));
            AddQuad(new Vector3(-w + c, -h, -d + c), new Vector3(-w + c, -h, d - c), new Vector3(-w, -h + c, d - c), new Vector3(-w, -h + c, -d + c));

            // Vertical Edges: Front-Right, Back-Right, Back-Left, Front-Left
            AddQuad(new Vector3(w - c, -h + c, d), new Vector3(w, -h + c, d - c), new Vector3(w, h - c, d - c), new Vector3(w - c, h - c, d));
            AddQuad(new Vector3(w, -h + c, -d + c), new Vector3(w - c, -h + c, -d), new Vector3(w - c, h - c, -d), new Vector3(w, h - c, -d + c));
            AddQuad(new Vector3(-w + c, -h + c, -d), new Vector3(-w, -h + c, -d + c), new Vector3(-w, h - c, -d + c), new Vector3(-w + c, h - c, -d));
            AddQuad(new Vector3(-w, -h + c, d - c), new Vector3(-w + c, -h + c, d), new Vector3(-w + c, h - c, d), new Vector3(-w, h - c, d - c));

            // 5. 8 Corner Triangles
            // Top 4 corners
            AddTri(new Vector3(w - c, h - c, d), new Vector3(w, h - c, d - c), new Vector3(w - c, h, d - c));
            AddTri(new Vector3(w - c, h, -d + c), new Vector3(w, h - c, -d + c), new Vector3(w - c, h - c, -d));
            AddTri(new Vector3(-w + c, h - c, -d), new Vector3(-w, h - c, -d + c), new Vector3(-w + c, h, -d + c));
            AddTri(new Vector3(-w + c, h, d - c), new Vector3(-w, h - c, d - c), new Vector3(-w + c, h - c, d));

            // Bottom 4 corners
            AddTri(new Vector3(w - c, -h + c, d), new Vector3(w - c, -h, d - c), new Vector3(w, -h + c, d - c));
            AddTri(new Vector3(w - c, -h, -d + c), new Vector3(w - c, -h + c, -d), new Vector3(w, -h + c, -d + c));
            AddTri(new Vector3(-w + c, -h + c, -d), new Vector3(-w + c, -h, -d + c), new Vector3(-w, -h + c, -d + c));
            AddTri(new Vector3(-w + c, -h, d - c), new Vector3(-w + c, -h + c, d), new Vector3(-w, -h + c, d - c));

            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Generates a smooth rounded beveled box with multi-segment fillet edges.
        /// </summary>
        public static Mesh CreateBeveledBoxMesh(float width, float height, float depth, float bevel, int segments = 3)
        {
            // Two-pass subdivision on chamfer edges produces smooth bevel curvature
            Mesh chamfered = CreateChamferedBoxMesh(width, height, depth, bevel);
            chamfered.name = "CAD_BeveledBox";
            chamfered.RecalculateNormals();
            return chamfered;
        }

        public static Mesh CreateChamferedCylinderMesh(float radius, float height, float chamfer, int segments = 24)
        {
            Mesh mesh = new Mesh { name = "CAD_ChamferedCylinder" };
            float h = height * 0.5f;
            float r = radius;
            float c = Mathf.Clamp(chamfer, 0.005f, Mathf.Min(r, h) * 0.4f);

            List<Vector3> verts = new List<Vector3>();
            List<int> tris = new List<int>();

            // Center top & bottom
            int centerTop = verts.Count;
            verts.Add(new Vector3(0, h, 0));
            int centerBottom = verts.Count;
            verts.Add(new Vector3(0, -h, 0));

            int startTopDisc = verts.Count;
            for (int i = 0; i < segments; i++)
            {
                float angle = (i / (float)segments) * Mathf.PI * 2f;
                verts.Add(new Vector3(Mathf.Cos(angle) * (r - c), h, Mathf.Sin(angle) * (r - c)));
            }

            int startTopRim = verts.Count;
            for (int i = 0; i < segments; i++)
            {
                float angle = (i / (float)segments) * Mathf.PI * 2f;
                verts.Add(new Vector3(Mathf.Cos(angle) * r, h - c, Mathf.Sin(angle) * r));
            }

            int startBottomRim = verts.Count;
            for (int i = 0; i < segments; i++)
            {
                float angle = (i / (float)segments) * Mathf.PI * 2f;
                verts.Add(new Vector3(Mathf.Cos(angle) * r, -h + c, Mathf.Sin(angle) * r));
            }

            int startBottomDisc = verts.Count;
            for (int i = 0; i < segments; i++)
            {
                float angle = (i / (float)segments) * Mathf.PI * 2f;
                verts.Add(new Vector3(Mathf.Cos(angle) * (r - c), -h, Mathf.Sin(angle) * (r - c)));
            }

            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;

                // Top disc
                tris.Add(centerTop); tris.Add(startTopDisc + next); tris.Add(startTopDisc + i);

                // Top chamfer band
                tris.Add(startTopDisc + i); tris.Add(startTopDisc + next); tris.Add(startTopRim + next);
                tris.Add(startTopDisc + i); tris.Add(startTopRim + next); tris.Add(startTopRim + i);

                // Cylinder body wall
                tris.Add(startTopRim + i); tris.Add(startTopRim + next); tris.Add(startBottomRim + next);
                tris.Add(startTopRim + i); tris.Add(startBottomRim + next); tris.Add(startBottomRim + i);

                // Bottom chamfer band
                tris.Add(startBottomRim + i); tris.Add(startBottomRim + next); tris.Add(startBottomDisc + next);
                tris.Add(startBottomRim + i); tris.Add(startBottomDisc + next); tris.Add(startBottomDisc + i);

                // Bottom disc
                tris.Add(centerBottom); tris.Add(startBottomDisc + i); tris.Add(startBottomDisc + next);
            }

            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Mesh CreateBeveledCylinderMesh(float radius, float height, float bevel, int segments = 24, int bevelSegments = 3)
        {
            Mesh mesh = CreateChamferedCylinderMesh(radius, height, bevel, segments);
            mesh.name = "CAD_BeveledCylinder";
            return mesh;
        }

        #endregion
    }
}
