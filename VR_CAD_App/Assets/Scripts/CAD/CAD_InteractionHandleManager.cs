using System.Collections.Generic;
using UnityEngine;

namespace VRCAD.Core
{
    public class CAD_InteractionHandleManager : MonoBehaviour
    {
        private List<CAD_InteractionHandle> activeHandles = new List<CAD_InteractionHandle>();
        
        private CADObject currentObject;
        private SelectionMode currentMode = SelectionMode.Object;

        private void Start()
        {
            if (CADManagerHub.Instance != null)
            {
                CADManagerHub.Instance.SelectionUpdated += OnSelectionUpdated;
                CADManagerHub.Instance.SelectionModeChanged += OnSelectionModeChanged;
            }
        }

        private void OnDestroy()
        {
            if (CADManagerHub.Instance != null)
            {
                CADManagerHub.Instance.SelectionUpdated -= OnSelectionUpdated;
                CADManagerHub.Instance.SelectionModeChanged -= OnSelectionModeChanged;
            }
        }

        private void OnSelectionUpdated(CADObject cadObj)
        {
            currentObject = cadObj;
            RefreshHandles();
        }

        private void OnSelectionModeChanged(SelectionMode mode)
        {
            currentMode = mode;
            RefreshHandles();
        }

        public void RefreshHandles()
        {
            ClearHandles();

            if (currentObject == null) return;

            Mesh mesh = currentObject.MeshFilter.sharedMesh;
            if (mesh == null) return;

            switch (currentMode)
            {
                case SelectionMode.Object:
                    SpawnObjectHandles(currentObject, mesh);
                    break;
                case SelectionMode.Vertex:
                    SpawnVertexHandles(currentObject, mesh);
                    break;
                case SelectionMode.Edge:
                    SpawnEdgeHandles(currentObject, mesh);
                    break;
                case SelectionMode.Face:
                    SpawnFaceHandles(currentObject, mesh);
                    break;
            }
        }

        private void ClearHandles()
        {
            foreach (var handle in activeHandles)
            {
                if (handle != null)
                {
                    handle.OnHandleDragged -= OnHandleDragged;
                    Destroy(handle.gameObject);
                }
            }
            activeHandles.Clear();
        }

        private CAD_InteractionHandle CreateHandle(Vector3 worldPos, string nameSuffix)
        {
            GameObject handleObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            handleObj.name = $"InteractionHandle_{nameSuffix}";
            handleObj.transform.position = worldPos;
            handleObj.transform.localScale = Vector3.one * 0.015f; 
            handleObj.layer = 2; // Layer 2 is 'Ignore Raycast' in Unity, ensuring interaction handles do not intercept selection raycasts
            
            Destroy(handleObj.GetComponent<Collider>());
            SphereCollider col = handleObj.AddComponent<SphereCollider>();
            col.isTrigger = false; 

            CAD_InteractionHandle handle = handleObj.AddComponent<CAD_InteractionHandle>();
            handle.OnHandleDragged += OnHandleDragged;
            handle.OnHandleGrabbed += OnHandleGrabbed;
            handle.OnHandleReleased += OnHandleReleased;

            activeHandles.Add(handle);
            return handle;
        }

        private void OnHandleGrabbed(CAD_InteractionHandle handle)
        {
            if (CADManagerHub.Instance != null && handle.TargetObject != null)
            {
                CADManagerHub.Instance.UndoRedoManager?.RecordCommand(new MeshSnapshotCommand(
                    handle.TargetObject, 
                    Instantiate(handle.TargetObject.MeshFilter.sharedMesh), 
                    handle.TargetObject.MeshFilter.sharedMesh, 
                    $"Deform {handle.TargetObject.name}"
                ));
            }
        }

        private void OnHandleReleased(CAD_InteractionHandle handle)
        {
            if (handle.TargetObject != null)
            {
                // Re-bake physics collider ONLY on release for performance
                if (handle.TargetObject.MeshCollider != null && handle.TargetObject.MeshFilter != null)
                {
                    handle.TargetObject.MeshCollider.sharedMesh = null;
                    handle.TargetObject.MeshCollider.sharedMesh = handle.TargetObject.MeshFilter.sharedMesh;
                }

                // Refresh all handles so they snap exactly back onto the new vertex/edge/face centers
                RefreshHandles(); 
            }
        }

