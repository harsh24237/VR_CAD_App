using System.Collections.Generic;
using UnityEngine;

namespace VRCAD.Core
{
    public class CAD_ShapeManager : MonoBehaviour
    {
        [Header("Shape Materials & Settings")]
        [SerializeField] private Material defaultCadMaterial;
        [SerializeField] private Transform shapesRoot;

        private readonly List<CADObject> registeredObjects = new List<CADObject>();

        public IReadOnlyList<CADObject> RegisteredObjects => registeredObjects;

        private void Awake()
        {
            if (shapesRoot == null)
            {
                GameObject root = new GameObject("CAD_Geometry_Root");
                shapesRoot = root.transform;
            }
        }

        public CADObject SpawnPrimitive(CADShapeType shapeType, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            GameObject obj = new GameObject($"CAD_{shapeType}_{registeredObjects.Count + 1}");
            obj.transform.SetParent(shapesRoot);
            obj.transform.position = position;
            obj.transform.rotation = rotation;
            obj.transform.localScale = scale;

            Mesh mesh = GeneratePrimitiveMesh(shapeType);
            
            CADObject cadObj = obj.AddComponent<CADObject>();
            cadObj.ShapeType = shapeType;
            cadObj.Dimensions = scale;
            cadObj.SetMesh(mesh);

            registeredObjects.Add(cadObj);
            CADManagerHub.Instance?.OnShapeCreated(cadObj);

            return cadObj;
        }

        public void DeleteObject(CADObject cadObject)
        {
            if (cadObject == null) return;

            if (registeredObjects.Contains(cadObject))
            {
                registeredObjects.Remove(cadObject);
            }

            CADManagerHub.Instance?.OnShapeDeleted(cadObject);
            Destroy(cadObject.gameObject);
        }

        public void ClearAll()
        {
            for (int i = registeredObjects.Count - 1; i >= 0; i--)
            {
                if (registeredObjects[i] != null)
                {
                    Destroy(registeredObjects[i].gameObject);
                }
            }
            registeredObjects.Clear();
        }

        #region Procedural Mesh Generators

        public Mesh GeneratePrimitiveMesh(CADShapeType type)
        {
            return type switch
            {
                CADShapeType.Box => CreateBoxMesh(1f, 1f, 1f),
                CADShapeType.Cylinder => CreateCylinderMesh(0.5f, 1f, 24),
                CADShapeType.Sphere => CreateSphereMesh(0.5f, 24, 16),
                CADShapeType.Cone => CreateConeMesh(0.5f, 1f, 24),
                CADShapeType.Prism => CreateTriangularPrismMesh(1f, 1f, 1f),
                CADShapeType.Torus => CreateTorusMesh(0.5f, 0.15f, 24, 16),
                _ => CreateBoxMesh(1f, 1f, 1f)
            };
        }

