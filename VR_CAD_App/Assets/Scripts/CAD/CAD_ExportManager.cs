using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace VRCAD.Core
{
    public class CAD_ExportManager : MonoBehaviour
    {
        [Header("Export Directory")]
        [SerializeField] private string customExportPath;

        public string GetExportDirectory()
        {
            string path = !string.IsNullOrEmpty(customExportPath) ? customExportPath : Path.Combine(Application.persistentDataPath, "CAD_Exports");
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            return path;
        }

        public string ExportToSTL(CADObject cadObject, bool binary = true)
        {
            if (cadObject == null || cadObject.MeshFilter.sharedMesh == null) return null;

            string filename = $"{cadObject.name}_{DateTime.Now:yyyyMMdd_HHmmss}.stl";
            string fullPath = Path.Combine(GetExportDirectory(), filename);

            Mesh mesh = cadObject.MeshFilter.sharedMesh;
            Transform t = cadObject.transform;

            if (binary)
            {
                WriteBinarySTL(fullPath, mesh, t);
            }
            else
            {
                WriteAsciiSTL(fullPath, mesh, t);
            }

            Debug.Log($"[CAD_ExportManager] Exported STL to: {fullPath}");
            return fullPath;
        }

        public string ExportToOBJ(CADObject cadObject)
        {
            if (cadObject == null || cadObject.MeshFilter.sharedMesh == null) return null;

            string filename = $"{cadObject.name}_{DateTime.Now:yyyyMMdd_HHmmss}.obj";
            string fullPath = Path.Combine(GetExportDirectory(), filename);

            Mesh mesh = cadObject.MeshFilter.sharedMesh;
            Transform t = cadObject.transform;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# MetaQuest VR CAD Exporter");
            sb.AppendLine($"# Object: {cadObject.name}");
            sb.AppendLine($"# Date: {DateTime.Now}");

            Vector3[] verts = mesh.vertices;
            Vector3[] norms = mesh.normals;
            Vector2[] uvs = mesh.uv;
            int[] tris = mesh.triangles;

            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 worldPt = t.TransformPoint(verts[i]);
                sb.AppendLine($"v {worldPt.x:F5} {worldPt.y:F5} {worldPt.z:F5}");
            }

            for (int i = 0; i < norms.Length; i++)
            {
                Vector3 worldNorm = t.TransformDirection(norms[i]);
                sb.AppendLine($"vn {worldNorm.x:F5} {worldNorm.y:F5} {worldNorm.z:F5}");
            }

            for (int i = 0; i < uvs.Length; i++)
            {
                sb.AppendLine($"vt {uvs[i].x:F5} {uvs[i].y:F5}");
            }

            sb.AppendLine($"g {cadObject.name}");
            for (int i = 0; i < tris.Length; i += 3)
            {
                int i1 = tris[i + 0] + 1;
                int i2 = tris[i + 1] + 1;
                int i3 = tris[i + 2] + 1;
                sb.AppendLine($"f {i1}/{i1}/{i1} {i2}/{i2}/{i2} {i3}/{i3}/{i3}");
            }

            File.WriteAllText(fullPath, sb.ToString());
            Debug.Log($"[CAD_ExportManager] Exported OBJ to: {fullPath}");
            return fullPath;
        }

        private void WriteAsciiSTL(string path, Mesh mesh, Transform t)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"solid {mesh.name}");

            Vector3[] verts = mesh.vertices;
            int[] tris = mesh.triangles;

            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 v0 = t.TransformPoint(verts[tris[i + 0]]);
                Vector3 v1 = t.TransformPoint(verts[tris[i + 1]]);
                Vector3 v2 = t.TransformPoint(verts[tris[i + 2]]);
                Vector3 normal = Vector3.Cross(v1 - v0, v2 - v0).normalized;

                sb.AppendLine($"  facet normal {normal.x:e} {normal.y:e} {normal.z:e}");
                sb.AppendLine("    outer loop");
                sb.AppendLine($"      vertex {v0.x:e} {v0.y:e} {v0.z:e}");
                sb.AppendLine($"      vertex {v1.x:e} {v1.y:e} {v1.z:e}");
                sb.AppendLine($"      vertex {v2.x:e} {v2.y:e} {v2.z:e}");
                sb.AppendLine("    endloop");
                sb.AppendLine("  endfacet");
            }

            sb.AppendLine($"endsolid {mesh.name}");
            File.WriteAllText(path, sb.ToString());
        }

        private void WriteBinarySTL(string path, Mesh mesh, Transform t)
        {
            using (BinaryWriter writer = new BinaryWriter(File.Open(path, FileMode.Create)))
            {
                // 80-byte header
                byte[] header = new byte[80];
                Encoding.ASCII.GetBytes("MetaQuest VR CAD Binary STL").CopyTo(header, 0);
                writer.Write(header);

                Vector3[] verts = mesh.vertices;
                int[] tris = mesh.triangles;
                uint triCount = (uint)(tris.Length / 3);
                writer.Write(triCount);

                for (int i = 0; i < tris.Length; i += 3)
                {
                    Vector3 v0 = t.TransformPoint(verts[tris[i + 0]]);
                    Vector3 v1 = t.TransformPoint(verts[tris[i + 1]]);
                    Vector3 v2 = t.TransformPoint(verts[tris[i + 2]]);
                    Vector3 normal = Vector3.Cross(v1 - v0, v2 - v0).normalized;

                    writer.Write(normal.x);
                    writer.Write(normal.y);
                    writer.Write(normal.z);

                    writer.Write(v0.x);
                    writer.Write(v0.y);
                    writer.Write(v0.z);

                    writer.Write(v1.x);
                    writer.Write(v1.y);
                    writer.Write(v1.z);

                    writer.Write(v2.x);
                    writer.Write(v2.y);
                    writer.Write(v2.z);

                    writer.Write((ushort)0); // Attribute byte count
                }
            }
        }
    }
}
