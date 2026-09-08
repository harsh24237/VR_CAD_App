using UnityEngine;

namespace VRCAD.Core
{
    /// <summary>
    /// Creates the authentic deep space environment background and the horizontal CAD grid floor
    /// that sits directly in front of the floating UI panels. Shapes spawn above this grid.
    /// </summary>
    public class CAD_EnvironmentManager : MonoBehaviour
    {
        [Header("Space Background")]
        [SerializeField] private Color spaceBackgroundColor = new Color(0.003f, 0.004f, 0.008f, 1f);
        [SerializeField] private int starCount = 750;

        [Header("Grid Floor (Horizontal work plane in front of UI)")]
        [SerializeField] private Vector3 gridCenter = new Vector3(0, 0.45f, 1.35f);
        [SerializeField] private float gridWidth = 4.2f;
        [SerializeField] private float gridDepth = 2.6f;
        [SerializeField] private int gridLinesX = 32;
        [SerializeField] private int gridLinesZ = 20;
        [SerializeField] private Color gridLineColor = new Color(0.00f, 0.65f, 0.90f, 0.35f);
        [SerializeField] private Color gridBorderColor = new Color(0.00f, 0.85f, 1.00f, 0.70f);
        [SerializeField] private float gridLineWidth = 0.004f;
        [SerializeField] private float gridBorderWidth = 0.008f;

        [Header("Grid Glow")]
        [SerializeField] private Color gridGlowColor = new Color(0.00f, 0.50f, 0.80f, 0.06f);

        public Transform GridFloorTransform { get; private set; }
        public Vector3 GridCenter => gridCenter;
        public float GridSurfaceY => gridCenter.y;

        private GameObject environmentRoot;

        private void Awake()
        {
            BuildEnvironment();
        }

        private void Start()
        {
            ApplySpaceCameraSettings();
        }

        private void LateUpdate()
        {
            // Ensure camera background remains deep space black even if XR runtime resets it
            if (Camera.main != null && Camera.main.backgroundColor != spaceBackgroundColor)
            {
                Camera.main.clearFlags = CameraClearFlags.SolidColor;
                Camera.main.backgroundColor = spaceBackgroundColor;
            }
        }

        private void BuildEnvironment()
        {
            environmentRoot = new GameObject("CAD_Environment");
            environmentRoot.transform.SetParent(transform);

            ApplySpaceCameraSettings();
            BuildStudioLighting();
            BuildGridFloor();
            BuildRealisticStarField();
            BuildCosmicNebulae();
        }

        /// <summary>
        /// Forces all cameras to render against an authentic deep space cosmic void.
        /// </summary>
        private void ApplySpaceCameraSettings()
        {
            foreach (Camera cam in Camera.allCameras)
            {
                if (cam != null)
                {
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = spaceBackgroundColor;
                }
            }

            if (Camera.main != null)
            {
                Camera.main.clearFlags = CameraClearFlags.SolidColor;
                Camera.main.backgroundColor = spaceBackgroundColor;
            }

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.32f, 0.35f, 0.40f, 1f);
            RenderSettings.skybox = null;
        }

