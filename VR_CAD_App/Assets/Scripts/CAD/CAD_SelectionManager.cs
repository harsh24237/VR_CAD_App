using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRCAD.Core
{
    public enum SelectionMode
    {
        Object,
        Face,
        Edge,
        Vertex
    }

    public class CAD_SelectionManager : MonoBehaviour
    {
        [Header("Selection Configuration")]
        [SerializeField] private SelectionMode activeMode = SelectionMode.Object;
        [SerializeField] private LayerMask cadLayerMask = ~0;

        [Header("Active Selection")]
        private CADObject selectedObject;
        private int selectedFaceTriangleIndex = -1;
        private int selectedVertexIndex = -1;
        private (int v1, int v2) selectedEdge = (-1, -1);

        [Header("Visual Selection Helpers")]
        private GameObject selectionVisualizer;
        private LineRenderer edgeHighlightRenderer;
        private GameObject vertexHighlightSphere;

        public SelectionMode ActiveMode
        {
            get => activeMode;
            set
            {
                activeMode = value;
                ClearSelection();
                CADManagerHub.Instance?.OnSelectionModeChanged(activeMode);
            }
        }

        public CADObject SelectedObject => selectedObject;
        public int SelectedFaceTriangleIndex => selectedFaceTriangleIndex;
        public int SelectedVertexIndex => selectedVertexIndex;
        public (int v1, int v2) SelectedEdge => selectedEdge;

        private void Awake()
        {
            CreateVisualHelpers();
        }

        private void CreateVisualHelpers()
        {
            selectionVisualizer = new GameObject("CAD_SelectionVisualizer");
            selectionVisualizer.transform.SetParent(transform);

            // Edge highlight LineRenderer
            GameObject edgeObj = new GameObject("EdgeHighlight");
            edgeObj.transform.SetParent(selectionVisualizer.transform);
            edgeHighlightRenderer = edgeObj.AddComponent<LineRenderer>();
            edgeHighlightRenderer.startWidth = 0.006f;
            edgeHighlightRenderer.endWidth = 0.006f;
            edgeHighlightRenderer.useWorldSpace = true;
            edgeHighlightRenderer.positionCount = 2;
            
            Material lineMat = new Material(Shader.Find("Unlit/Color") ?? Shader.Find("Standard"))
            {
                color = new Color(1.0f, 0.85f, 0.1f, 1.0f)
            };
            edgeHighlightRenderer.material = lineMat;
            edgeHighlightRenderer.enabled = false;

            // Vertex highlight Sphere
            vertexHighlightSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            vertexHighlightSphere.name = "VertexHighlight";
            vertexHighlightSphere.transform.SetParent(selectionVisualizer.transform);
            vertexHighlightSphere.transform.localScale = Vector3.one * 0.015f;
            
            if (vertexHighlightSphere.TryGetComponent<Collider>(out var col))
            {
                Destroy(col);
            }

            if (vertexHighlightSphere.TryGetComponent<MeshRenderer>(out var mr))
            {
                Material sphereMat = new Material(Shader.Find("Unlit/Color") ?? Shader.Find("Standard"))
                {
                    color = new Color(0.95f, 0.25f, 0.25f, 1.0f)
                };
                mr.material = sphereMat;
            }
            vertexHighlightSphere.SetActive(false);
        }

        public bool RaycastSelect(Ray ray)
        {
            if (Physics.Raycast(ray, out RaycastHit hit, 50f, cadLayerMask))
            {
                CADObject cadObj = hit.collider.GetComponentInParent<CADObject>();
                if (cadObj != null)
                {
                    Select(cadObj, hit);
                    return true;
                }
            }

            return false;
        }

        public void Select(CADObject cadObj, RaycastHit hit)
        {
            if (cadObj == null) return;

            // Deselect previous
            if (selectedObject != null && selectedObject != cadObj)
            {
                selectedObject.SetSelected(false);
                selectedObject.ClearSubSelections();
            }

            selectedObject = cadObj;
            selectedObject.SetSelected(true);

            Mesh mesh = cadObj.MeshFilter.sharedMesh;
            if (mesh == null) return;

            switch (activeMode)
            {
                case SelectionMode.Object:
                    selectedObject.ClearSubSelections();
                    HideVisualHelpers();
                    break;

                case SelectionMode.Face:
                    SelectFaceAt(cadObj, hit.triangleIndex);
                    break;

                case SelectionMode.Vertex:
                    SelectClosestVertexAt(cadObj, hit.point);
                    break;

                case SelectionMode.Edge:
                    SelectClosestEdgeAt(cadObj, hit.point, hit.triangleIndex);
                    break;
            }

            CADManagerHub.Instance?.OnSelectionUpdated(selectedObject);
        }

        public void SelectFaceAt(CADObject cadObj, int triangleIndex)
        {
            if (cadObj == null || triangleIndex < 0) return;
            selectedFaceTriangleIndex = triangleIndex;
            cadObj.SetSelectedFace(triangleIndex);
            HideVisualHelpers();
        }

        public void SelectClosestVertexAt(CADObject cadObj, Vector3 hitWorldPoint)
        {
            if (cadObj == null || cadObj.MeshFilter.sharedMesh == null) return;

            Mesh mesh = cadObj.MeshFilter.sharedMesh;
            Vector3[] verts = mesh.vertices;
            Vector3 localHit = cadObj.transform.InverseTransformPoint(hitWorldPoint);

            int closestIdx = 0;
            float minDist = float.MaxValue;

            for (int i = 0; i < verts.Length; i++)
            {
                float d = Vector3.Distance(localHit, verts[i]);
                if (d < minDist)
                {
                    minDist = d;
                    closestIdx = i;
                }
            }

            selectedVertexIndex = closestIdx;
            cadObj.SetSelectedVertex(closestIdx);

            // Show vertex visualizer
            Vector3 worldPos = cadObj.transform.TransformPoint(verts[closestIdx]);
            if (vertexHighlightSphere != null)
            {
                vertexHighlightSphere.transform.position = worldPos;
                vertexHighlightSphere.SetActive(true);
            }
            if (edgeHighlightRenderer != null) edgeHighlightRenderer.enabled = false;
        }

        public void SelectClosestEdgeAt(CADObject cadObj, Vector3 hitWorldPoint, int triangleIndex)
        {
            if (cadObj == null || cadObj.MeshFilter.sharedMesh == null) return;

            Mesh mesh = cadObj.MeshFilter.sharedMesh;
            int[] tris = mesh.triangles;
            Vector3[] verts = mesh.vertices;

            if (triangleIndex * 3 + 2 >= tris.Length) return;

            int i0 = tris[triangleIndex * 3 + 0];
            int i1 = tris[triangleIndex * 3 + 1];
            int i2 = tris[triangleIndex * 3 + 2];

            Vector3 w0 = cadObj.transform.TransformPoint(verts[i0]);
            Vector3 w1 = cadObj.transform.TransformPoint(verts[i1]);
            Vector3 w2 = cadObj.transform.TransformPoint(verts[i2]);

            float d01 = DistancePointToSegment(hitWorldPoint, w0, w1);
            float d12 = DistancePointToSegment(hitWorldPoint, w1, w2);
            float d20 = DistancePointToSegment(hitWorldPoint, w2, w0);

            if (d01 <= d12 && d01 <= d20)
            {
                selectedEdge = (i0, i1);
                ShowEdgeHighlight(w0, w1);
            }
            else if (d12 <= d01 && d12 <= d20)
            {
                selectedEdge = (i1, i2);
                ShowEdgeHighlight(w1, w2);
            }
            else
            {
                selectedEdge = (i2, i0);
                ShowEdgeHighlight(w2, w0);
            }

            cadObj.SetSelectedEdge(selectedEdge.v1);
        }

        private void ShowEdgeHighlight(Vector3 p1, Vector3 p2)
        {
            if (edgeHighlightRenderer != null)
            {
                edgeHighlightRenderer.SetPosition(0, p1);
                edgeHighlightRenderer.SetPosition(1, p2);
                edgeHighlightRenderer.enabled = true;
            }
            if (vertexHighlightSphere != null) vertexHighlightSphere.SetActive(false);
        }

        private void HideVisualHelpers()
        {
            if (edgeHighlightRenderer != null) edgeHighlightRenderer.enabled = false;
            if (vertexHighlightSphere != null) vertexHighlightSphere.SetActive(false);
        }

        public void ClearSelection()
        {
            if (selectedObject != null)
            {
                selectedObject.SetSelected(false);
                selectedObject.ClearSubSelections();
                selectedObject = null;
            }

            selectedFaceTriangleIndex = -1;
            selectedVertexIndex = -1;
            selectedEdge = (-1, -1);
            HideVisualHelpers();
        }

        private float DistancePointToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float t = Vector3.Dot(p - a, ab) / Vector3.Dot(ab, ab);
            t = Mathf.Clamp01(t);
            Vector3 closest = a + t * ab;
            return Vector3.Distance(p, closest);
        }
    }
}
