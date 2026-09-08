using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.UI;
using VRCAD.Core;

namespace VRCAD.UI
{
    /// <summary>
    /// Builds the complete VR CAD spatial UI matching the reference design:
    /// - Clean top bar (logo, selection mode, status indicator, undo/redo/settings/close)
    /// - 8 feature panels in a single horizontal row
    /// - Refined COLOUR card matching reference design:
    ///   * 12 color swatches (4x3 grid)
    ///   * Slot select with < [☑] > controls
    ///   * Sleek glowing Roughness and Metallic sliders with pill thumbs
    ///   * TEXTURE & RENDER OPTIONS section
    ///   * Brushed CHROME PRESET button
    ///   * OPACITY slider
    ///   * WIREFRAME VIEW toggle (vector wireframe view where only the wireframe is visible)
    /// - Bottom action dock bar with quick-access buttons
    /// - Interactive dimension editing via steppers and floating numpad
    /// </summary>
    public class CAD_UIManager : MonoBehaviour
    {
        [Header("Canvas Placement (Positioned in background behind grid workplane)")]
        [SerializeField] private Vector3 spawnOffset = new Vector3(0, 1.45f, 2.85f);
        [SerializeField] private float canvasScale = 0.00085f;

        [Header("State Tracking")]
        private Color currentColor = new Color(0.18f, 0.45f, 0.95f, 1.0f);
        private float roughnessVal = 0.35f;
        private float metallicVal = 0.1f;
        private float opacityVal = 1.0f;
        private bool isWireframeActive = false;

        // Sliders
        private Slider roughnessSlider;
        private Slider metallicSlider;
        private Slider opacitySlider;
        private TextMeshProUGUI roughnessValText;
        private TextMeshProUGUI metallicValText;
        private TextMeshProUGUI opacityValText;        // Wireframe toggle UI
        private Image wireframeToggleBg;
        private RectTransform wireframeToggleKnob;
        private TextMeshProUGUI wireframeIconSolid;
        private TextMeshProUGUI wireframeIconWire;

        // Transform readout text references
        private TextMeshProUGUI posXText, posYText, posZText;
        private TextMeshProUGUI rotXText, rotYText, rotZText;
        private TextMeshProUGUI scaleXText, scaleYText, scaleZText;

        // Status bar & Selection
        private TextMeshProUGUI statusText;
        private TextMeshProUGUI selectionModeText;

        // Snap state
        private float activeGridSnap = 0.005f;
        private float activeAngleSnap = 15f;
        private readonly Dictionary<string, Button> gridSnapButtons = new Dictionary<string, Button>();
        private readonly Dictionary<string, Button> angleSnapButtons = new Dictionary<string, Button>();
        private readonly Dictionary<string, Button> axisLockButtons = new Dictionary<string, Button>();
        private AxisConstraint currentAxisLock = AxisConstraint.None;

        // Selection mode buttons
        private readonly Dictionary<string, Button> selectionModeButtons = new Dictionary<string, Button>();
        private SelectionMode currentSelectionMode = SelectionMode.Object;

        // Color swatches tracking (12 colors)
        private readonly List<Image> swatchOutlines = new List<Image>();
        private int selectedSwatchIndex = 5; // Royal Blue by default

        // Primitives active tracking
        private Button activePrimitiveBtn;

        // Numeric Dimension Numpad Popup
        private GameObject numpadPanel;
        private TextMeshProUGUI numpadTitleText;
        private TextMeshProUGUI numpadDisplayText;
        private string numpadCurrentInput = "0";
        private int numpadTargetSection = 0; // 0: Pos, 1: Rot, 2: Scale
        private int numpadTargetAxis = 0;    // 0: X, 1: Y, 2: Z

        // ──────────────────────────────────────────────────
        //  COLOR PALETTE — Sleek dark navy glassmorphism
        // ──────────────────────────────────────────────────
        private readonly Color colCardBg         = new Color(0.05f, 0.07f, 0.12f, 0.95f);
        private readonly Color colInsetField     = new Color(0.03f, 0.04f, 0.08f, 1.0f);
        private readonly Color colBtnNormal      = new Color(0.08f, 0.11f, 0.18f, 1.0f);
        private readonly Color colBtnHover       = new Color(0.14f, 0.20f, 0.32f, 1.0f);
        private readonly Color colBtnBorder      = new Color(0.12f, 0.18f, 0.28f, 1.0f);
        private readonly Color colCyanActive     = new Color(0.00f, 0.70f, 0.98f, 1.0f);
        private readonly Color colCyanGlow       = new Color(0.00f, 0.85f, 1.00f, 1.0f);
        private readonly Color colSilverGlow     = new Color(0.85f, 0.88f, 0.94f, 1.0f);
        private readonly Color colTextLight      = new Color(0.92f, 0.95f, 0.98f, 1.0f);
        private readonly Color colTextMuted      = new Color(0.50f, 0.58f, 0.68f, 1.0f);
        private readonly Color colTopBar         = new Color(0.04f, 0.06f, 0.10f, 0.97f);
        private readonly Color colAxisRed        = new Color(0.90f, 0.20f, 0.20f, 1.0f);
        private readonly Color colAxisGreen      = new Color(0.18f, 0.78f, 0.28f, 1.0f);
        private readonly Color colAxisBlue       = new Color(0.20f, 0.45f, 0.95f, 1.0f);
        private readonly Color colGreenExport    = new Color(0.12f, 0.58f, 0.25f, 1.0f);
        private readonly Color colRedClear       = new Color(0.85f, 0.18f, 0.18f, 1.0f);
        private readonly Color colBlueAction     = new Color(0.14f, 0.45f, 0.88f, 1.0f);
        private readonly Color colDockBg         = new Color(0.04f, 0.06f, 0.10f, 0.97f);
        private readonly Color colCardBorderGlow = new Color(0.00f, 0.50f, 0.80f, 0.25f);

        // Layout constants — Carefully tuned with NO overlaps
        private const float TOP_BAR_W = 1460f;
        private const float TOP_BAR_H = 44f;
        private const float TOP_BAR_Y = 240f;

        private const float PANEL_ROW_GAP = 5f;
        private const float CARDS_CENTER_Y = 40f;
        private const float CARD_HEIGHT = 330f;

        private const float DOCK_H = 54f;
        private const float DOCK_Y = -162f;

        // ──────────────────────────────────────────────────
        //  LIFECYCLE
        // ──────────────────────────────────────────────────
        private void Start()
        {
            SetupEnvironment();
            BuildCompleteUI();
            SubscribeToEvents();
        }

        private void Update()
        {
            UpdateLiveTransformReadouts();
        }

        private void SetupEnvironment()
        {
            CAD_EnvironmentManager env = FindObjectOfType<CAD_EnvironmentManager>();
            if (env == null)
            {
                GameObject envObj = new GameObject("CAD_EnvironmentSetup");
                env = envObj.AddComponent<CAD_EnvironmentManager>();
            }
        }

        // ──────────────────────────────────────────────────
        //  MAIN UI BUILD
        // ──────────────────────────────────────────────────
        private void BuildCompleteUI()
        {
            GameObject existingCanvas = GameObject.Find("CAD_VR_Canvas");
            if (existingCanvas != null)
            {
                Destroy(existingCanvas);
            }

            GameObject canvasObj = new GameObject("CAD_VR_Canvas");
            canvasObj.transform.position = spawnOffset;
            canvasObj.transform.rotation = Quaternion.Euler(4.0f, 0, 0);

            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            if (canvas.worldCamera == null && Camera.main != null)
            {
                canvas.worldCamera = Camera.main;
            }

            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 10;
            scaler.referencePixelsPerUnit = 100;

            // Non-blocking raycasters: ensure 3D physics colliders never occlude UI graphic raycasts
            GraphicRaycaster gr = canvasObj.AddComponent<GraphicRaycaster>();
            gr.blockingObjects = GraphicRaycaster.BlockingObjects.None;

            TrackedDeviceGraphicRaycaster trackedRaycaster = canvasObj.AddComponent<TrackedDeviceGraphicRaycaster>();
            trackedRaycaster.blockingMask = 0; // LayerMask 0 = Nothing blocks raycasts

            RectTransform canvasRect = canvasObj.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(TOP_BAR_W + 40, 560);
            canvasRect.localScale = Vector3.one * canvasScale;

            // Physics & Colliders: automatically attach BoxCollider matching RectTransform bounds.
            // Z-center is offset slightly behind local Z = 0 so laser pointers always hit UI elements first.
            float panelDepth = 16f;
            BoxCollider col = canvasObj.AddComponent<BoxCollider>();
            col.size = new Vector3(canvasRect.sizeDelta.x, canvasRect.sizeDelta.y, panelDepth);
            col.center = new Vector3(0, 0, panelDepth * 0.5f + 1.0f);

            // Floating Rigidbody: isKinematic = true and useGravity = false so the panel floats in mid-air
            Rigidbody rb = canvasObj.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.isKinematic = true;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            // Grab Mechanics: XRGrabInteractable for smooth repositioning, rotation, and non-snapping dynamic attach
            XRGrabInteractable grab = canvasObj.AddComponent<XRGrabInteractable>();
            grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
            grab.throwOnDetach = false;
            grab.retainTransformParent = true;
            grab.useDynamicAttach = true;

            // Zoom Mechanics (Distance/Scale): attach CAD_UIPanelManipulator to read thumbstick up/down
            CAD_UIPanelManipulator zoomManipulator = canvasObj.AddComponent<CAD_UIPanelManipulator>();
            grab.AddSingleGrabTransformer(zoomManipulator);

            BuildTopBar(canvasObj.transform);
            BuildPanelsRow(canvasObj.transform);
            BuildBottomDock(canvasObj.transform);
            BuildNumpadModal(canvasObj.transform);
        }