        /// <summary>
        /// Creates a professional 3-point CAD studio lighting rig and reflection probe
        /// to ensure generated shapes have razor-sharp colors, distinct face definition, and crisp specular highlights.
        /// </summary>
        private void BuildStudioLighting()
        {
            GameObject lightsRoot = new GameObject("CAD_Studio_Lighting");
            lightsRoot.transform.SetParent(environmentRoot.transform);

            // 1. Key Directional Light: clean white, sharp surface definition & soft shadows
            GameObject keyObj = new GameObject("Key_Directional_Light");
            keyObj.transform.SetParent(lightsRoot.transform);
            keyObj.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
            Light keyLight = keyObj.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.color = Color.white;
            keyLight.intensity = 1.15f;
            keyLight.shadows = LightShadows.Soft;
            keyLight.shadowStrength = 0.5f;

            // 2. Fill Directional Light: gentle cool white, illuminates shadows so colors don't wash out
            GameObject fillObj = new GameObject("Fill_Directional_Light");
            fillObj.transform.SetParent(lightsRoot.transform);
            fillObj.transform.rotation = Quaternion.Euler(-20f, 145f, 0f);
            Light fillLight = fillObj.AddComponent<Light>();
            fillLight.type = LightType.Directional;
            fillLight.color = new Color(0.88f, 0.92f, 1.0f);
            fillLight.intensity = 0.50f;
            fillLight.shadows = LightShadows.None;

            // 3. Rim / Accent Light: crisp edge highlight
            GameObject rimObj = new GameObject("Rim_Directional_Light");
            rimObj.transform.SetParent(lightsRoot.transform);
            rimObj.transform.rotation = Quaternion.Euler(-55f, 5f, 0f);
            Light rimLight = rimObj.AddComponent<Light>();
            rimLight.type = LightType.Directional;
            rimLight.color = Color.white;
            rimLight.intensity = 0.35f;
            rimLight.shadows = LightShadows.None;

            // 4. CAD Workspace Reflection Probe for metallic & glossy materials
            GameObject probeObj = new GameObject("CAD_ReflectionProbe");
            probeObj.transform.SetParent(lightsRoot.transform);
            probeObj.transform.position = new Vector3(gridCenter.x, gridCenter.y + 0.8f, gridCenter.z);
            ReflectionProbe probe = probeObj.AddComponent<ReflectionProbe>();
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
            probe.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.ViaScripting;
            probe.size = new Vector3(8f, 6f, 8f);
            probe.intensity = 0.85f;
            probe.clearFlags = UnityEngine.Rendering.ReflectionProbeClearFlags.SolidColor;
            probe.backgroundColor = new Color(0.04f, 0.06f, 0.10f, 1f);
        }

        /// <summary>
        /// Creates a crisp horizontal CAD grid floor positioned in front of the UI.
        /// </summary>
        private void BuildGridFloor()
        {
            GameObject gridRoot = new GameObject("GridFloor");
            gridRoot.transform.SetParent(environmentRoot.transform);
            gridRoot.transform.position = gridCenter;
            gridRoot.transform.rotation = Quaternion.identity;
            GridFloorTransform = gridRoot.transform;

            Material lineMat = CreateUnlitLineMaterial();

            float halfW = gridWidth * 0.5f;
            float halfD = gridDepth * 0.5f;

            // --- Grid lines along X axis (running left-right) ---
            for (int i = 0; i <= gridLinesZ; i++)
            {
                float t = (float)i / gridLinesZ;
                float z = -halfD + t * gridDepth;
                bool isBorder = (i == 0 || i == gridLinesZ);
                bool isMajor = (i % 4 == 0);

                Color c = isBorder ? gridBorderColor : (isMajor ? gridLineColor * 1.4f : gridLineColor);
                float w = isBorder ? gridBorderWidth : (isMajor ? gridLineWidth * 1.3f : gridLineWidth);

                CreateGridLine(gridRoot.transform, $"GridX_{i}",
                    new Vector3(-halfW, 0, z),
                    new Vector3(halfW, 0, z),
                    c, w, lineMat);
            }

            // --- Grid lines along Z axis (running front-back) ---
            for (int i = 0; i <= gridLinesX; i++)
            {
                float t = (float)i / gridLinesX;
                float x = -halfW + t * gridWidth;
                bool isBorder = (i == 0 || i == gridLinesX);
                bool isMajor = (i % 4 == 0);

                Color c = isBorder ? gridBorderColor : (isMajor ? gridLineColor * 1.4f : gridLineColor);
                float w = isBorder ? gridBorderWidth : (isMajor ? gridLineWidth * 1.3f : gridLineWidth);

                CreateGridLine(gridRoot.transform, $"GridZ_{i}",
                    new Vector3(x, 0, -halfD),
                    new Vector3(x, 0, halfD),
                    c, w, lineMat);
            }

            // --- Ground glow plane ---
            GameObject glowPlane = GameObject.CreatePrimitive(PrimitiveType.Quad);
            glowPlane.name = "GridGlow";
            glowPlane.transform.SetParent(gridRoot.transform, false);
            glowPlane.transform.localPosition = new Vector3(0, -0.001f, 0);
            glowPlane.transform.localRotation = Quaternion.Euler(90, 0, 0);
            glowPlane.transform.localScale = new Vector3(gridWidth, gridDepth, 1f);

            Collider glowCol = glowPlane.GetComponent<Collider>();
            if (glowCol != null) Destroy(glowCol);

            MeshRenderer glowRenderer = glowPlane.GetComponent<MeshRenderer>();
            if (glowRenderer != null)
            {
                Material glowMat = new Material(Shader.Find("Unlit/Color") ?? Shader.Find("Standard"));
                glowMat.color = gridGlowColor;
                SetMaterialTransparent(glowMat);
                glowRenderer.material = glowMat;
                glowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                glowRenderer.receiveShadows = false;
            }

            // --- High-tech Corner Accents ---
            BuildCornerAccents(gridRoot.transform, halfW, halfD, lineMat);
        }

