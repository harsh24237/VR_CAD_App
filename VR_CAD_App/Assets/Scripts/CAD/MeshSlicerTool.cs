using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using VRCAD.Core;

namespace VRCAD.Tools
{
    public class MeshSlicerTool : MonoBehaviour
    {
        public XRBaseController leftController;
        public XRBaseController rightController;
        public Camera mainCamera;

        private bool isActive = false;
        private bool isPinchingBoth = false;

        private LineRenderer cutLine;
        private GameObject previewPlane;
        private MeshFilter previewPlaneFilter;
        private MeshRenderer previewPlaneRenderer;

        private void Start()
        {
            if (mainCamera == null) mainCamera = Camera.main;

            // Find controllers if not set
            if (leftController == null || rightController == null)
            {
                var controllers = FindObjectsOfType<XRBaseController>();
                foreach (var c in controllers)
                {
                    if (c.gameObject.name.Contains("Left", System.StringComparison.OrdinalIgnoreCase)) leftController = c;
                    if (c.gameObject.name.Contains("Right", System.StringComparison.OrdinalIgnoreCase)) rightController = c;
                }
            }

            // Create Visuals
            GameObject lineObj = new GameObject("CutLine");
            lineObj.transform.SetParent(transform);
            cutLine = lineObj.AddComponent<LineRenderer>();
            cutLine.startWidth = 0.005f;
            cutLine.endWidth = 0.005f;
            cutLine.material = new Material(Shader.Find("Unlit/Color"));
            cutLine.material.color = Color.red;
            cutLine.enabled = false;

            previewPlane = new GameObject("CutPreviewPlane");
            previewPlane.transform.SetParent(transform);
            previewPlaneFilter = previewPlane.AddComponent<MeshFilter>();
            previewPlaneRenderer = previewPlane.AddComponent<MeshRenderer>();
            
            Material planeMat = new Material(Shader.Find("Standard"));
            planeMat.SetFloat("_Mode", 3); // Transparent
            planeMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            planeMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            planeMat.SetInt("_ZWrite", 0);
            planeMat.DisableKeyword("_ALPHATEST_ON");
            planeMat.DisableKeyword("_ALPHABLEND_ON");
            planeMat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            planeMat.renderQueue = 3000;
            planeMat.color = new Color(1f, 0f, 0f, 0.2f);
            previewPlaneRenderer.material = planeMat;
            
            CreateQuadMesh();
            previewPlane.SetActive(false);
        }

        private void CreateQuadMesh()
        {
            Mesh m = new Mesh();
            m.vertices = new Vector3[] {
                new Vector3(-0.5f, -0.5f, 0),
                new Vector3(0.5f, -0.5f, 0),
                new Vector3(-0.5f, 0.5f, 0),
                new Vector3(0.5f, 0.5f, 0)
            };
            m.triangles = new int[] { 0, 2, 1, 2, 3, 1 };
            m.RecalculateNormals();
            previewPlaneFilter.sharedMesh = m;
        }

        private void Update()
        {
            // Activate only if Perform Cut mode is active. (Mocking state from CADManagerHub)
            // If the user selected "Perform Cut", we should enable this tool. 
            // We can also poll CADManagerHub or just let this run.
            
            if (leftController == null || rightController == null || mainCamera == null) return;

            bool leftPinch = leftController.selectInteractionState.active || leftController.selectInteractionState.value > 0.8f;
            bool rightPinch = rightController.selectInteractionState.active || rightController.selectInteractionState.value > 0.8f;

            if (leftPinch && rightPinch)
            {
                if (!isPinchingBoth)
                {
                    isPinchingBoth = true;
                    cutLine.enabled = true;
                    previewPlane.SetActive(true);
                }

                UpdateVisuals();
            }
            else
            {
                if (isPinchingBoth)
                {
                    // Released pinch while both were active -> EXECUTE CUT!
                    isPinchingBoth = false;
                    cutLine.enabled = false;
                    previewPlane.SetActive(false);
                    ExecuteCut();
                }
            }
        }

        private void UpdateVisuals()
        {
            Vector3 p1 = leftController.transform.position;
            Vector3 p2 = rightController.transform.position;
            cutLine.SetPosition(0, p1);
            cutLine.SetPosition(1, p2);

            Vector3 mid = (p1 + p2) * 0.5f;
            Vector3 lineDir = (p2 - p1).normalized;
            Vector3 camDir = (mainCamera.transform.position - mid).normalized;
            Vector3 normal = Vector3.Cross(lineDir, camDir).normalized;

            previewPlane.transform.position = mid;
            if (normal != Vector3.zero)
            {
                previewPlane.transform.rotation = Quaternion.LookRotation(normal, lineDir);
            }
            
            float dist = Vector3.Distance(p1, p2);
            previewPlane.transform.localScale = new Vector3(dist, dist, 1f);
        }