        // ════════════════════════════════════════════════════════
        //  TOP BAR (Clean, non-colliding header)
        // ════════════════════════════════════════════════════════

        #region Top Bar

        private void BuildTopBar(Transform parent)
        {
            GameObject bar = CreateUIPanel("TopBar", parent, new Vector2(0, TOP_BAR_Y), new Vector2(TOP_BAR_W, TOP_BAR_H), colTopBar);

            // Glowing bottom border
            CreateUIPanel("TopBarGlow", bar.transform, new Vector2(0, -TOP_BAR_H * 0.5f + 1), new Vector2(TOP_BAR_W, 2), colCardBorderGlow);

            // Brand / Logo matching reference screenshot: [☑] VR CAD 3D MODELING
            GameObject checkIcon = CreateUIPanel("LogoCheck", bar.transform, new Vector2(-TOP_BAR_W * 0.5f + 25, 0), new Vector2(22, 22), colBtnNormal);
            CreateTMPText("CheckMark", checkIcon.transform, "☑", 15, TextAlignmentOptions.Center, colCyanActive, Vector2.zero, new Vector2(22, 22));

            CreateTMPText("LogoText", bar.transform, "<b>VR CAD</b>", 16, TextAlignmentOptions.Left, colTextLight, new Vector2(-TOP_BAR_W * 0.5f + 85, 5), new Vector2(90, 20));
            CreateTMPText("LogoSub", bar.transform, "<size=8><b>3D MODELING</b></size>", 8, TextAlignmentOptions.Left, colTextMuted, new Vector2(-TOP_BAR_W * 0.5f + 85, -9), new Vector2(90, 14));

            // Selection Mode Indicator Pill
            float pillX = -60f;
            GameObject pill = CreateUIPanel("SelectionPill", bar.transform, new Vector2(pillX, 0), new Vector2(190, 28), colBtnNormal);
            CreateTMPText("PillLabel", pill.transform, "<size=10>Mode :</size>", 10, TextAlignmentOptions.Left, colTextMuted, new Vector2(-45, 0), new Vector2(50, 24));
            selectionModeText = CreateTMPText("PillValue", pill.transform, "<b>Object</b>", 11, TextAlignmentOptions.Left, colCyanActive, new Vector2(10, 0), new Vector2(65, 24));
            CreateTMPText("PillChevron", pill.transform, "▾", 10, TextAlignmentOptions.Right, colTextMuted, new Vector2(75, 0), new Vector2(16, 24));

            // Status Indicator Pill
            float statusX = 140f;
            GameObject statusPill = CreateUIPanel("StatusPill", bar.transform, new Vector2(statusX, 0), new Vector2(150, 28), new Color(0.06f, 0.09f, 0.14f, 0.85f));
            CreateTMPText("StatusDot", statusPill.transform, "●", 9, TextAlignmentOptions.Center, new Color(0.2f, 0.9f, 0.35f, 1f), new Vector2(-60, 0), new Vector2(16, 24));
            statusText = CreateTMPText("StatusText", statusPill.transform, "<size=11>Ready</size>", 11, TextAlignmentOptions.Left, colTextLight, new Vector2(10, 0), new Vector2(110, 24));

            // Right Action Buttons: Undo, Redo, Settings, Close
            float rx = TOP_BAR_W * 0.5f - 25;
            CreateTopBarBtn("Btn_Close", bar.transform, new Vector2(rx, 0), "✕", colRedClear, () => CADManagerHub.Instance?.EmitStatus("CAD Session Running"));
            CreateTopBarBtn("Btn_Settings", bar.transform, new Vector2(rx - 38, 0), "⚙", colTextMuted, () => CADManagerHub.Instance?.EmitStatus("Settings"));
            CreateTopBarBtn("Btn_Redo", bar.transform, new Vector2(rx - 76, 0), "↷", colTextMuted, () => CADManagerHub.Instance?.Redo());
            CreateTopBarBtn("Btn_Undo", bar.transform, new Vector2(rx - 114, 0), "↶", colTextMuted, () => CADManagerHub.Instance?.Undo());
        }

        private void CreateTopBarBtn(string name, Transform parent, Vector2 pos, string icon, Color iconColor, Action onClick)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            RectTransform rect = obj.AddComponent<RectTransform>();
            rect.anchoredPosition = pos;
            rect.sizeDelta = new Vector2(30, 28);

            Image img = obj.AddComponent<Image>();
            img.color = Color.clear;

            Button btn = obj.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor = Color.clear;
            cb.highlightedColor = new Color(1, 1, 1, 0.08f);
            cb.pressedColor = colCyanActive * 0.4f;
            btn.colors = cb;
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            CreateTMPText("Icon", obj.transform, icon, 14, TextAlignmentOptions.Center, iconColor, Vector2.zero, new Vector2(30, 28));
        }

        #endregion

        // ════════════════════════════════════════════════════════
        //  PANELS ROW — All 8 panels in one single horizontal strip
        // ════════════════════════════════════════════════════════

        #region Panels Row

        private void BuildPanelsRow(Transform parent)
        {
            // Panel widths: COLOUR panel is 176px wide to comfortably host the 4x3 swatches and render options
            float[] widths = { 176f, 160f, 260f, 190f, 190f, 120f, 150f, 175f };

            float totalW = 0;
            for (int i = 0; i < widths.Length; i++) totalW += widths[i];
            totalW += (widths.Length - 1) * PANEL_ROW_GAP;

            float x = -totalW * 0.5f;

            for (int i = 0; i < widths.Length; i++)
            {
                float cx = x + widths[i] * 0.5f;
                Vector2 pos = new Vector2(cx, CARDS_CENTER_Y);
                Vector2 size = new Vector2(widths[i], CARD_HEIGHT);

                switch (i)
                {
                    case 0: BuildColourCard(parent, pos, size); break;
                    case 1: BuildCreateCard(parent, pos, size); break;
                    case 2: BuildTransformCard(parent, pos, size); break;
                    case 3: BuildSnapCard(parent, pos, size); break;
                    case 4: BuildOperationsCard(parent, pos, size); break;
                    case 5: BuildSelectionCard(parent, pos, size); break;
                    case 6: BuildUtilityCard(parent, pos, size); break;
                    case 7: BuildFileCard(parent, pos, size); break;
                }

                x += widths[i] + PANEL_ROW_GAP;
            }
        }

        // ─── 1. REFINED COLOUR CARD (Exact match to target design) ───
        private void BuildColourCard(Transform parent, Vector2 pos, Vector2 size)
        {
            GameObject card = CreateCardPanel("Card_Colour", parent, pos, size, "• COLOUR", true);

            // 12 Refined Color Presets (4 cols × 3 rows) matching Image 2
            Color[] presets = {
                // Row 1: Red, Orange, Yellow, Green
                new Color(0.92f, 0.20f, 0.20f),
                new Color(0.98f, 0.52f, 0.05f),
                new Color(0.98f, 0.85f, 0.12f),
                new Color(0.18f, 0.78f, 0.32f),
                // Row 2: Sky Cyan, Royal Blue, Pure White, Silver Gray
                new Color(0.00f, 0.75f, 0.95f),
                new Color(0.18f, 0.45f, 0.95f),
                new Color(1.00f, 1.00f, 1.00f),
                new Color(0.82f, 0.84f, 0.88f),
                // Row 3: Lime, Teal/Mint, Purple, Magenta
                new Color(0.50f, 0.88f, 0.22f),
                new Color(0.18f, 0.75f, 0.65f),
                new Color(0.65f, 0.28f, 0.88f),
                new Color(0.88f, 0.28f, 0.75f),
            };

            float swW = 34f;
            float swH = 24f;
            float swGap = 4f;
            float totalSwW = 4 * swW + 3 * swGap;
            float swStartX = -totalSwW * 0.5f + swW * 0.5f;

            swatchOutlines.Clear();
            for (int i = 0; i < presets.Length; i++)
            {
                int idx = i;
                int row = i / 4;
                int col = i % 4;
                float sx = swStartX + col * (swW + swGap);
                float sy = 118f - row * (swH + swGap);

                Color c = presets[i];
                GameObject swObj = CreateSwatchButton($"Swatch_{i}", card.transform, new Vector2(sx, sy), new Vector2(swW, swH), c, (i == selectedSwatchIndex), () =>
                {
                    selectedSwatchIndex = idx;
                    SetColorFromPreset(c);
                    UpdateSwatchOutlines();
                });

                Image outline = swObj.transform.Find("Outline")?.GetComponent<Image>();
                if (outline != null) swatchOutlines.Add(outline);
            }

            // ─── Slot Select Row: < [☑ SLOT SELECT] > ───
            float slotY = 32f;
            CreateMaterialSlotButton("SlotPrev", card.transform, new Vector2(-46, slotY), "<", new Color(0.15f, 0.35f, 0.65f), () => CADManagerHub.Instance?.EmitStatus("Prev Material Slot"));

            // Center: Checkbox with SLOT SELECT label
            GameObject slotCenter = CreateUIPanel("SlotCenter", card.transform, new Vector2(0, slotY), new Vector2(54, 28), new Color(0.06f, 0.08f, 0.14f, 1f));
            CreateUIPanel("SlotBdr", slotCenter.transform, Vector2.zero, new Vector2(54, 28), colBtnBorder).transform.SetAsFirstSibling();
            GameObject checkIcon = CreateUIPanel("Chk", slotCenter.transform, new Vector2(0, 5), new Vector2(16, 14), colBtnNormal);
            CreateTMPText("ChkMark", checkIcon.transform, "☑", 11, TextAlignmentOptions.Center, colCyanActive, Vector2.zero, new Vector2(16, 14));
            CreateTMPText("SlotLbl", slotCenter.transform, "<size=7><b>SLOT SELECT</b></size>", 7, TextAlignmentOptions.Center, colTextMuted, new Vector2(0, -8), new Vector2(54, 10));

            CreateMaterialSlotButton("SlotNext", card.transform, new Vector2(46, slotY), ">", new Color(0.45f, 0.35f, 0.25f), () => CADManagerHub.Instance?.EmitStatus("Next Material Slot"));

            // ─── Sliders: Roughness & Metallic with sleek glowing pill handles ───
            CreateGlowingSlider(card.transform, "Roughness", roughnessVal, new Vector2(0, -4), size.x - 20f, colCyanGlow, (val) =>
            {
                roughnessVal = val;
                CADManagerHub.Instance?.SetSelectedMaterialProperties(roughnessVal, metallicVal);
            }, out roughnessSlider, out roughnessValText);

            CreateGlowingSlider(card.transform, "Metallic", metallicVal, new Vector2(0, -29), size.x - 20f, colSilverGlow, (val) =>
            {
                metallicVal = val;
                CADManagerHub.Instance?.SetSelectedMaterialProperties(roughnessVal, metallicVal);
            }, out metallicSlider, out metallicValText);

            // ─── TEXTURE & RENDER OPTIONS Section ───
            CreateUIPanel("SepRender", card.transform, new Vector2(0, -48), new Vector2(size.x - 14, 1), new Color(0.12f, 0.18f, 0.28f, 0.6f));
            CreateTMPText("RenderHdr", card.transform, "<size=8.5><b>• TEXTURE & RENDER OPTIONS</b></size>", 8.5f, TextAlignmentOptions.Left, colTextLight, new Vector2(-size.x * 0.5f + 12 + (size.x - 24) * 0.5f, -59), new Vector2(size.x - 24, 16));

            // Brushed CHROME PRESET Button
            CreateChromePresetButton(card.transform, new Vector2(0, -75), new Vector2(size.x - 22, 21));

            // RESET TEXTURES & EFFECTS Options Button
            CreateResetOptionsButton(card.transform, new Vector2(0, -98), new Vector2(size.x - 22, 21));

            // OPACITY Slider
            CreateGlowingSlider(card.transform, "OPACITY", opacityVal, new Vector2(0, -121), size.x - 20f, colCyanGlow, (val) =>
            {
                opacityVal = val;
                CADManagerHub.Instance?.SetSelectedOpacity(opacityVal);
            }, out opacitySlider, out opacityValText);

            // WIREFRAME VIEW Toggle with Icons
            BuildWireframeToggleRow(card.transform, new Vector2(0, -145), size.x - 20f);
        }

