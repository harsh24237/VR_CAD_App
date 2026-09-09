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
        Wedge,
        Face,
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

        [Header("Wireframe State")]
        private GameObject wireframeChild;
        private MeshFilter wireframeFilter;
        private MeshRenderer wireframeRenderer;
        private bool isWireframeMode = false;

        [Header("Selection Cage Visual")]
        private GameObject selectionCage;

        public bool IsWireframeMode => isWireframeMode;

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
        public (int v1, int v2) SelectedEdge => (selectedEdgeIndex >= 0) ? (0, 1) : (-1, -1);

        private void Awake()
        {
            if (string.IsNullOrEmpty(objectId))
            {
                objectId = Guid.NewGuid().ToString("N");
            }

            meshFilter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();
            meshCollider = GetComponent<MeshCollider>();
            grabInteractable = GetComponent<XRGrabInteractable>();
            rb = GetComponent<Rigidbody>();

            CreateDefaultMaterials();
            ConfigurePhysicsAndXR();
        }

        private void ConfigurePhysicsAndXR()
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

            if (isWireframeMode)
            {
                UpdateWireframeMesh();
            }

            if (isSelected)
            {
                UpdateSelectionCageBounds();
            }
        }

        [Header("State Tracking")]
        [SerializeField] private Color baseColor = new Color(0.18f, 0.45f, 0.95f, 1f);
        [SerializeField] private float roughness = 0.35f;
        [SerializeField] private float metallic = 0.10f;
        [SerializeField] private float opacity = 1.0f;

        public float Roughness => roughness;
        public float Metallic => metallic;
        public float Opacity => opacity;

        public void ApplyMaterialSettings()
        {
            if (meshRenderer == null) meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer == null) return;

            Material mat = meshRenderer.material;
            if (mat == null) return;

            Color c = baseColor;
            c.a = Mathf.Clamp01(opacity);
            mat.color = c;

            float smoothness = 1f - Mathf.Clamp01(roughness);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", Mathf.Clamp01(metallic));

            ConfigureMaterialTransparency(mat, opacity);

            if (wireframeRenderer != null && wireframeRenderer.material != null)
            {
                wireframeRenderer.material.color = c;
            }
        }

        public void SetColor(Color color)
        {
            baseColor = color;
            ApplyMaterialSettings();
        }

        public void SetMaterialProperties(float r, float m)
        {
            roughness = Mathf.Clamp01(r);
            metallic = Mathf.Clamp01(m);
            ApplyMaterialSettings();
        }

        public void SetOpacity(float op)
        {
            opacity = Mathf.Clamp01(op);
            ApplyMaterialSettings();
        }

        private void ConfigureMaterialTransparency(Material mat, float op)
        {
            if (op < 0.99f)
            {
                // Fade mode (Mode 2) allows diffuse and specular to fade smoothly with alpha
                mat.SetFloat("_Mode", 2f);
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            else
            {
                mat.SetFloat("_Mode", 0f); // Opaque
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                mat.SetInt("_ZWrite", 1);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.DisableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = -1;
            }
        }

        public void ApplyChromePreset()
        {
            baseColor = new Color(0.96f, 0.97f, 1.0f, 1.0f);
            roughness = 0.02f;
            metallic = 1.0f;
            opacity = 1.0f;
            ApplyMaterialSettings();

            if (meshRenderer != null && meshRenderer.material != null)
            {
                Material mat = meshRenderer.material;
                if (mat.HasProperty("_SpecularHighlights")) mat.SetFloat("_SpecularHighlights", 1f);
                if (mat.HasProperty("_GlossyReflections")) mat.SetFloat("_GlossyReflections", 1f);
            }
        }

        public void ResetMaterialAndEffects(Color bColor)
        {
            baseColor = bColor;
            roughness = 0.35f;
            metallic = 0.10f;
            opacity = 1.0f;
            SetWireframeMode(false);
            ApplyMaterialSettings();

            if (meshRenderer != null && meshRenderer.material != null)
            {
                Material mat = meshRenderer.material;
                if (mat.HasProperty("_SpecularHighlights")) mat.SetFloat("_SpecularHighlights", 1f);
                if (mat.HasProperty("_GlossyReflections")) mat.SetFloat("_GlossyReflections", 1f);
                mat.DisableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", Color.black);
            }
        }

        public void SetWireframeMode(bool enabled)
        {
            isWireframeMode = enabled;
            if (enabled)
            {
                UpdateWireframeMesh();
                if (wireframeChild != null) wireframeChild.SetActive(true);
                if (meshRenderer != null) meshRenderer.enabled = false;
            }
            else
            {
                if (wireframeChild != null) wireframeChild.SetActive(false);
                if (meshRenderer != null) meshRenderer.enabled = true;
            }
        }

        public void UpdateWireframeMesh()
        {
            Mesh sourceMesh = MeshFilter.sharedMesh;
            if (sourceMesh == null) return;

            if (wireframeChild == null)
            {
                wireframeChild = new GameObject("CAD_Wireframe_Lines");
                wireframeChild.transform.SetParent(transform, false);
                wireframeChild.transform.localPosition = Vector3.zero;
                wireframeChild.transform.localRotation = Quaternion.identity;
                wireframeChild.transform.localScale = Vector3.one;

                wireframeFilter = wireframeChild.AddComponent<MeshFilter>();
                wireframeRenderer = wireframeChild.AddComponent<MeshRenderer>();

                Material wireMat = new Material(Shader.Find("Unlit/Color") ?? Shader.Find("Standard"));
                wireMat.color = new Color(0.00f, 0.88f, 1.0f, 1.0f); // Radiant CAD vector wireframe
                wireframeRenderer.material = wireMat;
                wireframeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                wireframeRenderer.receiveShadows = false;
            }

            int[] triangles = sourceMesh.triangles;
            Vector3[] vertices = sourceMesh.vertices;
            HashSet<(int, int)> edges = new HashSet<(int, int)>();

            for (int t = 0; t < triangles.Length; t += 3)
            {
                int i0 = triangles[t];
                int i1 = triangles[t + 1];
                int i2 = triangles[t + 2];

                AddUniqueEdge(edges, i0, i1);
                AddUniqueEdge(edges, i1, i2);
                AddUniqueEdge(edges, i2, i0);
            }

            int[] lineIndices = new int[edges.Count * 2];
            int idx = 0;
            foreach (var edge in edges)
            {
                lineIndices[idx++] = edge.Item1;
                lineIndices[idx++] = edge.Item2;
            }

            Mesh wireMesh = new Mesh
            {
                name = sourceMesh.name + "_WireframeMesh",
                vertices = vertices
            };
            wireMesh.SetIndices(lineIndices, MeshTopology.Lines, 0);
            wireMesh.RecalculateBounds();

            wireframeFilter.sharedMesh = wireMesh;
        }

        private void AddUniqueEdge(HashSet<(int, int)> set, int a, int b)
        {
            if (a > b) { int tmp = a; a = b; b = tmp; }
            set.Add((a, b));
        }

        public Color GetColor()
        {
            if (meshRenderer != null && meshRenderer.material != null)
                return meshRenderer.material.color;
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

        public void SetPositionValue(int axis, float value)
        {
            Vector3 pos = transform.localPosition;
            if (axis == 0) pos.x = value;
            else if (axis == 1) pos.y = value;
            else if (axis == 2) pos.z = value;
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

        public void SetRotationValue(int axis, float value)
        {
            Vector3 euler = transform.localEulerAngles;
            if (axis == 0) euler.x = value;
            else if (axis == 1) euler.y = value;
            else if (axis == 2) euler.z = value;
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

        public void SetScaleValue(int axis, float value)
        {
            Vector3 scale = transform.localScale;
            if (axis == 0) scale.x = Mathf.Max(0.005f, value);
            else if (axis == 1) scale.y = Mathf.Max(0.005f, value);
            else if (axis == 2) scale.z = Mathf.Max(0.005f, value);
            transform.localScale = scale;
            dimensions = scale;
            CADManagerHub.Instance?.OnMeshModified(this);
        }

        public void SetSelected(bool selected)
        {
            isSelected = selected;

            // Ensure no color-polluting emission is active on the shape's material
            if (meshRenderer != null && meshRenderer.material != null)
            {
                Material mat = meshRenderer.material;
                mat.DisableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", Color.black);
            }

            UpdateSelectionVisual();
        }

        private void UpdateSelectionVisual()
        {
            if (isSelected)
            {
                if (selectionCage == null)
                {
                    selectionCage = CreateSelectionCage();
                }
                selectionCage.SetActive(true);
                UpdateSelectionCageBounds();
            }
            else
            {
                if (selectionCage != null)
                {
                    selectionCage.SetActive(false);
                }
            }
        }

        private GameObject CreateSelectionCage()
        {
            GameObject cage = new GameObject("CAD_SelectionCage");
            cage.transform.SetParent(transform, false);
            cage.transform.localPosition = Vector3.zero;
            cage.transform.localRotation = Quaternion.identity;
            cage.transform.localScale = Vector3.one;

            LineRenderer lr = cage.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.startWidth = 0.0035f;
            lr.endWidth = 0.0035f;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;

            Material cageMat = new Material(Shader.Find("Unlit/Color") ?? Shader.Find("Standard"));
            cageMat.color = new Color(0.00f, 0.85f, 1.00f, 0.95f); // Radiant CAD cyan bounding cage
            lr.material = cageMat;

            return cage;
        }

        public void UpdateSelectionCageBounds()
        {
            if (selectionCage == null) return;
            Mesh m = MeshFilter.sharedMesh;
            if (m == null) return;

            LineRenderer lr = selectionCage.GetComponent<LineRenderer>();
            if (lr == null) return;

            Bounds b = m.bounds;
            Vector3 min = b.min * 1.025f;
            Vector3 max = b.max * 1.025f;

            // 16 points tracing the 12 edges of a box
            Vector3[] pts = new Vector3[]
            {
                new Vector3(min.x, min.y, min.z),
                new Vector3(max.x, min.y, min.z),
                new Vector3(max.x, max.y, min.z),
                new Vector3(min.x, max.y, min.z),
                new Vector3(min.x, min.y, min.z),

                new Vector3(min.x, min.y, max.z),
                new Vector3(max.x, min.y, max.z),
                new Vector3(max.x, min.y, min.z),
                new Vector3(max.x, min.y, max.z),

                new Vector3(max.x, max.y, max.z),
                new Vector3(max.x, max.y, min.z),
                new Vector3(max.x, max.y, max.z),

                new Vector3(min.x, max.y, max.z),
                new Vector3(min.x, max.y, min.z),
                new Vector3(min.x, max.y, max.z),

                new Vector3(min.x, min.y, max.z)
            };

            lr.positionCount = pts.Length;
            lr.SetPositions(pts);
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
