using System;
using TMPro;
using UnityEngine;

namespace VRCAD.Core
{
    public enum DisplayUnit
    {
        Centimeters,
        Millimeters,
        Meters,
        Inches
    }

    /// <summary>
    /// Projects a real-time CAD footprint bounding box and dimension readouts directly onto the
    /// horizontal CAD grid surface beneath the object, matching precision AR/VR CAD cutting-mat
    /// workplane dimensioning.
    ///
    /// Labels lie flat on the grid surface along the projected footprint edges and auto-orient
    /// to the user's camera viewpoint for optimal readability without cluttering the 3D space.
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public class DimensionVisualizer : MonoBehaviour
    {
        // ──────────────────────────────────────────────
        //  Inspector Settings
        // ──────────────────────────────────────────────

        [Header("Display Units & Format")]
        [Tooltip("Unit used to display measurements on the grid.")]
        [SerializeField] private DisplayUnit displayUnit = DisplayUnit.Centimeters;

        [Tooltip("Number of decimal places displayed (e.g. 2 for 18.22 cm).")]
        [SerializeField] private int decimalPlaces = 2;

        [Header("Footprint On Grid")]
        [Tooltip("Show the rectangular footprint projection on the grid plane.")]
        [SerializeField] private bool showFootprint = true;

        [Tooltip("Color of the footprint bounding box outline on the grid.")]
        [SerializeField] private Color footprintColor = new Color(0.98f, 0.99f, 1.0f, 0.95f);

        [Tooltip("Width of the footprint outline line on the grid (meters).")]
        [SerializeField] private float footprintLineWidth = 0.0035f;

        [Tooltip("Vertical offset above the grid plane to prevent z-fighting (meters).")]
        [SerializeField] private float surfaceOffset = 0.0025f;

        [Header("Dimension Labels On Grid")]
        [Tooltip("Show dimension labels along the grid footprint edges.")]
        [SerializeField] private bool showLabels = true;

        [Tooltip("Color of the dimension text.")]
        [SerializeField] private Color labelColor = new Color(0.98f, 0.99f, 1.0f, 1.0f);

        [Tooltip("Font size for TextMeshPro.")]
        [SerializeField] private float labelFontSize = 3.5f;

        [Tooltip("Local scale applied to label transform for crisp SDF rendering.")]
        [SerializeField] private float labelScale = 0.055f;

        [Tooltip("Outward margin between the footprint edge and the dimension text (meters).")]
        [SerializeField] private float labelMargin = 0.03f;

        [Header("Drop Lines (Connectors to Grid)")]
        [Tooltip("Show subtle vertical lines connecting the floating object to the grid footprint.")]
        [SerializeField] private bool showDropLines = true;

        [Tooltip("Color of the drop connector lines.")]
        [SerializeField] private Color dropLineColor = new Color(0.92f, 0.96f, 1.0f, 0.35f);

        [Tooltip("Width of the drop connector lines.")]
        [SerializeField] private float dropLineWidth = 0.0018f;

        [Header("Height Display (Optional)")]
        [Tooltip("Show height dimension if object is elevated or extruded.")]
        [SerializeField] private bool showHeight = false;

        // ──────────────────────────────────────────────
        //  Public Properties
        // ──────────────────────────────────────────────

        public DisplayUnit Unit
        {
            get => displayUnit;
            set => displayUnit = value;
        }

        public bool ShowFootprint
        {
            get => showFootprint;
            set
            {
                showFootprint = value;
                if (_footprintRenderer != null) _footprintRenderer.enabled = value;
            }
        }

        public bool ShowLabels
        {
            get => showLabels;
            set
            {
                showLabels = value;
                if (_widthLabel != null) _widthLabel.gameObject.SetActive(value);
                if (_depthLabel != null) _depthLabel.gameObject.SetActive(value);
                if (_heightLabel != null) _heightLabel.gameObject.SetActive(value && showHeight);
            }
        }

        public bool ShowDropLines
        {
            get => showDropLines;
            set
            {
                showDropLines = value;
                if (_dropLines != null)
                {
                    for (int i = 0; i < _dropLines.Length; i++)
                    {
                        if (_dropLines[i] != null) _dropLines[i].enabled = value;
                    }
                }
            }
        }

        public bool ShowHeight
        {
            get => showHeight;
            set
            {
                showHeight = value;
                if (_heightLabel != null) _heightLabel.gameObject.SetActive(value && showLabels);
            }
        }

        // ──────────────────────────────────────────────
        //  Internal State
        // ──────────────────────────────────────────────

        private Renderer _renderer;
        private MeshFilter _meshFilter;
        private Transform _cam;
        private Transform _cachedGridTransform;

        // Root container for easy cleanup
        private GameObject _vizRoot;

        // Footprint outline on grid
        private LineRenderer _footprintRenderer;
        private Material _footprintMaterial;

        // Labels on grid
        private TextMeshPro _widthLabel;
        private TextMeshPro _depthLabel;
        private TextMeshPro _heightLabel;

        // Drop lines connecting object to grid
        private LineRenderer[] _dropLines;
        private Material _dropLineMaterial;

        // ──────────────────────────────────────────────
        //  Lifecycle
        // ──────────────────────────────────────────────

        private void Start()
        {
            _renderer = GetComponent<Renderer>();
            _meshFilter = GetComponent<MeshFilter>();
            _cam = Camera.main != null ? Camera.main.transform : null;

            BuildVisualization();
        }

        private void LateUpdate()
        {
            if (_renderer == null || !_renderer.enabled || !gameObject.activeInHierarchy)
            {
                if (_vizRoot != null && _vizRoot.activeSelf) _vizRoot.SetActive(false);
                return;
            }

            if (_vizRoot != null && !_vizRoot.activeSelf)
            {
                _vizRoot.SetActive(true);
            }

            if (_cam == null && Camera.main != null)
            {
                _cam = Camera.main.transform;
            }

            UpdateGridFootprintAndDimensions();
        }

        private void OnEnable()
        {
            if (_vizRoot != null) _vizRoot.SetActive(true);
        }

        private void OnDisable()
        {
            if (_vizRoot != null) _vizRoot.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_vizRoot != null)
            {
                Destroy(_vizRoot);
            }

            if (_footprintMaterial != null) Destroy(_footprintMaterial);
            if (_dropLineMaterial != null) Destroy(_dropLineMaterial);
        }