        private void CreateMaterialSlotButton(string name, Transform parent, Vector2 pos, string arrow, Color slotPreviewColor, Action onClick)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            RectTransform rect = obj.AddComponent<RectTransform>();
            rect.anchoredPosition = pos;
            rect.sizeDelta = new Vector2(28, 26);

            Image img = obj.AddComponent<Image>();
            img.color = slotPreviewColor * 0.7f;

            Button btn = obj.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor = slotPreviewColor * 0.7f;
            cb.highlightedColor = slotPreviewColor;
            cb.pressedColor = colCyanActive;
            btn.colors = cb;
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            // Dark overlay + arrow
            GameObject overlay = CreateUIPanel("Ovl", obj.transform, Vector2.zero, new Vector2(28, 26), new Color(0, 0, 0, 0.45f));
            CreateTMPText("Arr", overlay.transform, $"<b>{arrow}</b>", 11, TextAlignmentOptions.Center, colTextLight, Vector2.zero, new Vector2(28, 26));
        }

        private void CreateChromePresetButton(Transform parent, Vector2 pos, Vector2 size)
        {
            GameObject btnObj = new GameObject("Btn_ChromePreset");
            btnObj.transform.SetParent(parent, false);
            RectTransform rect = btnObj.AddComponent<RectTransform>();
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;

            // Brushed silver metallic look
            Image img = btnObj.AddComponent<Image>();
            img.color = new Color(0.78f, 0.82f, 0.88f, 1f);

            // Specular top highlight line
            CreateUIPanel("Highlight", btnObj.transform, new Vector2(0, size.y * 0.5f - 1), new Vector2(size.x, 2), new Color(1f, 1f, 1f, 0.75f));
            // Shadow bottom line
            CreateUIPanel("Shadow", btnObj.transform, new Vector2(0, -size.y * 0.5f + 1), new Vector2(size.x, 2), new Color(0.35f, 0.38f, 0.45f, 1f));

            Button btn = btnObj.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor = new Color(0.78f, 0.82f, 0.88f, 1f);
            cb.highlightedColor = new Color(0.92f, 0.95f, 1.0f, 1f);
            cb.pressedColor = new Color(0.65f, 0.70f, 0.78f, 1f);
            btn.colors = cb;

            btn.onClick.AddListener(() =>
            {
                CADManagerHub.Instance?.ApplyChromePresetToSelection();
                if (roughnessSlider != null) roughnessSlider.value = 0.02f;
                if (metallicSlider != null) metallicSlider.value = 1.0f;
                if (roughnessValText != null) roughnessValText.text = "0.02";
                if (metallicValText != null) metallicValText.text = "1.00";
            });

            CreateTMPText("Lbl", btnObj.transform, "<b>CHROME PRESET</b>", 9.5f, TextAlignmentOptions.Center, new Color(0.08f, 0.10f, 0.15f, 1f), Vector2.zero, size);
        }

        private void CreateResetOptionsButton(Transform parent, Vector2 pos, Vector2 size)
        {
            GameObject btnObj = new GameObject("Btn_ResetOptions");
            btnObj.transform.SetParent(parent, false);
            RectTransform rect = btnObj.AddComponent<RectTransform>();
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;

            // Deep navy sleek glassmorphic panel
            Image img = btnObj.AddComponent<Image>();
            img.color = new Color(0.08f, 0.12f, 0.20f, 1f);

            // Subtle cyan border
            CreateUIPanel("Bdr", btnObj.transform, Vector2.zero, size, new Color(0.00f, 0.65f, 0.95f, 0.50f)).transform.SetAsFirstSibling();

            // Specular top highlight line
            CreateUIPanel("TopHighlight", btnObj.transform, new Vector2(0, size.y * 0.5f - 1), new Vector2(size.x - 2, 1.5f), new Color(0.00f, 0.85f, 1.0f, 0.40f));

            Button btn = btnObj.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor = new Color(0.08f, 0.12f, 0.20f, 1f);
            cb.highlightedColor = new Color(0.14f, 0.22f, 0.35f, 1f);
            cb.pressedColor = new Color(0.00f, 0.50f, 0.85f, 1f);
            btn.colors = cb;

            btn.onClick.AddListener(() =>
            {
                CADManagerHub.Instance?.ResetSelectedMaterialAndEffects(currentColor);

                roughnessVal = 0.35f;
                if (roughnessSlider != null) roughnessSlider.value = 0.35f;
                if (roughnessValText != null) roughnessValText.text = "0.35";

                metallicVal = 0.10f;
                if (metallicSlider != null) metallicSlider.value = 0.10f;
                if (metallicValText != null) metallicValText.text = "0.10";

                opacityVal = 1.0f;
                if (opacitySlider != null) opacitySlider.value = 1.0f;
                if (opacityValText != null) opacityValText.text = "1.00";

                isWireframeActive = false;
                UpdateWireframeToggleVisual();
            });

            CreateTMPText("Lbl", btnObj.transform, "<b>↺ RESET OPTIONS</b>", 9.0f, TextAlignmentOptions.Center, new Color(0.85f, 0.94f, 1.0f, 1f), Vector2.zero, size);
        }

        private void BuildWireframeToggleRow(Transform parent, Vector2 pos, float width)
        {
            GameObject row = new GameObject("WireframeRow");
            row.transform.SetParent(parent, false);
            RectTransform rect = row.AddComponent<RectTransform>();
            rect.anchoredPosition = pos;
            rect.sizeDelta = new Vector2(width, 22);

            CreateTMPText("Lbl", row.transform, "<b>WIREFRAME VIEW</b>", 9, TextAlignmentOptions.Left, colTextLight, new Vector2(-width * 0.5f + 45, 0), new Vector2(90, 18));

            // Switch Toggle Button: [  ○ ]
            GameObject switchObj = new GameObject("SwitchToggle");
            switchObj.transform.SetParent(row.transform, false);
            RectTransform swRect = switchObj.AddComponent<RectTransform>();
            swRect.anchoredPosition = new Vector2(15, 0);
            swRect.sizeDelta = new Vector2(34, 16);

            wireframeToggleBg = switchObj.AddComponent<Image>();
            wireframeToggleBg.sprite = GetCapsuleSprite();
            wireframeToggleBg.type = Image.Type.Sliced;
            wireframeToggleBg.color = colBtnNormal;

            Button switchBtn = switchObj.AddComponent<Button>();

            // Circular Knob
            GameObject knob = CreateUIPanel("Knob", switchObj.transform, new Vector2(-8, 0), new Vector2(12, 12), Color.white);
            Image knobImg = knob.GetComponent<Image>();
            knobImg.sprite = GetCircleSprite();
            wireframeToggleKnob = knob.GetComponent<RectTransform>();

            // Icons on right: Solid Sphere vs Wireframe Sphere
            wireframeIconSolid = CreateTMPText("IconSolid", row.transform, "●", 12, TextAlignmentOptions.Center, colTextLight, new Vector2(width * 0.5f - 24, 0), new Vector2(16, 16));
            wireframeIconWire = CreateTMPText("IconWire", row.transform, "⬡", 12, TextAlignmentOptions.Center, colTextMuted, new Vector2(width * 0.5f - 8, 0), new Vector2(16, 16));

            switchBtn.onClick.AddListener(() =>
            {
                isWireframeActive = !isWireframeActive;
                UpdateWireframeToggleVisual();
                CADManagerHub.Instance?.SetWireframeMode(isWireframeActive);
            });
        }

        private void UpdateWireframeToggleVisual()
        {
            if (wireframeToggleBg != null)
                wireframeToggleBg.color = isWireframeActive ? colCyanActive : colBtnNormal;

            if (wireframeToggleKnob != null)
                wireframeToggleKnob.anchoredPosition = new Vector2(isWireframeActive ? 8 : -8, 0);

            if (wireframeIconSolid != null)
                wireframeIconSolid.color = isWireframeActive ? colTextMuted : colTextLight;

            if (wireframeIconWire != null)
                wireframeIconWire.color = isWireframeActive ? colCyanActive : colTextMuted;
        }

        private static Sprite circleSprite;
        private static Sprite capsuleSprite;

        public static Sprite GetCircleSprite()
        {
            if (circleSprite != null) return circleSprite;

            int size = 64;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.name = "CAD_CircleSprite";
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            float center = (size - 1) * 0.5f;
            float radius = center - 1.5f;
            Color[] cols = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    float alpha = Mathf.Clamp01(radius + 1.2f - dist);
                    cols[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels(cols);
            tex.Apply();
            circleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            return circleSprite;
        }

        public static Sprite GetCapsuleSprite()
        {
            if (capsuleSprite != null) return capsuleSprite;

            int w = 64;
            int h = 32;
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.name = "CAD_CapsuleSprite";
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            float r = (h - 1) * 0.5f - 1.5f;
            Color[] cols = new Color[w * h];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float cx = (x < r + 2f) ? (r + 2f) : ((x > w - 1f - (r + 2f)) ? (w - 1f - (r + 2f)) : x);
                    float cy = (h - 1) * 0.5f;
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy));
                    float alpha = Mathf.Clamp01(r + 1.2f - dist);
                    cols[y * w + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels(cols);
            tex.Apply();
            capsuleSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(16, 15, 16, 15));
            return capsuleSprite;
        }

        /// <summary>
        /// Creates a sleek slider with rounded track and smooth circular handle knob matching the reference design.
        /// </summary>
        private void CreateGlowingSlider(Transform parent, string label, float initialVal, Vector2 pos, float width, Color glowColor, Action<float> onValueChanged, out Slider outSlider, out TextMeshProUGUI outValText)
        {
            GameObject row = new GameObject("SliderRow_" + label);
            row.transform.SetParent(parent, false);
            RectTransform rect = row.AddComponent<RectTransform>();
            rect.anchoredPosition = pos;
            rect.sizeDelta = new Vector2(width, 20);

            CreateTMPText("Lbl", row.transform, $"<size=9>{label}</size>", 9, TextAlignmentOptions.Left, colTextMuted, new Vector2(-width * 0.5f + 25, 0), new Vector2(50, 18));
            outValText = CreateTMPText("Val", row.transform, $"{initialVal:F2}", 9, TextAlignmentOptions.Right, colTextLight, new Vector2(width * 0.5f - 14, 0), new Vector2(28, 18));

            // Slider Root
            float trackW = width - 82f;
            GameObject sliderObj = new GameObject("Slider");
            sliderObj.transform.SetParent(row.transform, false);
            RectTransform slRect = sliderObj.AddComponent<RectTransform>();
            slRect.anchoredPosition = new Vector2(8, 0);
            slRect.sizeDelta = new Vector2(trackW, 16);

            Slider slider = sliderObj.AddComponent<Slider>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;

            // Background Track with smooth rounded capsule ends
            GameObject bgTrack = CreateUIPanel("Background", sliderObj.transform, Vector2.zero, Vector2.zero, new Color(0.04f, 0.07f, 0.12f, 0.9f));
            RectTransform bgRect = bgTrack.GetComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0, 0.5f);
            bgRect.anchorMax = new Vector2(1, 0.5f);
            bgRect.sizeDelta = new Vector2(0, 6);
            Image bgImg = bgTrack.GetComponent<Image>();
            bgImg.sprite = GetCapsuleSprite();
            bgImg.type = Image.Type.Sliced;

            // Fill Area
            GameObject fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(sliderObj.transform, false);
            RectTransform fillAreaRect = fillArea.AddComponent<RectTransform>();
            fillAreaRect.anchorMin = new Vector2(0, 0.5f);
            fillAreaRect.anchorMax = new Vector2(1, 0.5f);
            fillAreaRect.offsetMin = new Vector2(0, -3);
            fillAreaRect.offsetMax = new Vector2(0, 3);

            // Active glowing fill with rounded ends
            GameObject fill = CreateUIPanel("Fill", fillArea.transform, Vector2.zero, Vector2.zero, glowColor);
            RectTransform fRect = fill.GetComponent<RectTransform>();
            fRect.anchorMin = Vector2.zero;
            fRect.anchorMax = Vector2.one;
            fRect.sizeDelta = Vector2.zero;
            Image fillImg = fill.GetComponent<Image>();
            fillImg.sprite = GetCapsuleSprite();
            fillImg.type = Image.Type.Sliced;
            slider.fillRect = fRect;

            // Handle Slide Area
            GameObject handleSlideArea = new GameObject("Handle Slide Area");
            handleSlideArea.transform.SetParent(sliderObj.transform, false);
            RectTransform handleAreaRect = handleSlideArea.AddComponent<RectTransform>();
            handleAreaRect.anchorMin = Vector2.zero;
            handleAreaRect.anchorMax = Vector2.one;
            handleAreaRect.offsetMin = new Vector2(6, 0);
            handleAreaRect.offsetMax = new Vector2(-6, 0);

            // Crisp Circular Handle Knob matching target reference image
            GameObject handle = CreateUIPanel("Handle", handleSlideArea.transform, Vector2.zero, new Vector2(13, 13), Color.white);
            RectTransform hRect = handle.GetComponent<RectTransform>();
            hRect.sizeDelta = new Vector2(13, 13);
            Image handleImg = handle.GetComponent<Image>();
            handleImg.sprite = GetCircleSprite();
            slider.handleRect = hRect;
            slider.targetGraphic = handleImg;

            TextMeshProUGUI vRef = outValText;
            slider.onValueChanged.AddListener((v) =>
            {
                vRef.text = $"{v:F2}";
                onValueChanged?.Invoke(v);
            });

            slider.value = initialVal;
            outSlider = slider;
        }

        private void UpdateSwatchOutlines()
        {
            for (int i = 0; i < swatchOutlines.Count; i++)
            {
                if (swatchOutlines[i] != null)
                {
                    swatchOutlines[i].color = (i == selectedSwatchIndex) ? Color.white : Color.clear;
                }
            }
        }

        // ─── 2. CREATE CARD ───
        private void BuildCreateCard(Transform parent, Vector2 pos, Vector2 size)
        {
            GameObject card = CreateCardPanel("Card_Create", parent, pos, size, "◈ CREATE", true);

            string[] names = { "Box", "Cylinder", "Sphere", "Cone", "Prism", "Wedge" };
            string[] icons = { "◻", "⬡", "●", "▲", "▷", "◢" };
            CADShapeType[] types = { CADShapeType.Box, CADShapeType.Cylinder, CADShapeType.Sphere, CADShapeType.Cone, CADShapeType.Prism, CADShapeType.Wedge };

            float btnW = 46f;
            float btnH = 50f;
            float gap = 5f;

            int cols = 3;
            float totalRowW = cols * btnW + (cols - 1) * gap;
            float sx = -totalRowW * 0.5f + btnW * 0.5f;

            for (int i = 0; i < names.Length; i++)
            {
                int row = i / cols;
                int col = i % cols;
                float bx = sx + col * (btnW + gap);
                float by = 75f - row * (btnH + gap + 10);
                bool active = (i == 0);

                CreatePrimitiveButton($"Prim_{names[i]}", card.transform, new Vector2(bx, by), new Vector2(btnW, btnH),
                    icons[i], names[i], types[i], active);
            }
        }

        private void CreatePrimitiveButton(string name, Transform parent, Vector2 pos, Vector2 size, string icon, string label, CADShapeType shapeType, bool activeByDefault)
        {
            Color bgCol = activeByDefault ? colCyanActive : colBtnNormal;

            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            RectTransform rect = obj.AddComponent<RectTransform>();
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;

            Image img = obj.AddComponent<Image>();
            img.color = bgCol;

            Button btn = obj.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor = bgCol;
            cb.highlightedColor = bgCol * 1.25f;
            cb.pressedColor = colCyanActive;
            btn.colors = cb;

            if (activeByDefault) activePrimitiveBtn = btn;

            btn.onClick.AddListener(() =>
            {
                if (activePrimitiveBtn != null && activePrimitiveBtn != btn) SetBtnColor(activePrimitiveBtn, colBtnNormal);
                SetBtnColor(btn, colCyanActive);
                activePrimitiveBtn = btn;
                CADManagerHub.Instance?.CreatePrimitive(shapeType);
            });

            CreateTMPText("Icon", obj.transform, icon, 16, TextAlignmentOptions.Center, colTextLight, new Vector2(0, 7), new Vector2(size.x, 22));
            CreateTMPText("Label", obj.transform, $"<size=8>{label}</size>", 8, TextAlignmentOptions.Center, colTextLight, new Vector2(0, -12), new Vector2(size.x, 14));
        }

        // ─── 3. TRANSFORM CARD (Interactive Dimensions & Steppers) ───
        private void BuildTransformCard(Transform parent, Vector2 pos, Vector2 size)
        {
            GameObject card = CreateCardPanel("Card_Transform", parent, pos, size, "✥ TRANSFORM", true);

            // Position Section
            CreateTMPText("PosHdr", card.transform, "<size=10><b>Position</b></size>", 10, TextAlignmentOptions.Left, colTextMuted, new Vector2(-size.x * 0.5f + 14, 110), new Vector2(80, 14));
            var posFields = CreateInteractiveXYZRow(card.transform, new Vector2(0, 85), size.x - 20f, 0, "0.000", "0.000", "0.000");
            posXText = posFields.Item1;
            posYText = posFields.Item2;
            posZText = posFields.Item3;

            // Rotation Section
            CreateTMPText("RotHdr", card.transform, "<size=10><b>Rotation</b></size>", 10, TextAlignmentOptions.Left, colTextMuted, new Vector2(-size.x * 0.5f + 14, 35), new Vector2(80, 14));
            var rotFields = CreateInteractiveXYZRow(card.transform, new Vector2(0, 10), size.x - 20f, 1, "0.000", "0.000", "0.000");
            rotXText = rotFields.Item1;
            rotYText = rotFields.Item2;
            rotZText = rotFields.Item3;

            // Scale Section
            CreateTMPText("ScaHdr", card.transform, "<size=10><b>Scale</b></size>", 10, TextAlignmentOptions.Left, colTextMuted, new Vector2(-size.x * 0.5f + 14, -40), new Vector2(80, 14));
            var scaFields = CreateInteractiveXYZRow(card.transform, new Vector2(0, -65), size.x - 20f, 2, "0.200", "0.200", "0.200");
            scaleXText = scaFields.Item1;
            scaleYText = scaFields.Item2;
            scaleZText = scaFields.Item3;
        }

        private (TextMeshProUGUI, TextMeshProUGUI, TextMeshProUGUI) CreateInteractiveXYZRow(Transform parent, Vector2 pos, float width, int section, string defX, string defY, string defZ)
        {
            GameObject row = new GameObject("XYZRow_" + section);
            row.transform.SetParent(parent, false);
            RectTransform rect = row.AddComponent<RectTransform>();
            rect.anchoredPosition = pos;
            rect.sizeDelta = new Vector2(width, 22);

            float groupW = 76f;
            float gap = 4f;
            float totalW = 3 * groupW + 2 * gap;
            float sx = -totalW * 0.5f;

            string[] axes = { "X", "Y", "Z" };
            Color[] axisColors = { colAxisRed, colAxisGreen, colAxisBlue };
            string[] defVals = { defX, defY, defZ };
            TextMeshProUGUI[] texts = new TextMeshProUGUI[3];

            for (int axis = 0; axis < 3; axis++)
            {
                int aIdx = axis;
                float gx = sx + axis * (groupW + gap) + groupW * 0.5f;

                GameObject group = new GameObject("AxisGroup_" + axes[axis]);
                group.transform.SetParent(row.transform, false);
                RectTransform grpRect = group.AddComponent<RectTransform>();
                grpRect.anchoredPosition = new Vector2(gx, 0);
                grpRect.sizeDelta = new Vector2(groupW, 22);

                CreateMiniStepBtn("-", group.transform, new Vector2(-groupW * 0.5f + 8, 0), new Vector2(16, 18), () =>
                {
                    OnStepDimension(section, aIdx, -1);
                });

                GameObject valBox = CreateUIPanel("ValBox", group.transform, new Vector2(0, 0), new Vector2(40, 20), colInsetField);
                Button valBtn = valBox.AddComponent<Button>();
                ColorBlock cb = valBtn.colors;
                cb.normalColor = colInsetField;
                cb.highlightedColor = colBtnHover;
                cb.pressedColor = colCyanActive * 0.5f;
                valBtn.colors = cb;
                valBtn.onClick.AddListener(() =>
                {
                    OpenNumpadModal(section, aIdx);
                });

                CreateTMPText("Badge", valBox.transform, axes[axis], 7, TextAlignmentOptions.Left, axisColors[axis], new Vector2(-13, 0), new Vector2(10, 16));
                texts[axis] = CreateTMPText("Val", valBox.transform, defVals[axis], 9, TextAlignmentOptions.Right, colTextLight, new Vector2(4, 0), new Vector2(28, 16));

                CreateMiniStepBtn("+", group.transform, new Vector2(groupW * 0.5f - 8, 0), new Vector2(16, 18), () =>
                {
                    OnStepDimension(section, aIdx, +1);
                });
            }

            return (texts[0], texts[1], texts[2]);
        }

        private void OnStepDimension(int section, int axis, int direction)
        {
            if (section == 0) // Position
            {
                float step = activeGridSnap > 0 ? activeGridSnap : 0.01f;
                CADManagerHub.Instance?.AdjustSelectedPosition(axis, direction * step);
            }
            else if (section == 1) // Rotation
            {
                float step = activeAngleSnap > 0 ? activeAngleSnap : 15f;
                CADManagerHub.Instance?.AdjustSelectedRotation(axis, direction * step);
            }
            else if (section == 2) // Scale
            {
                float step = 0.02f;
                CADManagerHub.Instance?.AdjustSelectedScale(axis, direction * step, false);
            }
        }

        private void CreateMiniStepBtn(string text, Transform parent, Vector2 pos, Vector2 size, Action onClick)
        {
            GameObject obj = new GameObject("StepBtn_" + text);
            obj.transform.SetParent(parent, false);
            RectTransform rect = obj.AddComponent<RectTransform>();
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;

            Image img = obj.AddComponent<Image>();
            img.color = colBtnNormal;

            Button btn = obj.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor = colBtnNormal;
            cb.highlightedColor = colBtnHover;
            cb.pressedColor = colCyanActive;
            btn.colors = cb;
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            CreateTMPText("Lbl", obj.transform, text, 11, TextAlignmentOptions.Center, colTextLight, Vector2.zero, size);
        }

        // ─── 4. SNAP CARD ───
        private void BuildSnapCard(Transform parent, Vector2 pos, Vector2 size)
        {
            GameObject card = CreateCardPanel("Card_Snap", parent, pos, size, "⊞ SNAP", true);

            // Grid Snap
            CreateTMPText("GridHdr", card.transform, "<size=9><b>Grid Snap</b></size>", 9, TextAlignmentOptions.Left, colTextMuted, new Vector2(-size.x * 0.5f + 14, 110), new Vector2(size.x - 28, 14));

            string[] gridLabels = { "1mm", "5mm", "10mm", "50mm" };
            float[] gridValues = { 0.001f, 0.005f, 0.010f, 0.050f };
            float gBtnW = 38f;
            float gGap = 4f;
            float gTotalW = gridLabels.Length * gBtnW + (gridLabels.Length - 1) * gGap;
            float gsx = -gTotalW * 0.5f + gBtnW * 0.5f;

            gridSnapButtons.Clear();
            for (int i = 0; i < gridLabels.Length; i++)
            {
                int idx = i;
                float bx = gsx + i * (gBtnW + gGap);
                bool active = Mathf.Approximately(gridValues[i], activeGridSnap);
                Button btn = CreateToggleBtn($"GSnap_{gridLabels[i]}", card.transform, new Vector2(bx, 84), new Vector2(gBtnW, 22), gridLabels[i], active, () => SetGridSnap(gridValues[idx]));
                gridSnapButtons[gridLabels[i]] = btn;
            }

            // Angle Snap
            CreateTMPText("AngHdr", card.transform, "<size=9><b>Angle Snap</b></size>", 9, TextAlignmentOptions.Left, colTextMuted, new Vector2(-size.x * 0.5f + 14, 38), new Vector2(size.x - 28, 14));

            string[] angLabels = { "5°", "15°", "45°", "90°" };
            float[] angValues = { 5f, 15f, 45f, 90f };

            angleSnapButtons.Clear();
            for (int i = 0; i < angLabels.Length; i++)
            {
                int idx = i;
                float bx = gsx + i * (gBtnW + gGap);
                bool active = Mathf.Approximately(angValues[i], activeAngleSnap);
                Button btn = CreateToggleBtn($"ASnap_{angLabels[i]}", card.transform, new Vector2(bx, 12), new Vector2(gBtnW, 22), angLabels[i], active, () => SetAngleSnap(angValues[idx]));
                angleSnapButtons[angLabels[i]] = btn;
            }

            // Axis Lock
            CreateTMPText("LockHdr", card.transform, "<size=9><b>AXIS LOCK</b></size>", 9, TextAlignmentOptions.Left, colTextMuted, new Vector2(-size.x * 0.5f + 14, -36), new Vector2(size.x - 28, 14));

            float aBtnW = 48f;
            float aGap = 6f;
            float aTotalW = 3 * aBtnW + 2 * aGap;
            float asx = -aTotalW * 0.5f + aBtnW * 0.5f;

            string[] ax = { "X", "Y", "Z" };
            Color[] axColors = { colAxisRed, colAxisGreen, colAxisBlue };
            AxisConstraint[] csts = { AxisConstraint.LockX, AxisConstraint.LockY, AxisConstraint.LockZ };

            axisLockButtons.Clear();
            for (int i = 0; i < ax.Length; i++)
            {
                int idx = i;
                float bx = asx + i * (aBtnW + aGap);
                Button btn = CreateAxisLockBtn($"Lock_{ax[i]}", card.transform, new Vector2(bx, -64), new Vector2(aBtnW, 26), ax[i], axColors[i], () => ToggleAxisLock(csts[idx]));
                axisLockButtons[ax[i]] = btn;
            }
        }

        // ─── 5. OPERATIONS CARD ───
        private void BuildOperationsCard(Transform parent, Vector2 pos, Vector2 size)
        {
            GameObject card = CreateCardPanel("Card_Operations", parent, pos, size, "❐ OPERATIONS", true);

            string[] names = { "Extrude", "Bevel", "Chamfer", "Hole Cut", "Union", "Subtract" };
            string[] icons = { "⤊", "⌒", "◿", "⊚", "⧉", "⊟" };
            Action[] actions = {
                () => CADManagerHub.Instance?.ExtrudeSelection(0.05f),
                () => CADManagerHub.Instance?.ApplyBevelToSelection(0.03f),
                () => CADManagerHub.Instance?.ApplyChamferToSelection(0.03f),
                () => CADManagerHub.Instance?.CutHoleInSelection(0.03f, 0.25f),
                () => CADManagerHub.Instance?.PerformUnion(),
                () => CADManagerHub.Instance?.PerformCombine(),
            };

            float btnW = 54f;
            float btnH = 50f;
            float gap = 5f;

            int cols = 3;
            float totalRowW = cols * btnW + (cols - 1) * gap;
            float sx = -totalRowW * 0.5f + btnW * 0.5f;

            for (int i = 0; i < names.Length; i++)
            {
                int row = i / cols;
                int col = i % cols;
                float bx = sx + col * (btnW + gap);
                float by = 65f - row * (btnH + gap + 10);

                CreateIconBtn(names[i], card.transform, new Vector2(bx, by), new Vector2(btnW, btnH), icons[i], names[i], actions[i]);
            }
        }

        // ─── 6. SELECTION CARD ───
        private void BuildSelectionCard(Transform parent, Vector2 pos, Vector2 size)
        {
            GameObject card = CreateCardPanel("Card_Selection", parent, pos, size, "⛶ SELECTION", false);

            string[] mNames = { "Object", "Face", "Edge", "Vertex" };
            string[] mIcons = { "❒", "◫", "━", "•" };
            SelectionMode[] mModes = { SelectionMode.Object, SelectionMode.Face, SelectionMode.Edge, SelectionMode.Vertex };

            float btnW = 46f;
            float btnH = 50f;
            float gap = 5f;

            selectionModeButtons.Clear();
            for (int i = 0; i < mNames.Length; i++)
            {
                int idx = i;
                int row = i / 2;
                int col = i % 2;
                float bx = -btnW * 0.5f - gap * 0.5f + col * (btnW + gap);
                float by = 65f - row * (btnH + gap + 10);
                bool active = (mModes[i] == currentSelectionMode);
                Color bg = active ? colCyanActive : colBtnNormal;

                Button btn = CreateIconBtnColored($"Sel_{mNames[i]}", card.transform, new Vector2(bx, by), new Vector2(btnW, btnH), mIcons[i], mNames[i], bg, () => SetSelectionMode(mModes[idx]));
                selectionModeButtons[mNames[i]] = btn;
            }
        }

        // ─── 7. UTILITY CARD ───
        private void BuildUtilityCard(Transform parent, Vector2 pos, Vector2 size)
        {
            GameObject card = CreateCardPanel("Card_Utility", parent, pos, size, "🔧 UTILITY", true);

            float btnW = 60f;
            float btnH = 50f;
            float gap = 5f;

            string[] names = { "Constraints", "Measure", "Align", "Reset View" };
            string[] icons = { "⊿", "📏", "⊞", "↻" };
            Action[] actions = {
                () => CADManagerHub.Instance?.EmitStatus("Constraints: None active"),
                () => CADManagerHub.Instance?.EmitStatus("Measure Tool Ready"),
                () => CADManagerHub.Instance?.EmitStatus("Align Object to Grid"),
                () => CADManagerHub.Instance?.EmitStatus("Workspace View Reset")
            };

            for (int i = 0; i < names.Length; i++)
            {
                int idx = i;
                int row = i / 2;
                int col = i % 2;
                float bx = -btnW * 0.5f - gap * 0.5f + col * (btnW + gap);
                float by = 65f - row * (btnH + gap + 10);

                CreateIconBtn(names[i], card.transform, new Vector2(bx, by), new Vector2(btnW, btnH), icons[i], names[i], actions[idx]);
            }
        }

        // ─── 8. FILE CARD ───
        private void BuildFileCard(Transform parent, Vector2 pos, Vector2 size)
        {
            GameObject card = CreateCardPanel("Card_File", parent, pos, size, "📁 FILE", true);

            float btnW = 72f;
            float btnH = 46f;
            float gap = 6f;

            // Row 1: Export STL, Export OBJ
            CreateIconBtnColored("ExpSTL", card.transform, new Vector2(-btnW * 0.5f - gap * 0.5f, 65), new Vector2(btnW, btnH), "📦", "Export STL", colGreenExport, () => CADManagerHub.Instance?.ExportSelectedSTL());
            CreateIconBtnColored("ExpOBJ", card.transform, new Vector2(btnW * 0.5f + gap * 0.5f, 65), new Vector2(btnW, btnH), "📦", "Export OBJ", colGreenExport, () => CADManagerHub.Instance?.ExportSelectedOBJ());

            // Row 2: Clear, Undo, Redo
            float r2Y = 3f;
            float smW = 46f;
            CreateIconBtnColored("Btn_ClearF", card.transform, new Vector2(-smW - gap, r2Y), new Vector2(smW, btnH), "🗑", "Clear", colRedClear, () => CADManagerHub.Instance?.ShapeManager?.ClearAll());
            CreateIconBtn("Btn_UndoF", card.transform, new Vector2(0, r2Y), new Vector2(smW, btnH), "↶", "Undo", () => CADManagerHub.Instance?.EmitStatus("Undo"));
            CreateIconBtn("Btn_RedoF", card.transform, new Vector2(smW + gap, r2Y), new Vector2(smW, btnH), "↷", "Redo", () => CADManagerHub.Instance?.EmitStatus("Redo"));
        }

        #endregion

        // ════════════════════════════════════════════════════════
        //  BOTTOM ACTION DOCK
        // ════════════════════════════════════════════════════════

        #region Bottom Dock

        private void BuildBottomDock(Transform parent)
        {
            GameObject dock = CreateUIPanel("BottomDock", parent, new Vector2(0, DOCK_Y), new Vector2(TOP_BAR_W, DOCK_H), colDockBg);

            // Glowing top border
            CreateUIPanel("DockGlow", dock.transform, new Vector2(0, DOCK_H * 0.5f - 1), new Vector2(TOP_BAR_W, 2), colCardBorderGlow);

            float btnW = 74f;
            float btnH = 36f;
            float gap = 5f;

            string[] labels = { "Undo", "Redo", "Set Sub", "Arc", "Rotation", "Position", "Perform", "+Add", "Edge Mode", "Perform Cut", "Mark Union", "Perform Union" };

            Action[] dockActions = {
                () => CADManagerHub.Instance?.Undo(),
                () => CADManagerHub.Instance?.Redo(),
                () => CADManagerHub.Instance?.MarkForCombine(),
                () => CADManagerHub.Instance?.EmitStatus("Arc Tool"),
                () => { var tm = CADManagerHub.Instance?.TransformManager; var sel = CADManagerHub.Instance?.SelectionManager?.SelectedObject; if (tm != null && sel != null) tm.RotateObject(sel, Vector3.up, 15f); },
                () => { var tm = CADManagerHub.Instance?.TransformManager; var sel = CADManagerHub.Instance?.SelectionManager?.SelectedObject; if (tm != null && sel != null) tm.NudgeObject(sel, Vector3.up); },
                () => CADManagerHub.Instance?.PerformCombine(),
                () => CADManagerHub.Instance?.CreatePrimitive(CADShapeType.Box),
                () => CADManagerHub.Instance?.SetSelectionMode(SelectionMode.Edge),
                () => CADManagerHub.Instance?.PerformCombine(),
                () => CADManagerHub.Instance?.MarkForUnion(),
                () => CADManagerHub.Instance?.PerformUnion(),
            };

            float totalBtnsW = labels.Length * btnW + (labels.Length - 1) * gap;
            float startX = -totalBtnsW * 0.5f + btnW * 0.5f - 70f;

            for (int i = 0; i < labels.Length; i++)
            {
                int idx = i;
                float bx = startX + i * (btnW + gap);
                CreateStyledButton($"Dock_{i}", dock.transform, new Vector2(bx, 0), new Vector2(btnW, btnH),
                    $"<size=9>{labels[i]}</size>", colBtnNormal, colBtnBorder, dockActions[idx]);
            }

            float clearAllX = startX + labels.Length * (btnW + gap) + 6f;
            CreateStyledButton("Dock_ClearAll", dock.transform, new Vector2(clearAllX, 0), new Vector2(86, btnH),
                "<size=10><b>Clear All</b></size>", colBlueAction, colBlueAction * 1.2f, () => CADManagerHub.Instance?.ShapeManager?.ClearAll());

            CreateStyledButton("Dock_Clear", dock.transform, new Vector2(clearAllX + 86 + gap, 0), new Vector2(66, btnH),
                "<size=10><b>Delete</b></size>", colRedClear, colRedClear * 1.2f, () => CADManagerHub.Instance?.DeleteSelected());
        }

        #endregion

        // ════════════════════════════════════════════════════════
        //  NUMPAD MODAL (Enter Exact Dimensions)
        // ════════════════════════════════════════════════════════

        #region Numpad Modal

        private void BuildNumpadModal(Transform parent)
        {
            numpadPanel = CreateUIPanel("NumpadModal", parent, new Vector2(0, 40), new Vector2(230, 270), new Color(0.06f, 0.08f, 0.14f, 0.98f));

            CreateUIPanel("NBdrT", numpadPanel.transform, new Vector2(0, 134), new Vector2(230, 2), colCyanActive);
            CreateUIPanel("NBdrB", numpadPanel.transform, new Vector2(0, -134), new Vector2(230, 2), colCyanActive);

            numpadTitleText = CreateTMPText("NTitle", numpadPanel.transform, "<b>ENTER DIMENSION</b>", 10, TextAlignmentOptions.Center, colCyanActive, new Vector2(0, 116), new Vector2(210, 18));

            GameObject dispBox = CreateUIPanel("NDisp", numpadPanel.transform, new Vector2(0, 88), new Vector2(200, 28), colInsetField);
            numpadDisplayText = CreateTMPText("NVal", dispBox.transform, "0.000", 14, TextAlignmentOptions.Right, colTextLight, new Vector2(0, 0), new Vector2(190, 24));

            string[][] keys = {
                new string[] { "7", "8", "9" },
                new string[] { "4", "5", "6" },
                new string[] { "1", "2", "3" },
                new string[] { "±", "0", "." }
            };

            float kw = 60f;
            float kh = 28f;
            float kg = 6f;

            for (int r = 0; r < 4; r++)
            {
                float by = 54f - r * (kh + kg);
                for (int c = 0; c < 3; c++)
                {
                    string key = keys[r][c];
                    float bx = -kw - kg + c * (kw + kg);
                    CreateStyledButton($"NKey_{key}", numpadPanel.transform, new Vector2(bx, by), new Vector2(kw, kh),
                        $"<b>{key}</b>", colBtnNormal, colBtnBorder, () => OnNumpadKeyPressed(key));
                }
            }

            float bRowY = -80f;
            float aW = 60f;
            CreateStyledButton("NKey_Bk", numpadPanel.transform, new Vector2(-kw - kg, bRowY), new Vector2(aW, 28),
                "<size=9>⌫ Back</size>", colBtnNormal, colBtnBorder, OnNumpadBackspace);

            CreateStyledButton("NKey_Can", numpadPanel.transform, new Vector2(0, bRowY), new Vector2(aW, 28),
                "<size=9>Cancel</size>", colBtnNormal, colBtnBorder, () => numpadPanel.SetActive(false));

            CreateStyledButton("NKey_App", numpadPanel.transform, new Vector2(kw + kg, bRowY), new Vector2(aW, 28),
                "<size=9><b>✓ Apply</b></size>", colBlueAction, colBlueAction * 1.3f, OnNumpadApply);

            numpadPanel.SetActive(false);
        }

        private void OpenNumpadModal(int section, int axis)
        {
            numpadTargetSection = section;
            numpadTargetAxis = axis;

            string sName = section == 0 ? "POSITION" : (section == 1 ? "ROTATION" : "SCALE");
            string aName = axis == 0 ? "X" : (axis == 1 ? "Y" : "Z");
            string unit = section == 1 ? "°" : "m";

            if (numpadTitleText != null)
                numpadTitleText.text = $"<b>ENTER {sName} {aName} ({unit})</b>";

            CADObject selected = CADManagerHub.Instance?.SelectionManager?.SelectedObject;
            float currentVal = 0;
            if (selected != null)
            {
                if (section == 0) currentVal = (axis == 0) ? selected.transform.localPosition.x : (axis == 1) ? selected.transform.localPosition.y : selected.transform.localPosition.z;
                else if (section == 1) currentVal = (axis == 0) ? selected.transform.localEulerAngles.x : (axis == 1) ? selected.transform.localEulerAngles.y : selected.transform.localEulerAngles.z;
                else if (section == 2) currentVal = (axis == 0) ? selected.transform.localScale.x : (axis == 1) ? selected.transform.localScale.y : selected.transform.localScale.z;
            }

            numpadCurrentInput = $"{currentVal:F3}";
            if (numpadDisplayText != null) numpadDisplayText.text = numpadCurrentInput;

            numpadPanel.SetActive(true);
        }

        private void OnNumpadKeyPressed(string key)
        {
            if (key == "±")
            {
                if (numpadCurrentInput.StartsWith("-"))
                    numpadCurrentInput = numpadCurrentInput.Substring(1);
                else
                    numpadCurrentInput = "-" + numpadCurrentInput;
            }
            else if (key == ".")
            {
                if (!numpadCurrentInput.Contains("."))
                    numpadCurrentInput += ".";
            }
            else
            {
                if (numpadCurrentInput == "0")
                    numpadCurrentInput = key;
                else
                    numpadCurrentInput += key;
            }

            if (numpadDisplayText != null) numpadDisplayText.text = numpadCurrentInput;
        }

        private void OnNumpadBackspace()
        {
            if (numpadCurrentInput.Length > 1)
                numpadCurrentInput = numpadCurrentInput.Substring(0, numpadCurrentInput.Length - 1);
            else
                numpadCurrentInput = "0";

            if (numpadDisplayText != null) numpadDisplayText.text = numpadCurrentInput;
        }

        private void OnNumpadApply()
        {
            if (float.TryParse(numpadCurrentInput, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float val))
            {
                if (numpadTargetSection == 0)
                    CADManagerHub.Instance?.SetSelectedPositionValue(numpadTargetAxis, val);
                else if (numpadTargetSection == 1)
                    CADManagerHub.Instance?.SetSelectedRotationValue(numpadTargetAxis, val);
                else if (numpadTargetSection == 2)
                    CADManagerHub.Instance?.SetSelectedScaleValue(numpadTargetAxis, Mathf.Max(0.005f, val));
            }

            numpadPanel.SetActive(false);
        }

        #endregion

        // ════════════════════════════════════════════════════════
        //  FACTORY HELPERS
        // ════════════════════════════════════════════════════════

        #region Factory Helpers

        private GameObject CreateCardPanel(string name, Transform parent, Vector2 pos, Vector2 size, string title, bool showChevron = false)
        {
            GameObject card = CreateUIPanel(name, parent, pos, size, colCardBg);

            float bw = 1.5f;
            CreateUIPanel("BdrT", card.transform, new Vector2(0, size.y * 0.5f - bw * 0.5f), new Vector2(size.x, bw), colCardBorderGlow);
            CreateUIPanel("BdrB", card.transform, new Vector2(0, -size.y * 0.5f + bw * 0.5f), new Vector2(size.x, bw), colCardBorderGlow);
            CreateUIPanel("BdrL", card.transform, new Vector2(-size.x * 0.5f + bw * 0.5f, 0), new Vector2(bw, size.y), colCardBorderGlow);
            CreateUIPanel("BdrR", card.transform, new Vector2(size.x * 0.5f - bw * 0.5f, 0), new Vector2(bw, size.y), colCardBorderGlow);

            float headerY = size.y * 0.5f - 14f;
            CreateTMPText("Title", card.transform, $"<b>{title}</b>", 10.5f, TextAlignmentOptions.Left, colTextLight,
                new Vector2(-size.x * 0.5f + 12f + (size.x - 30f) * 0.5f, headerY), new Vector2(size.x - 30f, 20f));

            if (showChevron)
            {
                CreateTMPText("Chevron", card.transform, ">", 10, TextAlignmentOptions.Right, colTextMuted,
                    new Vector2(size.x * 0.5f - 14f, headerY), new Vector2(16, 20));
            }

            CreateUIPanel("Sep", card.transform, new Vector2(0, headerY - 12f), new Vector2(size.x - 12f, 1), new Color(0.12f, 0.18f, 0.28f, 0.6f));

            return card;
        }

        private GameObject CreateSwatchButton(string name, Transform parent, Vector2 pos, Vector2 size, Color swatchColor, bool active, Action onClick)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            RectTransform rect = obj.AddComponent<RectTransform>();
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;

            Image img = obj.AddComponent<Image>();
            img.color = swatchColor;

            Button btn = obj.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor = swatchColor;
            cb.highlightedColor = Color.Lerp(swatchColor, Color.white, 0.25f);
            cb.pressedColor = Color.white;
            btn.colors = cb;
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            GameObject outObj = CreateUIPanel("Outline", obj.transform, Vector2.zero, new Vector2(size.x + 4, size.y + 4), active ? Color.white : Color.clear);
            outObj.transform.SetAsFirstSibling();

            return obj;
        }

        private Button CreateToggleBtn(string name, Transform parent, Vector2 pos, Vector2 size, string label, bool active, Action onClick)
        {
            Color bg = active ? colCyanActive : colBtnNormal;
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            RectTransform rect = obj.AddComponent<RectTransform>();
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;

            Image img = obj.AddComponent<Image>();
            img.color = bg;

            Button btn = obj.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor = bg;
            cb.highlightedColor = bg * 1.25f;
            cb.pressedColor = colCyanActive;
            btn.colors = cb;
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            CreateTMPText("Lbl", obj.transform, $"<size=9><b>{label}</b></size>", 9, TextAlignmentOptions.Center, colTextLight, Vector2.zero, size);
            return btn;
        }

        private Button CreateAxisLockBtn(string name, Transform parent, Vector2 pos, Vector2 size, string label, Color axisColor, Action onClick)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            RectTransform rect = obj.AddComponent<RectTransform>();
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;

            Image img = obj.AddComponent<Image>();
            img.color = axisColor;

            Button btn = obj.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor = axisColor;
            cb.highlightedColor = axisColor * 1.3f;
            cb.pressedColor = Color.white;
            btn.colors = cb;
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            CreateTMPText("Lbl", obj.transform, $"<b>{label}</b>", 12, TextAlignmentOptions.Center, Color.white, Vector2.zero, size);
            return btn;
        }

        private void CreateIconBtn(string name, Transform parent, Vector2 pos, Vector2 size, string icon, string label, Action onClick)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            RectTransform rect = obj.AddComponent<RectTransform>();
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;

            Image img = obj.AddComponent<Image>();
            img.color = colBtnNormal;

            Button btn = obj.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor = colBtnNormal;
            cb.highlightedColor = colBtnHover;
            cb.pressedColor = colCyanActive;
            btn.colors = cb;
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            CreateTMPText("Icon", obj.transform, icon, 15, TextAlignmentOptions.Center, colTextLight, new Vector2(0, 6), new Vector2(size.x, 22));
            CreateTMPText("Lbl", obj.transform, $"<size=8>{label}</size>", 8, TextAlignmentOptions.Center, colTextMuted, new Vector2(0, -13), new Vector2(size.x, 14));
        }

        private Button CreateIconBtnColored(string name, Transform parent, Vector2 pos, Vector2 size, string icon, string label, Color bgColor, Action onClick)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            RectTransform rect = obj.AddComponent<RectTransform>();
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;

            Image img = obj.AddComponent<Image>();
            img.color = bgColor;

            Button btn = obj.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor = bgColor;
            cb.highlightedColor = bgColor * 1.25f;
            cb.pressedColor = colCyanActive;
            btn.colors = cb;
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            CreateTMPText("Icon", obj.transform, icon, 15, TextAlignmentOptions.Center, colTextLight, new Vector2(0, 6), new Vector2(size.x, 22));
            CreateTMPText("Lbl", obj.transform, $"<size=8>{label}</size>", 8, TextAlignmentOptions.Center, colTextLight, new Vector2(0, -13), new Vector2(size.x, 14));

            return btn;
        }

        private Button CreateStyledButton(string name, Transform parent, Vector2 pos, Vector2 size, string text, Color bgColor, Color borderColor, Action onClick)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            RectTransform rect = obj.AddComponent<RectTransform>();
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;

            Image img = obj.AddComponent<Image>();
            img.color = bgColor;

            Button btn = obj.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor = bgColor;
            cb.highlightedColor = bgColor * 1.25f;
            cb.pressedColor = colCyanActive;
            btn.colors = cb;
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            CreateTMPText("Lbl", obj.transform, text, 10, TextAlignmentOptions.Center, colTextLight, Vector2.zero, size);
            return btn;
        }

        private GameObject CreateUIPanel(string name, Transform parent, Vector2 pos, Vector2 size, Color color, bool raycastTarget = false)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            RectTransform rect = obj.AddComponent<RectTransform>();
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;

            Image img = obj.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = raycastTarget;
            return obj;
        }

        private TextMeshProUGUI CreateTMPText(string name, Transform parent, string text, float fontSize, TextAlignmentOptions alignment, Color color, Vector2? position = null, Vector2? size = null)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            RectTransform rect = obj.AddComponent<RectTransform>();
            rect.anchoredPosition = position ?? Vector2.zero;
            rect.sizeDelta = size ?? new Vector2(100, 30);

            TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.alignment = alignment;
            tmp.color = color;
            tmp.enableWordWrapping = false;
            tmp.raycastTarget = false;
            return tmp;
        }

        private void SetBtnColor(Button btn, Color col)
        {
            if (btn == null) return;
            Image img = btn.GetComponent<Image>();
            if (img != null) img.color = col;
            ColorBlock cb = btn.colors;
            cb.normalColor = col;
            cb.highlightedColor = col * 1.25f;
            cb.selectedColor = col;
            btn.colors = cb;
        }

        #endregion

        // ════════════════════════════════════════════════════════
        //  STATE LOGIC & EVENT DISPATCHING
        // ════════════════════════════════════════════════════════

        #region State Logic

        private void SetGridSnap(float value)
        {
            activeGridSnap = value;
            if (CADManagerHub.Instance?.TransformManager != null)
                CADManagerHub.Instance.TransformManager.GridSnapIncrement = value;

            string[] labels = { "1mm", "5mm", "10mm", "50mm" };
            float[] values = { 0.001f, 0.005f, 0.010f, 0.050f };
            for (int i = 0; i < labels.Length; i++)
                if (gridSnapButtons.TryGetValue(labels[i], out Button btn))
                    SetBtnColor(btn, Mathf.Approximately(values[i], value) ? colCyanActive : colBtnNormal);

            CADManagerHub.Instance?.EmitStatus($"Grid Snap: {value * 1000f:F0}mm");
        }

        private void SetAngleSnap(float value)
        {
            activeAngleSnap = value;
            if (CADManagerHub.Instance?.TransformManager != null)
                CADManagerHub.Instance.TransformManager.AngleSnapIncrement = value;

            string[] labels = { "5°", "15°", "45°", "90°" };
            float[] values = { 5f, 15f, 45f, 90f };
            for (int i = 0; i < labels.Length; i++)
                if (angleSnapButtons.TryGetValue(labels[i], out Button btn))
                    SetBtnColor(btn, Mathf.Approximately(values[i], value) ? colCyanActive : colBtnNormal);

            CADManagerHub.Instance?.EmitStatus($"Angle Snap: {value:F0}°");
        }

        private void ToggleAxisLock(AxisConstraint axis)
        {
            currentAxisLock = (currentAxisLock == axis) ? AxisConstraint.None : axis;

            if (CADManagerHub.Instance?.TransformManager != null)
                CADManagerHub.Instance.TransformManager.ActiveConstraint = currentAxisLock;

            string[] ax = { "X", "Y", "Z" };
            AxisConstraint[] cs = { AxisConstraint.LockX, AxisConstraint.LockY, AxisConstraint.LockZ };
            Color[] cols = { colAxisRed, colAxisGreen, colAxisBlue };
            for (int i = 0; i < ax.Length; i++)
                if (axisLockButtons.TryGetValue(ax[i], out Button btn))
                    SetBtnColor(btn, (currentAxisLock == cs[i]) ? cols[i] : cols[i] * 0.45f);

            CADManagerHub.Instance?.EmitStatus($"Axis Lock: {currentAxisLock}");
        }

        private void SetSelectionMode(SelectionMode mode)
        {
            currentSelectionMode = mode;
            CADManagerHub.Instance?.SetSelectionMode(mode);

            string[] names = { "Object", "Face", "Edge", "Vertex" };
            SelectionMode[] modes = { SelectionMode.Object, SelectionMode.Face, SelectionMode.Edge, SelectionMode.Vertex };
            for (int i = 0; i < names.Length; i++)
                if (selectionModeButtons.TryGetValue(names[i], out Button btn))
                    SetBtnColor(btn, modes[i] == mode ? colCyanActive : colBtnNormal);

            if (selectionModeText != null) selectionModeText.text = $"<b>{mode}</b>";
        }

        private void SetColorFromPreset(Color c)
        {
            currentColor = c;
            CADManagerHub.Instance?.SetSelectedColor(c);
        }

        private void UpdateLiveTransformReadouts()
        {
            CADObject selected = CADManagerHub.Instance?.SelectionManager?.SelectedObject;
            if (selected != null)
            {
                Vector3 p = selected.transform.localPosition;
                Vector3 r = selected.transform.localEulerAngles;
                Vector3 s = selected.transform.localScale;

                if (posXText != null) posXText.text = $"{p.x:F3}";
                if (posYText != null) posYText.text = $"{p.y:F3}";
                if (posZText != null) posZText.text = $"{p.z:F3}";
                if (rotXText != null) rotXText.text = $"{r.x:F1}";
                if (rotYText != null) rotYText.text = $"{r.y:F1}";
                if (rotZText != null) rotZText.text = $"{r.z:F1}";
                if (scaleXText != null) scaleXText.text = $"{s.x:F3}";
                if (scaleYText != null) scaleYText.text = $"{s.y:F3}";
                if (scaleZText != null) scaleZText.text = $"{s.z:F3}";
            }
        }

        private void SubscribeToEvents()
        {
            if (CADManagerHub.Instance != null)
            {
                CADManagerHub.Instance.SelectionUpdated += OnSelectionUpdated;
                CADManagerHub.Instance.StatusMessageEmitted += OnStatusMessage;
            }
        }

        private void OnSelectionUpdated(CADObject selected)
        {
            if (selected != null)
            {
                currentColor = selected.GetColor();
                UpdateSwatchOutlines();
            }
        }

        private void OnStatusMessage(string message)
        {
            if (statusText != null) statusText.text = $"<size=11>{message}</size>";
        }

        private void OnDestroy()
        {
            if (CADManagerHub.Instance != null)
            {
                CADManagerHub.Instance.SelectionUpdated -= OnSelectionUpdated;
                CADManagerHub.Instance.StatusMessageEmitted -= OnStatusMessage;
            }
        }

        #endregion
    }
}