        private void CreateGridLine(Transform parent, string name, Vector3 start, Vector3 end, Color color, float width, Material mat)
        {
            GameObject lineObj = new GameObject(name);
            lineObj.transform.SetParent(parent, false);

            LineRenderer lr = lineObj.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.positionCount = 2;
            lr.SetPosition(0, start);
            lr.SetPosition(1, end);
            lr.startWidth = width;
            lr.endWidth = width;
            lr.material = new Material(mat);
            lr.material.color = color;
            lr.startColor = color;
            lr.endColor = color;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
        }

        private void BuildCornerAccents(Transform parent, float halfW, float halfD, Material mat)
        {
            float accentLen = 0.25f;
            Color accentColor = new Color(0.00f, 0.90f, 1.0f, 0.85f);
            float accentWidth = 0.010f;

            Vector3[] corners = new Vector3[]
            {
                new Vector3(-halfW, 0, -halfD),
                new Vector3( halfW, 0, -halfD),
                new Vector3( halfW, 0,  halfD),
                new Vector3(-halfW, 0,  halfD),
            };

            Vector3[][] directions = new Vector3[][]
            {
                new[] { Vector3.right, Vector3.forward },
                new[] { Vector3.left,  Vector3.forward },
                new[] { Vector3.left,  Vector3.back    },
                new[] { Vector3.right, Vector3.back    },
            };

            for (int i = 0; i < 4; i++)
            {
                for (int d = 0; d < 2; d++)
                {
                    CreateGridLine(parent, $"CornerAccent_{i}_{d}",
                        corners[i],
                        corners[i] + directions[i][d] * accentLen,
                        accentColor, accentWidth, mat);
                }
            }
        }