        // ──────────────────────────────────────────────
        //  Build Visualization Objects
        // ──────────────────────────────────────────────

        private void BuildVisualization()
        {
            _vizRoot = new GameObject($"DimViz_{gameObject.name}");

            // 1. Footprint line renderer on the grid
            _footprintRenderer = CreateFootprintRenderer();

            // 2. Dimension labels lying flat on the grid
            _widthLabel = CreateGridLabel("GridLabel_Width");
            _depthLabel = CreateGridLabel("GridLabel_Depth");
            _heightLabel = CreateGridLabel("GridLabel_Height");
            _heightLabel.gameObject.SetActive(showHeight && showLabels);

            // 3. Four drop lines connecting bottom corners to grid
            _dropLines = CreateDropLineRenderers();
        }

        private LineRenderer CreateFootprintRenderer()
        {
            GameObject obj = new GameObject("Footprint_Outline");
            obj.transform.SetParent(_vizRoot.transform, false);

            LineRenderer lr = obj.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 5;
            lr.loop = false;
            lr.startWidth = footprintLineWidth;
            lr.endWidth = footprintLineWidth;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;

            _footprintMaterial = CreateLineMaterial(footprintColor);
            lr.material = _footprintMaterial;
            lr.startColor = footprintColor;
            lr.endColor = footprintColor;

            obj.SetActive(showFootprint);
            return lr;
        }

        private TextMeshPro CreateGridLabel(string name)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(_vizRoot.transform, false);