        public static Mesh CreateBoxMesh(float width, float height, float depth)
        {
            Mesh mesh = new Mesh { name = "Procedural_Box" };
            float w = width * 0.5f;
            float h = height * 0.5f;
            float d = depth * 0.5f;

            Vector3[] vertices = new Vector3[]
            {
                // Front
                new Vector3(-w, -h,  d), new Vector3( w, -h,  d), new Vector3( w,  h,  d), new Vector3(-w,  h,  d),
                // Back
                new Vector3( w, -h, -d), new Vector3(-w, -h, -d), new Vector3(-w,  h, -d), new Vector3( w,  h, -d),
                // Top
                new Vector3(-w,  h,  d), new Vector3( w,  h,  d), new Vector3( w,  h, -d), new Vector3(-w,  h, -d),
                // Bottom
                new Vector3(-w, -h, -d), new Vector3( w, -h, -d), new Vector3( w, -h,  d), new Vector3(-w, -h,  d),
                // Left
                new Vector3(-w, -h, -d), new Vector3(-w, -h,  d), new Vector3(-w,  h,  d), new Vector3(-w,  h, -d),
                // Right
                new Vector3( w, -h,  d), new Vector3( w, -h, -d), new Vector3( w,  h, -d), new Vector3( w,  h,  d)
            };

            Vector3[] normals = new Vector3[]
            {
                Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward,
                Vector3.back, Vector3.back, Vector3.back, Vector3.back,
                Vector3.up, Vector3.up, Vector3.up, Vector3.up,
                Vector3.down, Vector3.down, Vector3.down, Vector3.down,
                Vector3.left, Vector3.left, Vector3.left, Vector3.left,
                Vector3.right, Vector3.right, Vector3.right, Vector3.right
            };

            Vector2[] uvs = new Vector2[24];
            for (int i = 0; i < 6; i++)
            {
                uvs[i * 4 + 0] = new Vector2(0, 0);
                uvs[i * 4 + 1] = new Vector2(1, 0);
                uvs[i * 4 + 2] = new Vector2(1, 1);
                uvs[i * 4 + 3] = new Vector2(0, 1);
            }

            int[] triangles = new int[]
            {
                0, 1, 2, 0, 2, 3,       // Front
                4, 5, 6, 4, 6, 7,       // Back
                8, 9, 10, 8, 10, 11,    // Top
                12, 13, 14, 12, 14, 15, // Bottom
                16, 17, 18, 16, 18, 19, // Left
                20, 21, 22, 20, 22, 23  // Right
            };

            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Mesh CreateCylinderMesh(float radius, float height, int segments)
        {
            Mesh mesh = new Mesh { name = "Procedural_Cylinder" };
            List<Vector3> verts = new List<Vector3>();
            List<Vector3> norms = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> tris = new List<int>();

            float halfH = height * 0.5f;

            // Side wall
            for (int i = 0; i <= segments; i++)
            {
                float angle = (float)i / segments * Mathf.PI * 2f;
                float x = Mathf.Cos(angle) * radius;
                float z = Mathf.Sin(angle) * radius;

                Vector3 norm = new Vector3(x, 0, z).normalized;

                verts.Add(new Vector3(x, -halfH, z));
                verts.Add(new Vector3(x, halfH, z));
                norms.Add(norm);
                norms.Add(norm);
                uvs.Add(new Vector2((float)i / segments, 0));
                uvs.Add(new Vector2((float)i / segments, 1));
            }

            for (int i = 0; i < segments; i++)
            {
                int baseIdx = i * 2;
                tris.Add(baseIdx);
                tris.Add(baseIdx + 1);
                tris.Add(baseIdx + 2);

                tris.Add(baseIdx + 1);
                tris.Add(baseIdx + 3);
                tris.Add(baseIdx + 2);
            }

            // Top Cap
            int topCenterIdx = verts.Count;
            verts.Add(new Vector3(0, halfH, 0));
            norms.Add(Vector3.up);
            uvs.Add(new Vector2(0.5f, 0.5f));

            int topRingStart = verts.Count;
            for (int i = 0; i < segments; i++)
            {
                float angle = (float)i / segments * Mathf.PI * 2f;
                float x = Mathf.Cos(angle) * radius;
                float z = Mathf.Sin(angle) * radius;
                verts.Add(new Vector3(x, halfH, z));
                norms.Add(Vector3.up);
                uvs.Add(new Vector2(x / (radius * 2f) + 0.5f, z / (radius * 2f) + 0.5f));
            }

            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                tris.Add(topCenterIdx);
                tris.Add(topRingStart + i);
                tris.Add(topRingStart + next);
            }

            // Bottom Cap
            int bottomCenterIdx = verts.Count;
            verts.Add(new Vector3(0, -halfH, 0));
            norms.Add(Vector3.down);
            uvs.Add(new Vector2(0.5f, 0.5f));

            int botRingStart = verts.Count;
            for (int i = 0; i < segments; i++)
            {
                float angle = (float)i / segments * Mathf.PI * 2f;
                float x = Mathf.Cos(angle) * radius;
                float z = Mathf.Sin(angle) * radius;
                verts.Add(new Vector3(x, -halfH, z));
                norms.Add(Vector3.down);
                uvs.Add(new Vector2(x / (radius * 2f) + 0.5f, z / (radius * 2f) + 0.5f));
            }

            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                tris.Add(bottomCenterIdx);
                tris.Add(botRingStart + next);
                tris.Add(botRingStart + i);
            }

            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Mesh CreateSphereMesh(float radius, int longitudeSegments, int latitudeSegments)
        {
            Mesh mesh = new Mesh { name = "Procedural_Sphere" };
            List<Vector3> verts = new List<Vector3>();
            List<Vector3> norms = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> tris = new List<int>();

            for (int lat = 0; lat <= latitudeSegments; lat++)
            {
                float theta = lat * Mathf.PI / latitudeSegments;
                float sinTheta = Mathf.Sin(theta);
                float cosTheta = Mathf.Cos(theta);

                for (int lon = 0; lon <= longitudeSegments; lon++)
                {
                    float phi = lon * 2 * Mathf.PI / longitudeSegments;
                    float sinPhi = Mathf.Sin(phi);
                    float cosPhi = Mathf.Cos(phi);

                    Vector3 norm = new Vector3(cosPhi * sinTheta, cosTheta, sinPhi * sinTheta);
                    verts.Add(norm * radius);
                    norms.Add(norm);
                    uvs.Add(new Vector2((float)lon / longitudeSegments, (float)lat / latitudeSegments));
                }
            }

            for (int lat = 0; lat < latitudeSegments; lat++)
            {
                for (int lon = 0; lon < longitudeSegments; lon++)
                {
                    int first = lat * (longitudeSegments + 1) + lon;
                    int second = first + longitudeSegments + 1;

                    tris.Add(first);
                    tris.Add(second);
                    tris.Add(first + 1);

                    tris.Add(second);
                    tris.Add(second + 1);
                    tris.Add(first + 1);
                }
            }

            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Mesh CreateConeMesh(float radius, float height, int segments)
        {
            Mesh mesh = new Mesh { name = "Procedural_Cone" };
            List<Vector3> verts = new List<Vector3>();
            List<Vector3> norms = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> tris = new List<int>();

            float halfH = height * 0.5f;

            // Apex
            Vector3 apex = new Vector3(0, halfH, 0);

            // Side Triangles
            for (int i = 0; i < segments; i++)
            {
                float a1 = (float)i / segments * Mathf.PI * 2f;
                float a2 = (float)(i + 1) / segments * Mathf.PI * 2f;

                Vector3 p1 = new Vector3(Mathf.Cos(a1) * radius, -halfH, Mathf.Sin(a1) * radius);
                Vector3 p2 = new Vector3(Mathf.Cos(a2) * radius, -halfH, Mathf.Sin(a2) * radius);

                Vector3 normal = Vector3.Cross(p2 - apex, p1 - apex).normalized;

                int idx = verts.Count;
                verts.Add(apex);
                verts.Add(p1);
                verts.Add(p2);

                norms.Add(normal);
                norms.Add(normal);
                norms.Add(normal);

                uvs.Add(new Vector2(0.5f, 1f));
                uvs.Add(new Vector2((float)i / segments, 0f));
                uvs.Add(new Vector2((float)(i + 1) / segments, 0f));

                tris.Add(idx);
                tris.Add(idx + 1);
                tris.Add(idx + 2);
            }

            // Bottom Base
            int baseCenterIdx = verts.Count;
            verts.Add(new Vector3(0, -halfH, 0));
            norms.Add(Vector3.down);
            uvs.Add(new Vector2(0.5f, 0.5f));

            int baseRingStart = verts.Count;
            for (int i = 0; i < segments; i++)
            {
                float a = (float)i / segments * Mathf.PI * 2f;
                Vector3 p = new Vector3(Mathf.Cos(a) * radius, -halfH, Mathf.Sin(a) * radius);
                verts.Add(p);
                norms.Add(Vector3.down);
                uvs.Add(new Vector2(p.x / (radius * 2f) + 0.5f, p.z / (radius * 2f) + 0.5f));
            }

            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                tris.Add(baseCenterIdx);
                tris.Add(baseRingStart + next);
                tris.Add(baseRingStart + i);
            }

            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Mesh CreateTriangularPrismMesh(float width, float height, float length)
        {
            Mesh mesh = new Mesh { name = "Procedural_Prism" };
            float halfW = width * 0.5f;
            float halfH = height * 0.5f;
            float halfL = length * 0.5f;

            // 6 vertices defining the triangular prism
            Vector3 v0 = new Vector3(-halfW, -halfH, -halfL);
            Vector3 v1 = new Vector3( halfW, -halfH, -halfL);
            Vector3 v2 = new Vector3(     0,  halfH, -halfL);

            Vector3 v3 = new Vector3(-halfW, -halfH,  halfL);
            Vector3 v4 = new Vector3( halfW, -halfH,  halfL);
            Vector3 v5 = new Vector3(     0,  halfH,  halfL);

            List<Vector3> verts = new List<Vector3>();
            List<Vector3> norms = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> tris = new List<int>();

            void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                int start = verts.Count;
                verts.AddRange(new[] { a, b, c, d });
                norms.AddRange(new[] { n, n, n, n });
                uvs.AddRange(new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
                tris.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            }

            void AddTri(Vector3 a, Vector3 b, Vector3 c)
            {
                Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                int start = verts.Count;
                verts.AddRange(new[] { a, b, c });
                norms.AddRange(new[] { n, n, n });
                uvs.AddRange(new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 1) });
                tris.AddRange(new[] { start, start + 1, start + 2 });
            }

