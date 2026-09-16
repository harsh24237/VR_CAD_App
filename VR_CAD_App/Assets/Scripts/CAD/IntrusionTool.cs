using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using VRCAD.Core;

namespace VRCAD.Tools
{
    public class IntrusionTool : MonoBehaviour
    {
        public bool IsActive = false;

        private XRBaseController rightController;
        private XRRayInteractor rayInteractor;

        private LineRenderer outlineRenderer;
        
        private CADObject hoveredObject;
        private int hoveredTriangleIndex = -1;
        private Vector3 hoveredNormal;

        private bool isDragging = false;
        private CADObject draggingObject;
        private List<int> dynamicVerts;
        private Vector3[] baseVertsSnapshot;
        private float dragStartProj;

        private void Start()
        {
            var controllers = FindObjectsOfType<XRBaseController>();
            foreach (var c in controllers)
            {
                if (c.gameObject.name.Contains("Right", System.StringComparison.OrdinalIgnoreCase))
                {
                    rightController = c;
                    rayInteractor = c.GetComponentInChildren<XRRayInteractor>();
                }
            }

            GameObject lineObj = new GameObject("IntrusionOutline");
            lineObj.transform.SetParent(transform);
            outlineRenderer = lineObj.AddComponent<LineRenderer>();
            outlineRenderer.startWidth = 0.003f;
            outlineRenderer.endWidth = 0.003f;
            outlineRenderer.material = new Material(Shader.Find("Unlit/Color"));
            outlineRenderer.material.color = new Color(1f, 0.4f, 0f, 0.8f);
            outlineRenderer.loop = true;
            outlineRenderer.enabled = false;
        }

        private void Update()
        {
            if (!IsActive)
            {
                if (outlineRenderer.enabled) outlineRenderer.enabled = false;
                isDragging = false;
                return;
            }

            if (rightController == null || rayInteractor == null) return;

            bool isTriggerPulled = rightController.selectInteractionState.active || rightController.selectInteractionState.value > 0.8f;

#if UNITY_EDITOR
            if (Application.isEditor && !UnityEngine.XR.XRSettings.isDeviceActive)
            {
                isTriggerPulled = Input.GetMouseButton(0);
            }
#endif

            if (!isDragging)
            {
                // Hover Phase
                if (rayInteractor.TryGetCurrent3DRaycastHit(out RaycastHit hit))
                {
                    CADObject hitCad = hit.collider.GetComponent<CADObject>();
                    if (hitCad != null && hit.triangleIndex >= 0)
                    {
                        if (hitCad != hoveredObject || hit.triangleIndex != hoveredTriangleIndex)
                        {
                            hoveredObject = hitCad;
                            hoveredTriangleIndex = hit.triangleIndex;
                            UpdateHighlightOutline(hitCad, hit.triangleIndex);
                        }
                    }
                    else
                    {
                        ClearHover();
                    }
                }
                else
                {
                    ClearHover();
                }

                // Check for Drag Start
                if (isTriggerPulled && hoveredObject != null)
                {
                    StartDragging();
                }
            }
            else
            {
                // Drag Phase
                if (isTriggerPulled)
                {
                    UpdateDragging();
                }
                else
                {
                    EndDragging();
                }
            }
        }

        private void ClearHover()
        {
            hoveredObject = null;
            hoveredTriangleIndex = -1;
            outlineRenderer.enabled = false;
        }

        private void UpdateHighlightOutline(CADObject cadObj, int triIndex)
        {
            Mesh m = cadObj.MeshFilter.sharedMesh;
            if (m == null) return;

            int[] tris = m.triangles;
            Vector3[] verts = m.vertices;
            
            if (triIndex * 3 + 2 >= tris.Length) return;

            Vector3 v0 = verts[tris[triIndex * 3 + 0]];
            Vector3 v1 = verts[tris[triIndex * 3 + 1]];
            Vector3 v2 = verts[tris[triIndex * 3 + 2]];

            hoveredNormal = Vector3.Cross(v1 - v0, v2 - v0).normalized;
            if (hoveredNormal.sqrMagnitude < 0.001f) hoveredNormal = m.normals[tris[triIndex * 3 + 0]];

            // Simplified highlighting: Just draw the triangle for now since finding the whole polygon perimeter is complex real-time
            outlineRenderer.positionCount = 3;
            outlineRenderer.SetPosition(0, cadObj.transform.TransformPoint(v0) + cadObj.transform.TransformDirection(hoveredNormal) * 0.001f);
            outlineRenderer.SetPosition(1, cadObj.transform.TransformPoint(v1) + cadObj.transform.TransformDirection(hoveredNormal) * 0.001f);
            outlineRenderer.SetPosition(2, cadObj.transform.TransformPoint(v2) + cadObj.transform.TransformDirection(hoveredNormal) * 0.001f);
            outlineRenderer.enabled = true;
        }

        private float editorAccumulatedDrag = 0f;

        private void StartDragging()
        {
            if (CADManagerHub.Instance?.ExtrusionManager == null) return;

            draggingObject = hoveredObject;
            Vector3 worldNormal = draggingObject.transform.TransformDirection(hoveredNormal);
            dragStartProj = Vector3.Dot(rightController.transform.position, worldNormal);
            editorAccumulatedDrag = 0f;

            // Generate topology
            if (CADManagerHub.Instance.ExtrusionManager.PrepareFaceForInteractiveExtrusion(draggingObject, hoveredTriangleIndex, out dynamicVerts))
            {
                isDragging = true;
                baseVertsSnapshot = (Vector3[])draggingObject.MeshFilter.sharedMesh.vertices.Clone();
                outlineRenderer.enabled = false;
            }
        }

        private void UpdateDragging()
        {
            float distanceMoved = 0f;

            if (Application.isEditor && !UnityEngine.XR.XRSettings.isDeviceActive)
            {
                editorAccumulatedDrag += Input.GetAxis("Mouse Y") * 0.05f;
                distanceMoved = editorAccumulatedDrag;
            }
            else
            {
                Vector3 worldNormal = draggingObject.transform.TransformDirection(hoveredNormal);
                float currentProj = Vector3.Dot(rightController.transform.position, worldNormal);
                distanceMoved = currentProj - dragStartProj; 
            }
            
            // Limit outward extrusion if we only want intrusion, but let's allow both for flexibility
            Vector3 localOffset = hoveredNormal * distanceMoved;

            Mesh m = draggingObject.MeshFilter.sharedMesh;
            Vector3[] activeVerts = (Vector3[])baseVertsSnapshot.Clone();

            foreach(int vIdx in dynamicVerts)
            {
                activeVerts[vIdx] += localOffset;
            }

            m.vertices = activeVerts;
            m.RecalculateBounds();
        }

        private void EndDragging()
        {
            isDragging = false;
            if (draggingObject != null)
            {
                Mesh m = draggingObject.MeshFilter.sharedMesh;
                m.RecalculateNormals();
                m.RecalculateTangents();
                draggingObject.MeshCollider.sharedMesh = m;
                draggingObject.UpdateWireframeMesh();
                CADManagerHub.Instance?.OnMeshModified(draggingObject);
                draggingObject = null;
            }
        }
    }
}
