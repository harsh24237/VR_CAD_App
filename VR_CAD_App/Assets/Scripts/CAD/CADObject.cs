using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace VRCAD.Core
{
    public enum CADShapeType
    {
        Box,
        Cylinder,
        Sphere,
        Cone,
        Prism,
        Torus,
        Custom
    }

    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
    [RequireComponent(typeof(XRGrabInteractable), typeof(Rigidbody))]
    public class CADObject : MonoBehaviour
    {
        [Header("CAD Metadata")]
        [SerializeField] private string objectId;
        [SerializeField] private CADShapeType shapeType = CADShapeType.Custom;
        [SerializeField] private Vector3 dimensions = Vector3.one;

        [Header("Components")]
        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private MeshCollider meshCollider;
        private XRGrabInteractable grabInteractable;
        private Rigidbody rb;

        [Header("Selection State")]
        private bool isSelected = false;
        private int selectedFaceIndex = -1;
        private int selectedVertexIndex = -1;
        private int selectedEdgeIndex = -1;

        [Header("Materials")]
        private Material defaultMaterial;
        private Material selectedMaterial;
        private Material highlightMaterial;

        public string ObjectId => objectId;
        public CADShapeType ShapeType { get => shapeType; set => shapeType = value; }
        public Vector3 Dimensions { get => dimensions; set => dimensions = value; }
        public MeshFilter MeshFilter => meshFilter ??= GetComponent<MeshFilter>();
        public MeshRenderer MeshRenderer => meshRenderer ??= GetComponent<MeshRenderer>();
        public MeshCollider MeshCollider => meshCollider ??= GetComponent<MeshCollider>();
        public XRGrabInteractable GrabInteractable => grabInteractable ??= GetComponent<XRGrabInteractable>();
        public Rigidbody Rigidbody => rb ??= GetComponent<Rigidbody>();

        public bool IsSelected => isSelected;
        public int SelectedFaceIndex => selectedFaceIndex;
        public int SelectedVertexIndex => selectedVertexIndex;
        public int SelectedEdgeIndex => selectedEdgeIndex;

        private void Awake()
        {
            if (string.IsNullOrEmpty(objectId))
            {
                objectId = "CAD_" + System.Guid.NewGuid().ToString().Substring(0, 8);
            }

            meshFilter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();
            meshCollider = GetComponent<MeshCollider>();
            grabInteractable = GetComponent<XRGrabInteractable>();
            rb = GetComponent<Rigidbody>();

            SetupPhysicsAndGrabbing();
            CreateDefaultMaterials();
        }

        private void SetupPhysicsAndGrabbing()
        {
            if (rb != null)
            {
                rb.useGravity = false;
                rb.isKinematic = true;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
            }

            if (grabInteractable != null)
            {
                grabInteractable.movementType = XRBaseInteractable.MovementType.VelocityTracking;
                grabInteractable.throwOnDetach = false;
                grabInteractable.selectEntered.AddListener(OnSelectEntered);
                grabInteractable.selectExited.AddListener(OnSelectExited);
            }
        }

        private void CreateDefaultMaterials()
        {
            Shader standardShader = Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Legacy Shaders/Diffuse");
            
            defaultMaterial = new Material(standardShader)
            {
                name = "CAD_DefaultMat",
                color = new Color(0.85f, 0.88f, 0.92f, 1.0f)
            };

            selectedMaterial = new Material(standardShader)
            {
                name = "CAD_SelectedMat",
                color = new Color(0.18f, 0.55f, 0.95f, 1.0f)
            };

            highlightMaterial = new Material(standardShader)
            {
                name = "CAD_HighlightMat",
                color = new Color(1.0f, 0.65f, 0.15f, 1.0f)
            };

            if (meshRenderer != null && meshRenderer.sharedMaterial == null)
            {
                meshRenderer.material = defaultMaterial;
            }
        }

        public void SetMesh(Mesh newMesh)
        {
            if (newMesh == null) return;

            MeshFilter.sharedMesh = newMesh;
            
            if (MeshCollider != null)
            {
                MeshCollider.sharedMesh = null;
                MeshCollider.sharedMesh = newMesh;
                MeshCollider.convex = true;
            }
        }

        public void SetColor(Color color)
        {
            if (defaultMaterial != null)
            {
                defaultMaterial.color = color;
            }
            if (meshRenderer != null && !isSelected)
            {
                meshRenderer.material = defaultMaterial;
            }
        }

        public Color GetColor()
        {
            return defaultMaterial != null ? defaultMaterial.color : Color.white;
        }

        public void AdjustPosition(int axis, float delta)
        {
            Vector3 pos = transform.localPosition;
            if (axis == 0) pos.x += delta;
            else if (axis == 1) pos.y += delta;
            else if (axis == 2) pos.z += delta;
            transform.localPosition = pos;
            CADManagerHub.Instance?.OnMeshModified(this);
        }

        public void AdjustRotation(int axis, float delta)
        {
            Vector3 euler = transform.localEulerAngles;
            if (axis == 0) euler.x += delta;
            else if (axis == 1) euler.y += delta;
            else if (axis == 2) euler.z += delta;
            transform.localEulerAngles = euler;
            CADManagerHub.Instance?.OnMeshModified(this);
        }

        public void AdjustScale(int axis, float delta, bool uniform)
        {
            Vector3 scale = transform.localScale;
            if (uniform)
            {
                scale += Vector3.one * delta;
            }
            else
            {
                if (axis == 0) scale.x += delta;
                else if (axis == 1) scale.y += delta;
                else if (axis == 2) scale.z += delta;
            }

            scale.x = Mathf.Max(0.01f, scale.x);
            scale.y = Mathf.Max(0.01f, scale.y);
            scale.z = Mathf.Max(0.01f, scale.z);

            transform.localScale = scale;
            dimensions = scale;
            CADManagerHub.Instance?.OnMeshModified(this);
        }

        public void SetSelected(bool selected)
        {
            isSelected = selected;
            if (meshRenderer != null)
            {
                meshRenderer.material = isSelected ? selectedMaterial : defaultMaterial;
            }
        }

        public void SetSelectedFace(int faceIndex)
        {
            selectedFaceIndex = faceIndex;
            selectedEdgeIndex = -1;
            selectedVertexIndex = -1;
        }

        public void SetSelectedVertex(int vertexIndex)
        {
            selectedVertexIndex = vertexIndex;
            selectedFaceIndex = -1;
            selectedEdgeIndex = -1;
        }

        public void SetSelectedEdge(int edgeIndex)
        {
            selectedEdgeIndex = edgeIndex;
            selectedFaceIndex = -1;
            selectedVertexIndex = -1;
        }

        public void ClearSubSelections()
        {
            selectedFaceIndex = -1;
            selectedVertexIndex = -1;
            selectedEdgeIndex = -1;
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            CADManagerHub.Instance?.OnObjectGrabbed(this, args);
        }

        private void OnSelectExited(SelectExitEventArgs args)
        {
            CADManagerHub.Instance?.OnObjectReleased(this, args);
        }

        private void OnDestroy()
        {
            if (grabInteractable != null)
            {
                grabInteractable.selectEntered.RemoveListener(OnSelectEntered);
                grabInteractable.selectExited.RemoveListener(OnSelectExited);
            }
        }
    }
}