        private void ExecuteCut()
        {
            CADObject target = CADManagerHub.Instance?.SelectionManager?.SelectedObject;
            if (target == null) return;

            Vector3 p1 = leftController.transform.position;
            Vector3 p2 = rightController.transform.position;
            Vector3 mid = (p1 + p2) * 0.5f;
            Vector3 lineDir = (p2 - p1).normalized;
            Vector3 camDir = (mainCamera.transform.position - mid).normalized;
            Vector3 normal = Vector3.Cross(lineDir, camDir).normalized;

            Plane cutPlaneWorld = new Plane(normal, mid);
            
            // Transform plane to local space of the target CADObject
            Vector3 localNormal = target.transform.InverseTransformDirection(normal);
            Vector3 localPt = target.transform.InverseTransformPoint(mid);
            Plane localPlane = new Plane(localNormal, localPt);

            SliceMesh(target, localPlane);
        }

        private void SliceMesh(CADObject target, Plane plane)
        {
            Mesh sourceMesh = target.MeshFilter.sharedMesh;
            if (sourceMesh == null) return;

            Vector3[] verts = sourceMesh.vertices;
            int[] tris = sourceMesh.triangles;

            List<Vector3> posVerts = new List<Vector3>();
            List<int> posTris = new List<int>();

            List<Vector3> negVerts = new List<Vector3>();
            List<int> negTris = new List<int>();

            List<Vector3> cutEdges = new List<Vector3>();

            for (int i = 0; i < tris.Length; i += 3)
            {
                int i0 = tris[i];
                int i1 = tris[i + 1];
                int i2 = tris[i + 2];

                Vector3 v0 = verts[i0];
                Vector3 v1 = verts[i1];
                Vector3 v2 = verts[i2];

                bool p0 = plane.GetSide(v0);
                bool p1 = plane.GetSide(v1);
                bool p2 = plane.GetSide(v2);

                if (p0 && p1 && p2)
                {
                    AddTriangle(posVerts, posTris, v0, v1, v2);
                }
                else if (!p0 && !p1 && !p2)
                {
                    AddTriangle(negVerts, negTris, v0, v1, v2);
                }
                else
                {
                    // Triangle intersects the plane. 
                    // Simplified handling for basic shapes: we will just assign the triangle 
                    // to the side where the majority of its vertices lie to avoid complex ear-clipping.
                    // A true rigorous slicer requires adding vertices at the exact intersection.
                    int posCount = (p0 ? 1 : 0) + (p1 ? 1 : 0) + (p2 ? 1 : 0);
                    if (posCount >= 2) AddTriangle(posVerts, posTris, v0, v1, v2);
                    else AddTriangle(negVerts, negTris, v0, v1, v2);
                    
                    // We record the center of the triangle as an approximate cap edge point
                    cutEdges.Add((v0 + v1 + v2) / 3f);
                }
            }

            // Simple Cap Generation
            if (cutEdges.Count > 0)
            {
                Vector3 capCenter = Vector3.zero;
                foreach (var v in cutEdges) capCenter += v;
                capCenter /= cutEdges.Count;

                // Sort edges around normal
                cutEdges.Sort((a, b) =>
                {
                    Vector3 d1 = (a - capCenter).normalized;
                    Vector3 d2 = (b - capCenter).normalized;
                    float angle1 = Mathf.Atan2(Vector3.Dot(plane.normal, Vector3.Cross(Vector3.right, d1)), Vector3.Dot(Vector3.right, d1));
                    float angle2 = Mathf.Atan2(Vector3.Dot(plane.normal, Vector3.Cross(Vector3.right, d2)), Vector3.Dot(Vector3.right, d2));
                    return angle1.CompareTo(angle2);
                });

                for (int i = 0; i < cutEdges.Count; i++)
                {
                    Vector3 next = cutEdges[(i + 1) % cutEdges.Count];
                    AddTriangle(posVerts, posTris, capCenter, next, cutEdges[i]);
                    AddTriangle(negVerts, negTris, capCenter, cutEdges[i], next);
                }
            }

            // Apply results to positive mesh
            Mesh posMesh = new Mesh();
            posMesh.vertices = posVerts.ToArray();
            posMesh.triangles = posTris.ToArray();
            posMesh.RecalculateNormals();
            target.MeshFilter.sharedMesh = posMesh;
            target.MeshCollider.sharedMesh = posMesh;

            // Spawn negative mesh
            if (negVerts.Count > 0)
            {
                GameObject negObj = Instantiate(target.gameObject, target.transform.parent);
                CADObject negCad = negObj.GetComponent<CADObject>();
                Mesh negMesh = new Mesh();
                negMesh.vertices = negVerts.ToArray();
                negMesh.triangles = negTris.ToArray();
                negMesh.RecalculateNormals();
                negCad.MeshFilter.sharedMesh = negMesh;
                negCad.MeshCollider.sharedMesh = negMesh;
            }
        }

        private void AddTriangle(List<Vector3> verts, List<int> tris, Vector3 v0, Vector3 v1, Vector3 v2)
        {
            int idx = verts.Count;
            verts.Add(v0);
            verts.Add(v1);
            verts.Add(v2);
            tris.Add(idx);
            tris.Add(idx + 1);
            tris.Add(idx + 2);
        }
    }
}