            // Bottom Quad
            AddQuad(v3, v4, v1, v0);
            // Front Triangle
            AddTri(v3, v5, v4);
            // Back Triangle
            AddTri(v0, v1, v2);
            // Left Slope Quad
            AddQuad(v0, v2, v5, v3);
            // Right Slope Quad
            AddQuad(v1, v4, v5, v2);

            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Mesh CreateTorusMesh(float mainRadius, float tubeRadius, int mainSegments, int tubeSegments)
        {
            Mesh mesh = new Mesh { name = "Procedural_Torus" };
            List<Vector3> verts = new List<Vector3>();
            List<Vector3> norms = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> tris = new List<int>();

            for (int i = 0; i <= mainSegments; i++)
            {
                float u = (float)i / mainSegments * Mathf.PI * 2f;
                Vector3 center = new Vector3(Mathf.Cos(u) * mainRadius, 0, Mathf.Sin(u) * mainRadius);

                for (int j = 0; j <= tubeSegments; j++)
                {
                    float v = (float)j / tubeSegments * Mathf.PI * 2f;
                    Vector3 normal = new Vector3(Mathf.Cos(u) * Mathf.Cos(v), Mathf.Sin(v), Mathf.Sin(u) * Mathf.Cos(v));
                    Vector3 pos = center + normal * tubeRadius;

                    verts.Add(pos);
                    norms.Add(normal);
                    uvs.Add(new Vector2((float)i / mainSegments, (float)j / tubeSegments));
                }
            }

            for (int i = 0; i < mainSegments; i++)
            {
                for (int j = 0; j < tubeSegments; j++)
                {
                    int current = i * (tubeSegments + 1) + j;
                    int next = (i + 1) * (tubeSegments + 1) + j;

                    tris.Add(current);
                    tris.Add(next);
                    tris.Add(current + 1);

                    tris.Add(next);
                    tris.Add(next + 1);
                    tris.Add(current + 1);
                }
            }

            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        #endregion
    }
}