        private void OnHandleDragged(CAD_InteractionHandle handle, Vector3 worldDelta)
        {
            if (handle.TargetObject == null) return;
            Vector3 localDelta = handle.TargetObject.transform.InverseTransformVector(worldDelta);

            switch (handle.Type)
            {
                case HandleType.VertexMove:
                    handle.TargetObject.ApplyVertexDeformation(handle.Index1, localDelta);
                    break;
                case HandleType.EdgeMove:
                    handle.TargetObject.ApplyEdgeDeformation(handle.Index1, handle.Index2, localDelta);
                    break;
                case HandleType.FaceExtrude:
                    handle.TargetObject.ApplyFaceDeformation(handle.Index1, localDelta);
                    break;
                case HandleType.ObjectScale:
                    handle.TargetObject.ApplyBoundingBoxDeformation(handle.CornerDirection, localDelta);
                    break;
            }
        }

        private void SpawnObjectHandles(CADObject target, Mesh mesh)
        {
            Bounds b = mesh.bounds;
            Vector3[] corners = new Vector3[]
            {
                new Vector3(b.max.x, b.max.y, b.max.z),
                new Vector3(b.min.x, b.max.y, b.max.z),
                new Vector3(b.max.x, b.min.y, b.max.z),
                new Vector3(b.min.x, b.min.y, b.max.z),
                new Vector3(b.max.x, b.max.y, b.min.z),
                new Vector3(b.min.x, b.max.y, b.min.z),
                new Vector3(b.max.x, b.min.y, b.min.z),
                new Vector3(b.min.x, b.min.y, b.min.z)
            };

            foreach (var corner in corners)
            {
                Vector3 worldPos = target.transform.TransformPoint(corner);
                Vector3 cornerDir = (corner - b.center).normalized;
                
                var handle = CreateHandle(worldPos, "Corner");
                handle.InitializeForObjectScale(target, new Vector3(Mathf.Sign(cornerDir.x), Mathf.Sign(cornerDir.y), Mathf.Sign(cornerDir.z)));
            }
        }

        private void SpawnVertexHandles(CADObject target, Mesh mesh)
        {
            // Only spawn handle for the actively selected vertex
            int vIdx = target.SelectedVertexIndex;
            if (vIdx < 0 || vIdx >= mesh.vertices.Length) return;

            Vector3 pos = mesh.vertices[vIdx];
            Vector3 worldPos = target.transform.TransformPoint(pos);
            var handle = CreateHandle(worldPos, $"Vertex_{vIdx}");
            handle.InitializeForVertex(target, vIdx);
        }

        private void SpawnEdgeHandles(CADObject target, Mesh mesh)
        {
            // Only spawn handle for the actively selected edge
            var edge = target.SelectedEdge;
            if (edge.v1 < 0 || edge.v2 < 0 || edge.v1 >= mesh.vertices.Length || edge.v2 >= mesh.vertices.Length) return;

            Vector3 p1 = mesh.vertices[edge.v1];
            Vector3 p2 = mesh.vertices[edge.v2];
            Vector3 mid = (p1 + p2) * 0.5f;

            Vector3 worldPos = target.transform.TransformPoint(mid);
            var handle = CreateHandle(worldPos, $"Edge_{edge.v1}_{edge.v2}");
            handle.InitializeForEdge(target, edge.v1, edge.v2);
        }

        private void SpawnFaceHandles(CADObject target, Mesh mesh)
        {
            // Only spawn a single handle for the actively selected face
            int faceIdx = target.SelectedFaceIndex;
            if (faceIdx < 0) return;

            int[] tris = mesh.triangles;
            Vector3[] verts = mesh.vertices;
            if (faceIdx * 3 + 2 >= tris.Length) return;

            Vector3 p0 = verts[tris[faceIdx * 3]];
            Vector3 p1 = verts[tris[faceIdx * 3 + 1]];
            Vector3 p2 = verts[tris[faceIdx * 3 + 2]];
            Vector3 center = (p0 + p1 + p2) / 3f;

            Vector3 worldPos = target.transform.TransformPoint(center);
            var handle = CreateHandle(worldPos, $"Face_{faceIdx}");
            handle.InitializeForFace(target, faceIdx);
        }
    }
}