        /// <summary>
        /// Builds a breathtaking realistic starfield with visible stars of calibrated sizes,
        /// celestial brightness variation, and color temperatures (diamond white, ice blue, solar gold).
        /// </summary>
        private void BuildRealisticStarField()
        {
            GameObject starsRoot = new GameObject("StarField_Celestial");
            starsRoot.transform.SetParent(environmentRoot.transform);

            Material baseStarMat = new Material(Shader.Find("Unlit/Color") ?? Shader.Find("Standard"));

            for (int i = 0; i < starCount; i++)
            {
                GameObject star = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                star.name = $"Star_{i}";
                star.transform.SetParent(starsRoot.transform);

                // Calibrated celestial sphere distance (18m - 35m)
                float dist = Random.Range(18f, 35f);
                Vector3 dir = Random.onUnitSphere;
                // Bias slightly towards upper hemisphere and forward/sides for prominent view
                if (dir.y < -0.2f && Random.value < 0.7f) dir.y = -dir.y;
                star.transform.position = dir * dist;

                // Remove collider
                Collider col = star.GetComponent<Collider>();
                if (col != null) Destroy(col);

                float size = 0.08f;
                MeshRenderer mr = star.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    Material sm = new Material(baseStarMat);
                    Color starCol;

                    if (i < 45)
                    {
                        // Tier 1: Brilliant major navigation stars (large & radiant)
                        size = Random.Range(0.16f, 0.26f);
                        float cType = Random.value;
                        if (cType < 0.35f) starCol = new Color(0.85f, 0.95f, 1.0f, 1f); // Blue giant
                        else if (cType < 0.65f) starCol = new Color(1.0f, 0.96f, 0.85f, 1f); // Warm sun
                        else if (cType < 0.85f) starCol = new Color(1.0f, 0.82f, 0.65f, 1f); // Orange giant
                        else starCol = new Color(1.0f, 1.0f, 1.0f, 1f); // Pure white

                        // Add cross flare billboard to top 20 brightest stars
                        if (i < 20)
                        {
                            CreateStarSpike(star.transform, size * 2.5f, starCol);
                        }
                    }
                    else if (i < 220)
                    {
                        // Tier 2: Medium prominent stars
                        size = Random.Range(0.08f, 0.13f);
                        float brightness = Random.Range(0.8f, 1.0f);
                        starCol = new Color(brightness, brightness * 0.98f, brightness * 0.95f, 1f);
                    }
                    else
                    {
                        // Tier 3: Distant sparkling pinpoints
                        size = Random.Range(0.045f, 0.075f);
                        float brightness = Random.Range(0.55f, 0.85f);
                        starCol = new Color(brightness, brightness, brightness, 1f);
                    }

                    sm.color = starCol;
                    mr.material = sm;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                }
                else
                {
                    size = 0.08f;
                }

                star.transform.localScale = Vector3.one * size;
            }
        }

        private void CreateStarSpike(Transform starTransform, float length, Color col)
        {
            GameObject spikeObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            spikeObj.name = "Spike";
            spikeObj.transform.SetParent(starTransform, false);
            spikeObj.transform.localPosition = Vector3.zero;
            spikeObj.transform.localScale = new Vector3(length, length * 0.15f, 1f);

            Collider c = spikeObj.GetComponent<Collider>();
            if (c != null) Destroy(c);

            MeshRenderer r = spikeObj.GetComponent<MeshRenderer>();
            if (r != null)
            {
                Material m = new Material(Shader.Find("Unlit/Color") ?? Shader.Find("Standard"));
                m.color = new Color(col.r, col.g, col.b, 0.45f);
                SetMaterialTransparent(m);
                r.material = m;
            }
        }

        /// <summary>
        /// Creates gentle deep cosmic nebula dust in the far background for authentic space depth.
        /// </summary>
        private void BuildCosmicNebulae()
        {
            GameObject nebRoot = new GameObject("CosmicNebulae");
            nebRoot.transform.SetParent(environmentRoot.transform);

            Vector3[] nebPositions = {
                new Vector3(-14f, 12f, 28f),
                new Vector3(18f, 16f, 26f),
                new Vector3(0f, 22f, 32f)
            };

            Color[] nebColors = {
                new Color(0.08f, 0.03f, 0.18f, 0.06f), // Deep cosmic violet
                new Color(0.01f, 0.08f, 0.16f, 0.06f), // Cyan cosmic gas
                new Color(0.04f, 0.06f, 0.14f, 0.05f)  // Midnight blue
            };

            for (int i = 0; i < nebPositions.Length; i++)
            {
                GameObject neb = GameObject.CreatePrimitive(PrimitiveType.Quad);
                neb.name = $"Nebula_{i}";
                neb.transform.SetParent(nebRoot.transform);
                neb.transform.position = nebPositions[i];
                neb.transform.LookAt(Vector3.zero);
                neb.transform.localScale = new Vector3(25f, 18f, 1f);

                Collider col = neb.GetComponent<Collider>();
                if (col != null) Destroy(col);

                MeshRenderer mr = neb.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    Material nm = new Material(Shader.Find("Unlit/Color") ?? Shader.Find("Standard"));
                    nm.color = nebColors[i];
                    SetMaterialTransparent(nm);
                    mr.material = nm;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                }
            }
        }

        private Material CreateUnlitLineMaterial()
        {
            Shader shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Legacy Shaders/Diffuse");

            Material mat = new Material(shader);
            mat.color = Color.white;
            return mat;
        }

        private void SetMaterialTransparent(Material mat)
        {
            if (mat.HasProperty("_Mode"))
            {
                mat.SetFloat("_Mode", 3); // Transparent
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = 3000;
            }
        }
    }
}