            TextMeshPro tmp = obj.AddComponent<TextMeshPro>();
            tmp.fontSize = labelFontSize;
            tmp.color = labelColor;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.fontStyle = FontStyles.Bold;
            tmp.sortingOrder = 200;

            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(3.0f, 0.8f);
            rect.localScale = Vector3.one * labelScale;

            obj.SetActive(showLabels);
            return tmp;
        }

        private LineRenderer[] CreateDropLineRenderers()
        {
            LineRenderer[] lines = new LineRenderer[4];
            _dropLineMaterial = CreateLineMaterial(dropLineColor);

            for (int i = 0; i < 4; i++)
            {
                GameObject obj = new GameObject($"DropLine_{i}");
                obj.transform.SetParent(_vizRoot.transform, false);

                LineRenderer lr = obj.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.positionCount = 2;
                lr.startWidth = dropLineWidth;
                lr.endWidth = dropLineWidth;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.material = _dropLineMaterial;
                lr.startColor = dropLineColor;
                lr.endColor = dropLineColor;

                obj.SetActive(showDropLines);
                lines[i] = lr;
            }
            return lines;
        }

        // ──────────────────────────────────────────────
        //  Core Update: Project Footprint & Orient Labels
        // ──────────────────────────────────────────────

        private void UpdateGridFootprintAndDimensions()
        {
            // 1. Identify grid plane (origin and normal)
            Transform gridTransform = GetGridTransform();
            Vector3 gridUp = (gridTransform != null) ? gridTransform.up : Vector3.up;
            Vector3 gridPoint = (gridTransform != null) ? gridTransform.position : new Vector3(0, 0.78f, 1.05f);

            // 2. Compute 8 object corners in local and world space
            Bounds localB = (_meshFilter != null && _meshFilter.sharedMesh != null)
                ? _meshFilter.sharedMesh.bounds
                : new Bounds(Vector3.zero, Vector3.one);

            Vector3 min = localB.min;
            Vector3 max = localB.max;

            // 4 bottom corners in local space (Y = min.y)
            Vector3[] localBottom = new Vector3[4]
            {
                new Vector3(min.x, min.y, min.z),
                new Vector3(max.x, min.y, min.z),
                new Vector3(max.x, min.y, max.z),
                new Vector3(min.x, min.y, max.z)
            };

            // 4 top corners in local space (Y = max.y)
            Vector3[] localTop = new Vector3[4]
            {
                new Vector3(min.x, max.y, min.z),
                new Vector3(max.x, max.y, min.z),
                new Vector3(max.x, max.y, max.z),
                new Vector3(min.x, max.y, max.z)
            };

            Vector3[] worldBottom = new Vector3[4];
            Vector3[] worldTop = new Vector3[4];
            Vector3[] allWorld = new Vector3[8];

            for (int i = 0; i < 4; i++)
            {
                worldBottom[i] = transform.TransformPoint(localBottom[i]);
                worldTop[i] = transform.TransformPoint(localTop[i]);
                allWorld[i] = worldBottom[i];
                allWorld[i + 4] = worldTop[i];
            }

            // 3. Project object's orientation onto grid plane
            Vector3 objFwd = Vector3.ProjectOnPlane(transform.forward, gridUp);
            if (objFwd.sqrMagnitude < 0.0001f)
            {
                objFwd = Vector3.ProjectOnPlane(transform.up, gridUp);
            }
            if (objFwd.sqrMagnitude < 0.0001f)
            {
                objFwd = (gridTransform != null) ? gridTransform.forward : Vector3.forward;
            }
            objFwd.Normalize();

            Vector3 objRight = Vector3.Cross(gridUp, objFwd).normalized;

            // 4. Center point projected onto grid
            Vector3 centerOnGrid = ProjectPointToGrid(transform.position, gridPoint, gridUp);

            // 5. Find min/max bounds of all 8 projected corners along objRight and objFwd
            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;

            for (int i = 0; i < 8; i++)
            {
                Vector3 proj = ProjectPointToGrid(allWorld[i], gridPoint, gridUp);
                Vector3 diff = proj - centerOnGrid;
                float x = Vector3.Dot(diff, objRight);
                float z = Vector3.Dot(diff, objFwd);

                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (z < minZ) minZ = z;
                if (z > maxZ) maxZ = z;
            }

            // 4 corners of the oriented footprint rectangle on the grid
            Vector3 c0 = centerOnGrid + objRight * minX + objFwd * minZ; // Front-Left
            Vector3 c1 = centerOnGrid + objRight * maxX + objFwd * minZ; // Front-Right
            Vector3 c2 = centerOnGrid + objRight * maxX + objFwd * maxZ; // Back-Right
            Vector3 c3 = centerOnGrid + objRight * minX + objFwd * maxZ; // Back-Left

            float width = maxX - minX;
            float depth = maxZ - minZ;
            float height = localB.size.y * transform.lossyScale.y;

            // 6. Update footprint outline on grid
            if (showFootprint && _footprintRenderer != null)
            {
                _footprintRenderer.enabled = true;
                _footprintRenderer.SetPosition(0, c0);
                _footprintRenderer.SetPosition(1, c1);
                _footprintRenderer.SetPosition(2, c2);
                _footprintRenderer.SetPosition(3, c3);
                _footprintRenderer.SetPosition(4, c0);
            }
            else if (_footprintRenderer != null)
            {
                _footprintRenderer.enabled = false;
            }

            // 7. Update dimension labels on the grid
            if (showLabels)
            {
                // Width label (along front edge: c0 to c1)
                Vector3 widthMid = (c0 + c1) * 0.5f;
                Vector3 widthOutward = -objFwd;
                Vector3 widthPos = widthMid + widthOutward * labelMargin;

                _widthLabel.gameObject.SetActive(true);
                _widthLabel.text = FormatMeasurement(width);
                _widthLabel.transform.position = widthPos;
                OrientLabelOnGrid(_widthLabel.transform, objRight, widthOutward, gridUp);

                // Depth label (along right edge: c1 to c2)
                Vector3 depthMid = (c1 + c2) * 0.5f;
                Vector3 depthOutward = objRight;
                Vector3 depthPos = depthMid + depthOutward * labelMargin;

                _depthLabel.gameObject.SetActive(true);
                _depthLabel.text = FormatMeasurement(depth);
                _depthLabel.transform.position = depthPos;
                OrientLabelOnGrid(_depthLabel.transform, objFwd, depthOutward, gridUp);

                // Optional height label
                if (showHeight && _heightLabel != null)
                {
                    _heightLabel.gameObject.SetActive(true);
                    Vector3 heightPos = c1 + (objRight - objFwd).normalized * (labelMargin * 1.5f);
                    _heightLabel.text = $"H: {FormatMeasurement(height)}";
                    _heightLabel.transform.position = heightPos;
                    OrientLabelOnGrid(_heightLabel.transform, objRight, (objRight - objFwd).normalized, gridUp);
                }
                else if (_heightLabel != null)
                {
                    _heightLabel.gameObject.SetActive(false);
                }
            }
            else
            {
                if (_widthLabel != null) _widthLabel.gameObject.SetActive(false);
                if (_depthLabel != null) _depthLabel.gameObject.SetActive(false);
                if (_heightLabel != null) _heightLabel.gameObject.SetActive(false);
            }

            // 8. Update drop connector lines from bottom corners to grid footprint
            float bottomDistToGrid = Vector3.Dot(worldBottom[0] - gridPoint, gridUp);
            bool isElevated = bottomDistToGrid > 0.015f;

            if (showDropLines && isElevated && _dropLines != null)
            {
                // Connect 4 bottom corners of object to matching 4 grid footprint corners
                Vector3[] targetGridCorners = new Vector3[4] { c0, c1, c2, c3 };
                for (int i = 0; i < 4; i++)
                {
                    if (_dropLines[i] != null)
                    {
                        _dropLines[i].enabled = true;
                        _dropLines[i].SetPosition(0, worldBottom[i]);
                        _dropLines[i].SetPosition(1, targetGridCorners[i]);
                    }
                }
            }
            else if (_dropLines != null)
            {
                for (int i = 0; i < 4; i++)
                {
                    if (_dropLines[i] != null) _dropLines[i].enabled = false;
                }
            }
        }

        // ──────────────────────────────────────────────
        //  Math & Orientation Helpers
        // ──────────────────────────────────────────────

        private Vector3 ProjectPointToGrid(Vector3 p, Vector3 gridPoint, Vector3 gridUp)
        {
            float dist = Vector3.Dot(p - gridPoint, gridUp);
            return p - (dist - surfaceOffset) * gridUp;
        }

        /// <summary>
        /// Orients a TextMeshPro label so it lies completely flat on the grid surface,
        /// facing upward towards the sky/viewer (normal = -gridUp so front face is visible),
        /// and auto-orients so letters read left-to-right and right-side up from the camera.
        /// </summary>
        private void OrientLabelOnGrid(Transform labelTransform, Vector3 edgeDir, Vector3 outwardDir, Vector3 gridUp)
        {
            if (_cam != null)
            {
                // Ensure edgeDir flows from left-to-right relative to camera
                Vector3 camRightOnGrid = Vector3.ProjectOnPlane(_cam.right, gridUp).normalized;
                if (Vector3.Dot(edgeDir, camRightOnGrid) < -0.2f)
                {
                    edgeDir = -edgeDir;
                }
            }

            // textUp is perpendicular to edgeDir on the grid plane
            Vector3 textUp = Vector3.Cross(gridUp, edgeDir).normalized;

            // Ensure textUp points in the direction of the camera's gaze (away from viewer)
            // so the top of the letters points away and the text is right-side up
            if (_cam != null)
            {
                Vector3 camGaze = Vector3.ProjectOnPlane(labelTransform.position - _cam.position, gridUp).normalized;
                if (Vector3.Dot(textUp, camGaze) < 0f)
                {
                    textUp = -textUp;
                    edgeDir = -edgeDir;
                }
            }

            // Normal is -gridUp (pointing into grid) so TextMeshPro's front face faces UP toward viewer
            labelTransform.rotation = Quaternion.LookRotation(-gridUp, textUp);
        }

        private Transform GetGridTransform()
        {
            if (_cachedGridTransform != null && _cachedGridTransform.gameObject.activeInHierarchy)
            {
                return _cachedGridTransform;
            }

            if (CAD_EnvironmentManager.Instance != null && CAD_EnvironmentManager.Instance.GridFloorTransform != null)
            {
                _cachedGridTransform = CAD_EnvironmentManager.Instance.GridFloorTransform;
                return _cachedGridTransform;
            }

            GameObject gridObj = GameObject.Find("GridFloor");
            if (gridObj != null)
            {
                _cachedGridTransform = gridObj.transform;
                return _cachedGridTransform;
            }

            return null;
        }

        // ──────────────────────────────────────────────
        //  Dimension Formatting (Matching Ref: "18.22 cm")
        // ──────────────────────────────────────────────

        private string FormatMeasurement(float worldMeters)
        {
            string fmt = $"F{decimalPlaces}";
            switch (displayUnit)
            {
                case DisplayUnit.Centimeters:
                    float cm = worldMeters * 100f;
                    return $"{cm.ToString(fmt)} cm";

                case DisplayUnit.Millimeters:
                    float mm = worldMeters * 1000f;
                    return $"{mm.ToString(fmt)} mm";

                case DisplayUnit.Meters:
                    return $"{worldMeters.ToString(fmt)} m";

                case DisplayUnit.Inches:
                    float inches = worldMeters * 39.3701f;
                    return $"{inches.ToString(fmt)} in";

                default:
                    return $"{(worldMeters * 100f).ToString(fmt)} cm";
            }
        }

        private static Material CreateLineMaterial(Color color)
        {
            Shader shader = Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            Material mat = new Material(shader);
            mat.color = color;

            if (mat.HasProperty("_Mode"))
            {
                mat.SetFloat("_Mode", 3);
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = 3100;
            }

            return mat;
        }
    }
}
