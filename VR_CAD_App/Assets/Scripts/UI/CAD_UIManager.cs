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
    public class CAD_UIManager : MonoBehaviour
    {
        [Header("Canvas Placement")]
        [SerializeField] private Vector3 spawnOffset = new Vector3(0, 1.25f, 0.75f);
        [SerializeField] private Vector2 totalCanvasSize = new Vector2(1380, 740);
        [SerializeField] private float canvasScale = 0.0008f;

        [Header("State Tracking")]
        private bool uniformScaleEnabled = true;
        private Color currentColor = Color.white;
        private float redVal = 255f, greenVal = 255f, blueVal = 255f;

        // Numeric text displays
        private TextMeshProUGUI posXText, posYText, posZText;
        private TextMeshProUGUI rotXText, rotYText, rotZText;
        private TextMeshProUGUI scaleXText, scaleYText, scaleZText;
        private TextMeshProUGUI colRValText, colGValText, colBValText;
        private Slider sliderR, sliderG, sliderB;
        private Button uniformBtn;

        // Bottom dock tab elements
        private readonly List<TextMeshProUGUI> tabLabels = new List<TextMeshProUGUI>();
        private GameObject tabUnderline;
        private int activeTabIndex = 0;

        // Color Palette
        private readonly Color colBgDark = new Color(0.06f, 0.08f, 0.12f, 0.96f);
        private readonly Color colCardBg = new Color(0.09f, 0.12f, 0.18f, 0.94f);
        private readonly Color colCardHeader = new Color(0.12f, 0.16f, 0.24f, 1.0f);
        private readonly Color colInsetField = new Color(0.05f, 0.07f, 0.10f, 1.0f);
        private readonly Color colBtnNormal = new Color(0.11f, 0.15f, 0.22f, 1.0f);
        private readonly Color colBtnHover = new Color(0.18f, 0.24f, 0.35f, 1.0f);
        private readonly Color colBtnBorder = new Color(0.18f, 0.25f, 0.36f, 1.0f);

        // Accent Colors
        private readonly Color colGreenExport = new Color(0.13f, 0.58f, 0.25f, 1.0f);
        private readonly Color colBlueLoad = new Color(0.13f, 0.38f, 0.85f, 1.0f);
        private readonly Color colBlueClearAll = new Color(0.15f, 0.42f, 0.92f, 1.0f);
        private readonly Color colRedClear = new Color(0.85f, 0.20f, 0.20f, 1.0f);
        private readonly Color colCyanActive = new Color(0.00f, 0.70f, 1.00f, 1.0f);
        private readonly Color colTextLight = new Color(0.92f, 0.95f, 0.98f, 1.0f);
        private readonly Color colTextMuted = new Color(0.55f, 0.62f, 0.72f, 1.0f);

        private void Start()
        {
            BuildCompleteUI();
            SubscribeToEvents();
        }

        private void Update()
        {
            UpdateLiveTransformReadouts();
        }

        private void BuildCompleteUI()
        {
            // 1. Root Canvas
            GameObject canvasObj = new GameObject("CAD_Pro_Spatial_Canvas");
            canvasObj.transform.position = spawnOffset;
            canvasObj.transform.rotation = Quaternion.identity;

            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 10;
            scaler.referencePixelsPerUnit = 100;

            canvasObj.AddComponent<GraphicRaycaster>();
            canvasObj.AddComponent<TrackedDeviceGraphicRaycaster>();

            RectTransform canvasRect = canvasObj.GetComponent<RectTransform>();
            canvasRect.sizeDelta = totalCanvasSize;
            canvasRect.localScale = Vector3.one * canvasScale;

            // BoxCollider for Raycast & Poke
            BoxCollider col = canvasObj.AddComponent<BoxCollider>();
            col.size = new Vector3(totalCanvasSize.x, totalCanvasSize.y, 20f);
            col.center = new Vector3(0, 0, 10f);

            // XRGrabInteractable so the canvas can be repositioned
            XRGrabInteractable grab = canvasObj.AddComponent<XRGrabInteractable>();
            grab.movementType = XRBaseInteractable.MovementType.VelocityTracking;
            grab.throwOnDetach = false;
            Rigidbody rb = canvasObj.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.useGravity = false;
                rb.isKinematic = true;
            }

            // 2. Build Top Floating Cards Row
            BuildTopCardsRow(canvasObj.transform);

            // 3. Build Bottom Main Dock
            BuildBottomDock(canvasObj.transform);
        }

        #region Top Cards Row (6 Panels)

        private void BuildTopCardsRow(Transform parent)
        {
            float topY = 160f;
            float cardHeight = 330f;

            // Card 1: TOOLS (x: -560, w: 140)
            BuildToolsCard(parent, new Vector2(-570, topY), new Vector2(150, cardHeight));

            // Card 2: COLOUR (x: -370, w: 230)
            BuildColourCard(parent, new Vector2(-370, topY), new Vector2(230, cardHeight));

            // Card 3: TEXTURE (x: -130, w: 230)
            BuildTextureCard(parent, new Vector2(-130, topY), new Vector2(230, cardHeight));

            // Card 4: SCALE (x: 100, w: 210)
            BuildScaleCard(parent, new Vector2(100, topY), new Vector2(210, cardHeight));

            // Card 5: ROTATE (x: 320, w: 210)
            BuildRotateCard(parent, new Vector2(320, topY), new Vector2(210, cardHeight));

            // Card 6: POSITION (x: 540, w: 210)
            BuildPositionCard(parent, new Vector2(540, topY), new Vector2(210, cardHeight));
        }

        private void BuildToolsCard(Transform parent, Vector2 pos, Vector2 size)
        {
            GameObject card = CreateCardPanel("Card_Tools", parent, pos, size, "❖ TOOLS");

            // Export Button (Green)
            CreateColoredButton("Btn_Export", card.transform, new Vector2(0, 60), new Vector2(120, 65), "<b>⇪ EXPORT</b>", colGreenExport, () =>
            {
                CADManagerHub.Instance?.ExportSelectedSTL();
            });

            // Load Button (Blue)
            CreateColoredButton("Btn_Load", card.transform, new Vector2(0, -15), new Vector2(120, 65), "<b>📁 LOAD</b>", colBlueLoad, () =>
            {
                CADManagerHub.Instance?.CreatePrimitive(CADShapeType.Box);
            });

            // Undo / Redo Row
            CreateButtonWithLabel("Btn_Undo", card.transform, new Vector2(-32, -90), new Vector2(56, 55), "<b>↺</b>\n<size=10>UNDO</size>", () =>
            {
                CADManagerHub.Instance?.EmitStatus("Undo action executed");
            });

            CreateButtonWithLabel("Btn_Redo", card.transform, new Vector2(32, -90), new Vector2(56, 55), "<b>↻</b>\n<size=10>REDO</size>", () =>
            {
                CADManagerHub.Instance?.EmitStatus("Redo action executed");
            });
        }

        private void BuildColourCard(Transform parent, Vector2 pos, Vector2 size)
        {
            GameObject card = CreateCardPanel("Card_Colour", parent, pos, size, "🎨 COLOUR");

            // R Slider Row
            sliderR = CreateColorSliderRow(card.transform, "X", new Color(0.95f, 0.25f, 0.25f, 1f), new Vector2(0, 75), out colRValText, val =>
            {
                redVal = val;
                ApplyColorUpdate();
            });

            // G Slider Row
            sliderG = CreateColorSliderRow(card.transform, "Y", new Color(0.25f, 0.85f, 0.35f, 1f), new Vector2(0, 25), out colGValText, val =>
            {
                greenVal = val;
                ApplyColorUpdate();
            });

            // B Slider Row
            sliderB = CreateColorSliderRow(card.transform, "Z", new Color(0.25f, 0.45f, 0.95f, 1f), new Vector2(0, -25), out colBValText, val =>
            {
                blueVal = val;
                ApplyColorUpdate();
            });

            // Preset Label
            CreateTMPText("PresetLabel", card.transform, "<size=11><color=#8899aa>Preset</color></size>", 12, TextAlignmentOptions.Left, colTextMuted, new Vector2(-80, -65), new Vector2(60, 20));

            // Preset Swatches Row
            Color[] presets = new[]
            {
                Color.white,
                new Color(0.6f, 0.6f, 0.6f),
                new Color(0.2f, 0.2f, 0.2f),
                new Color(0.9f, 0.2f, 0.2f),
                new Color(0.2f, 0.8f, 0.3f),
                new Color(0.2f, 0.4f, 0.9f)
            };

            for (int i = 0; i < presets.Length; i++)
            {
                Color c = presets[i];
                float px = -85 + i * 26;
                CreateSwatchButton($"Swatch_{i}", card.transform, new Vector2(px, -90), new Vector2(22, 22), c, () =>
                {
                    SetColorFromPreset(c);
                });
            }

            // Plus Button
            CreateButtonWithLabel("Btn_AddPreset", card.transform, new Vector2(75, -90), new Vector2(22, 22), "+", () =>
            {
                CADManagerHub.Instance?.EmitStatus("Saved custom color preset");
            });
        }

        private void BuildTextureCard(Transform parent, Vector2 pos, Vector2 size)
        {
            GameObject card = CreateCardPanel("Card_Texture", parent, pos, size, "▦ TEXTURE");

            // Slot 1
            CreateTextureSlotRow(card.transform, "Slot 1", new Vector2(0, 70));
            // Slot 2
            CreateTextureSlotRow(card.transform, "Slot 2", new Vector2(0, 20));

            // Tiling
            CreateParamRow(card.transform, "Tiling", "X", "1.00", "Y", "1.00", new Vector2(0, -35));
            // Offset
            CreateParamRow(card.transform, "Offset", "X", "0.00", "Y", "0.00", new Vector2(0, -85));
        }

        private void BuildScaleCard(Transform parent, Vector2 pos, Vector2 size)
        {
            GameObject card = CreateCardPanel("Card_Scale", parent, pos, size, "⤢ SCALE");

            scaleXText = CreateStepperRow(card.transform, "X", "1.000", new Vector2(0, 75),
                () => CADManagerHub.Instance?.AdjustSelectedScale(0, -0.05f, uniformScaleEnabled),
                () => CADManagerHub.Instance?.AdjustSelectedScale(0, +0.05f, uniformScaleEnabled));

            scaleYText = CreateStepperRow(card.transform, "Y", "1.000", new Vector2(0, 25),
                () => CADManagerHub.Instance?.AdjustSelectedScale(1, -0.05f, uniformScaleEnabled),
                () => CADManagerHub.Instance?.AdjustSelectedScale(1, +0.05f, uniformScaleEnabled));

            scaleZText = CreateStepperRow(card.transform, "Z", "1.000", new Vector2(0, -25),
                () => CADManagerHub.Instance?.AdjustSelectedScale(2, -0.05f, uniformScaleEnabled),
                () => CADManagerHub.Instance?.AdjustSelectedScale(2, +0.05f, uniformScaleEnabled));

            // Uniform Toggle Row
            CreateButtonWithLabel("Btn_Link", card.transform, new Vector2(-60, -85), new Vector2(35, 35), "🔗", () =>
            {
                uniformScaleEnabled = !uniformScaleEnabled;
                UpdateUniformButtonState();
            });

            uniformBtn = CreateButtonWithLabel("Btn_Uniform", card.transform, new Vector2(25, -85), new Vector2(120, 35), "<size=12><b>UNIFORM</b></size>", () =>
            {
                uniformScaleEnabled = !uniformScaleEnabled;
                UpdateUniformButtonState();
            });
            UpdateUniformButtonState();
        }

        private void BuildRotateCard(Transform parent, Vector2 pos, Vector2 size)
        {
            GameObject card = CreateCardPanel("Card_Rotate", parent, pos, size, "⟳ ROTATE");

            rotXText = CreateStepperRow(card.transform, "X", "0.00°", new Vector2(0, 75),
                () => CADManagerHub.Instance?.AdjustSelectedRotation(0, -15f),
                () => CADManagerHub.Instance?.AdjustSelectedRotation(0, +15f));

            rotYText = CreateStepperRow(card.transform, "Y", "0.00°", new Vector2(0, 25),
                () => CADManagerHub.Instance?.AdjustSelectedRotation(1, -15f),
                () => CADManagerHub.Instance?.AdjustSelectedRotation(1, +15f));

            rotZText = CreateStepperRow(card.transform, "Z", "0.00°", new Vector2(0, -25),
                () => CADManagerHub.Instance?.AdjustSelectedRotation(2, -15f),
                () => CADManagerHub.Instance?.AdjustSelectedRotation(2, +15f));
        }

        private void BuildPositionCard(Transform parent, Vector2 pos, Vector2 size)
        {
            GameObject card = CreateCardPanel("Card_Position", parent, pos, size, "✥ POSITION");

            posXText = CreateStepperRow(card.transform, "X", "0.000", new Vector2(0, 75),
                () => CADManagerHub.Instance?.AdjustSelectedPosition(0, -0.05f),
                () => CADManagerHub.Instance?.AdjustSelectedPosition(0, +0.05f));

            posYText = CreateStepperRow(card.transform, "Y", "0.000", new Vector2(0, 25),
                () => CADManagerHub.Instance?.AdjustSelectedPosition(1, -0.05f),
                () => CADManagerHub.Instance?.AdjustSelectedPosition(1, +0.05f));

            posZText = CreateStepperRow(card.transform, "Z", "0.000", new Vector2(0, -25),
                () => CADManagerHub.Instance?.AdjustSelectedPosition(2, -0.05f),
                () => CADManagerHub.Instance?.AdjustSelectedPosition(2, +0.05f));
        }

        #endregion

        #region Bottom Main Dock

        private void BuildBottomDock(Transform parent)
        {
            float dockY = -190f;
            float dockW = 1260f;
            float dockH = 260f;

            GameObject dockPanel = CreateUIPanel("BottomDock", parent, new Vector2(0, dockY), new Vector2(dockW, dockH), colBgDark);

            // 1. Tab Navigation Bar
            string[] tabs = new[] { "❖  MODELING", "⚒  EDITING", "⬡  COMBINE", "⁘  VERTEX", "🔧  UTILITY" };
            float tabSpacing = 160f;
            float startTabX = -dockW * 0.5f + 110f;

            for (int i = 0; i < tabs.Length; i++)
            {
                int tabIdx = i;
                float tx = startTabX + i * tabSpacing;
                TextMeshProUGUI tLabel = CreateTMPText($"Tab_{i}", dockPanel.transform, $"<b>{tabs[i]}</b>", 14, TextAlignmentOptions.Center, (i == 0) ? colCyanActive : colTextMuted, new Vector2(tx, dockH * 0.5f - 22), new Vector2(140, 30));
                tabLabels.Add(tLabel);

                // Invisible button over tab
                Button btn = tLabel.gameObject.AddComponent<Button>();
                btn.onClick.AddListener(() => SwitchBottomTab(tabIdx));
            }

            // Cyan Underline indicator
            tabUnderline = CreateUIPanel("TabUnderline", dockPanel.transform, new Vector2(startTabX, dockH * 0.5f - 36), new Vector2(130, 3), colCyanActive);

            // Separator Line
            CreateUIPanel("SepLine", dockPanel.transform, new Vector2(0, dockH * 0.5f - 40), new Vector2(dockW - 40, 1), new Color(0.18f, 0.24f, 0.35f, 0.6f));

            // 2. Sections Container (Columns)
            float contentCenterY = -15f;

            // Column 1: CREATE (x: -490)
            BuildCreateColumn(dockPanel.transform, new Vector2(-490, contentCenterY));

            // Column 2: APPEARANCE (x: -330)
            BuildAppearanceColumn(dockPanel.transform, new Vector2(-330, contentCenterY));

            // Column 3: TRANSFORM (x: -150)
            BuildTransformColumn(dockPanel.transform, new Vector2(-150, contentCenterY));

            // Column 4: OPERATIONS (x: 50)
            BuildOperationsColumn(dockPanel.transform, new Vector2(50, contentCenterY));

            // Column 5: BOOLEAN (x: 270)
            BuildBooleanColumn(dockPanel.transform, new Vector2(270, contentCenterY));

            // Right Action Dock: CLEAR ALL & CLEAR (x: 480)
            BuildRightActionDock(dockPanel.transform, new Vector2(490, contentCenterY));
        }

        private void BuildCreateColumn(Transform parent, Vector2 pos)
        {
            CreateColumnHeader(parent, "CREATE", new Vector2(pos.x, pos.y + 70));

            CreateDockButton("Btn_Spline", parent, new Vector2(pos.x, pos.y + 25), new Vector2(135, 52), "<b>╭─╮  SPLINE</b>", () =>
            {
                CADManagerHub.Instance?.CreatePrimitive(CADShapeType.Cylinder);
            });

            CreateDockButton("Btn_Extrude", parent, new Vector2(pos.x, pos.y - 35), new Vector2(135, 52), "<b>⇪  EXTRUDE</b>", () =>
            {
                CADManagerHub.Instance?.ExtrudeSelection(0.05f);
            });
        }

        private void BuildAppearanceColumn(Transform parent, Vector2 pos)
        {
            CreateColumnHeader(parent, "APPEARANCE", new Vector2(pos.x, pos.y + 70));

            CreateDockButton("Btn_Colour", parent, new Vector2(pos.x, pos.y + 25), new Vector2(150, 52), "<b>🎨  COLOUR</b>", () =>
            {
                CADManagerHub.Instance?.SetSelectionMode(SelectionMode.Object);
            });

            CreateDockButton("Btn_SetSubCombine", parent, new Vector2(pos.x, pos.y - 35), new Vector2(150, 52), "<size=11><b>❐  SET SUB COMBINE</b></size>", () =>
            {
                CADManagerHub.Instance?.MarkForCombine();
            });
        }

        private void BuildTransformColumn(Transform parent, Vector2 pos)
        {
            CreateColumnHeader(parent, "TRANSFORM", new Vector2(pos.x, pos.y + 70));

            CreateDockButton("Btn_Rotation", parent, new Vector2(pos.x, pos.y + 40), new Vector2(145, 42), "<b>⟳  ROTATION</b>", () =>
            {
                CADManagerHub.Instance?.TransformManager?.RotateObject(CADManagerHub.Instance.SelectionManager.SelectedObject, Vector3.up, 15f);
            });

            CreateDockButton("Btn_Position", parent, new Vector2(pos.x, pos.y - 5), new Vector2(145, 42), "<b>✥  POSITION</b>", () =>
            {
                CADManagerHub.Instance?.TransformManager?.NudgeObject(CADManagerHub.Instance.SelectionManager.SelectedObject, Vector3.up);
            });

            CreateDockButton("Btn_Scale", parent, new Vector2(pos.x, pos.y - 50), new Vector2(145, 42), "<b>⤢  SCALE</b>", () =>
            {
                CADManagerHub.Instance?.TransformManager?.ScaleObject(CADManagerHub.Instance.SelectionManager.SelectedObject, Vector3.one * 1.1f);
            });
        }

        private void BuildOperationsColumn(Transform parent, Vector2 pos)
        {
            CreateColumnHeader(parent, "OPERATIONS", new Vector2(pos.x, pos.y + 70));

            // Add & Switch row
            CreateDockButton("Btn_Add", parent, new Vector2(pos.x - 45, pos.y + 35), new Vector2(95, 46), "<color=#22c55e><b>+ ADD</b></color>", () =>
            {
                CADManagerHub.Instance?.CreatePrimitive(CADShapeType.Box);
            });

            CreateDockButton("Btn_Switch", parent, new Vector2(pos.x + 55, pos.y + 35), new Vector2(95, 46), "<b>⇄ SWITCH</b>", () =>
            {
                CADManagerHub.Instance?.SetSelectionMode(SelectionMode.Face);
            });

            // Mark Combine
            CreateDockButton("Btn_MarkCombine", parent, new Vector2(pos.x + 5, pos.y - 12), new Vector2(195, 42), "<size=11><b>⬚  MARK COMBINE</b></size>", () =>
            {
                CADManagerHub.Instance?.MarkForCombine();
            });

            // Mark Union
            CreateDockButton("Btn_MarkUnion", parent, new Vector2(pos.x + 5, pos.y - 58), new Vector2(195, 42), "<size=11><color=#22c55e><b>✓  MARK UNION</b></color></size>", () =>
            {
                CADManagerHub.Instance?.MarkForUnion();
            });
        }

        private void BuildBooleanColumn(Transform parent, Vector2 pos)
        {
            CreateColumnHeader(parent, "BOOLEAN", new Vector2(pos.x, pos.y + 70));

            CreateDockButton("Btn_PerformCombine", parent, new Vector2(pos.x, pos.y + 40), new Vector2(175, 42), "<size=11><color=#38bdf8><b>❖ PERFORM COMBINE</b></color></size>", () =>
            {
                CADManagerHub.Instance?.PerformCombine();
            });

            CreateDockButton("Btn_PerformUnion", parent, new Vector2(pos.x, pos.y - 5), new Vector2(175, 42), "<size=11><color=#22c55e><b>❖ PERFORM UNION</b></color></size>", () =>
            {
                CADManagerHub.Instance?.PerformUnion();
            });

            CreateDockButton("Btn_EdgeMode", parent, new Vector2(pos.x, pos.y - 50), new Vector2(175, 42), "<size=11><color=#38bdf8><b>⬡ EDGE MODE</b></color></size>", () =>
            {
                CADManagerHub.Instance?.SetSelectionMode(SelectionMode.Edge);
            });
        }

        private void BuildRightActionDock(Transform parent, Vector2 pos)
        {
            // Clear All Button (Blue)
            CreateColoredButton("Btn_ClearAll", parent, new Vector2(pos.x, pos.y + 25), new Vector2(95, 80), "<b>🧹</b>\n<size=11><b>CLEAR ALL</b></size>", colBlueClearAll, () =>
            {
                CADManagerHub.Instance?.ShapeManager?.ClearAll();
            });

            // Clear Single (Red)
            CreateColoredButton("Btn_ClearSingle", parent, new Vector2(pos.x, pos.y - 65), new Vector2(95, 80), "<b>🗑</b>\n<size=11><b>CLEAR</b></size>", colRedClear, () =>
            {
                CADManagerHub.Instance?.DeleteSelected();
            });
        }

        #endregion

        #region Factory Helpers & Component Builders

        private GameObject CreateCardPanel(string name, Transform parent, Vector2 pos, Vector2 size, string title)
        {
            GameObject card = CreateUIPanel(name, parent, pos, size, colCardBg);

            // Header bar
            GameObject header = CreateUIPanel("Header", card.transform, new Vector2(0, size.y * 0.5f - 20), new Vector2(size.x - 4, 38), colCardHeader);

            // Title
            CreateTMPText("Title", header.transform, $"<b>{title}</b>", 13, TextAlignmentOptions.Left, colTextLight, new Vector2(-size.x * 0.5f + 55, 0), new Vector2(size.x - 40, 30));

            // Down chevron ▾
            CreateTMPText("Chevron", header.transform, "▾", 14, TextAlignmentOptions.Right, colTextMuted, new Vector2(size.x * 0.5f - 25, 0), new Vector2(30, 30));

            return card;
        }

        private Slider CreateColorSliderRow(Transform parent, string axisLabel, Color indicatorCol, Vector2 pos, out TextMeshProUGUI valText, Action<float> onValueChanged)
        {
            GameObject row = new GameObject("SliderRow_" + axisLabel);
            row.transform.SetParent(parent, false);
            RectTransform rowRect = row.AddComponent<RectTransform>();
            rowRect.anchoredPosition = pos;
            rowRect.sizeDelta = new Vector2(210, 36);

            // Axis letter + Circle indicator
            CreateTMPText("AxisLetter", row.transform, axisLabel, 13, TextAlignmentOptions.Left, colTextMuted, new Vector2(-92, 0), new Vector2(15, 30));
            GameObject dot = CreateUIPanel("Dot", row.transform, new Vector2(-75, 0), new Vector2(16, 16), indicatorCol);

            // Slider Component
            GameObject sliderObj = new GameObject("Slider");
            sliderObj.transform.SetParent(row.transform, false);
            RectTransform sRect = sliderObj.AddComponent<RectTransform>();
            sRect.anchoredPosition = new Vector2(5, 0);
            sRect.sizeDelta = new Vector2(100, 20);

            Slider slider = sliderObj.AddComponent<Slider>();
            slider.minValue = 0;
            slider.maxValue = 255;
            slider.value = 255;

            // Background track
            GameObject bgTrack = CreateUIPanel("Background", sliderObj.transform, Vector2.zero, new Vector2(100, 6), colInsetField);
            // Fill track
            GameObject fill = CreateUIPanel("Fill", sliderObj.transform, Vector2.zero, new Vector2(100, 6), indicatorCol);
            slider.fillRect = fill.GetComponent<RectTransform>();

            // Handle
            GameObject handle = CreateUIPanel("Handle", sliderObj.transform, Vector2.zero, new Vector2(14, 14), Color.white);
            slider.handleRect = handle.GetComponent<RectTransform>();
            slider.targetGraphic = handle.GetComponent<Image>();

            // Value text
            valText = CreateTMPText("Val", row.transform, "255", 12, TextAlignmentOptions.Right, colTextLight, new Vector2(85, 0), new Vector2(35, 30));

            TextMeshProUGUI capturedValText = valText;
            slider.onValueChanged.AddListener(v =>
            {
                capturedValText.text = Mathf.RoundToInt(v).ToString();
                onValueChanged?.Invoke(v);
            });

            return slider;
        }

        private TextMeshProUGUI CreateStepperRow(Transform parent, string axis, string defaultVal, Vector2 pos, Action onMinus, Action onPlus)
        {
            GameObject row = new GameObject("StepperRow_" + axis);
            row.transform.SetParent(parent, false);
            RectTransform rowRect = row.AddComponent<RectTransform>();
            rowRect.anchoredPosition = pos;
            rowRect.sizeDelta = new Vector2(190, 36);

            // Axis Label
            CreateTMPText("Axis", row.transform, $"<b>{axis}</b>", 13, TextAlignmentOptions.Left, colTextMuted, new Vector2(-80, 0), new Vector2(15, 30));

            // Minus Button [ - ]
            CreateButtonWithLabel("Btn_Minus", row.transform, new Vector2(-50, 0), new Vector2(28, 28), "-", onMinus);

            // Value Box
            GameObject valBox = CreateUIPanel("ValBox", row.transform, new Vector2(5, 0), new Vector2(65, 28), colInsetField);
            TextMeshProUGUI valText = CreateTMPText("Val", valBox.transform, defaultVal, 12, TextAlignmentOptions.Center, colTextLight, Vector2.zero, new Vector2(65, 28));

            // Plus Button [ + ]
            CreateButtonWithLabel("Btn_Plus", row.transform, new Vector2(60, 0), new Vector2(28, 28), "+", onPlus);

            return valText;
        }

        private void CreateTextureSlotRow(Transform parent, string slotName, Vector2 pos)
        {
            GameObject row = new GameObject("TexRow_" + slotName);
            row.transform.SetParent(parent, false);
            RectTransform rowRect = row.AddComponent<RectTransform>();
            rowRect.anchoredPosition = pos;
            rowRect.sizeDelta = new Vector2(210, 36);

            CreateTMPText("SlotLabel", row.transform, $"<size=11>{slotName}</size>", 12, TextAlignmentOptions.Left, colTextMuted, new Vector2(-75, 0), new Vector2(45, 30));

            // Slider
            GameObject sObj = CreateUIPanel("SliderFake", row.transform, new Vector2(-15, 0), new Vector2(60, 6), colInsetField);
            CreateUIPanel("Thumb", sObj.transform, new Vector2(-10, 0), new Vector2(12, 12), colTextLight);

            // Checkerboard Preview Box
            CreateUIPanel("Preview", row.transform, new Vector2(40, 0), new Vector2(24, 24), new Color(0.3f, 0.35f, 0.45f, 1f));

            // Folder Icon Button
            CreateButtonWithLabel("Btn_Browse", row.transform, new Vector2(75, 0), new Vector2(24, 24), "📁", () =>
            {
                CADManagerHub.Instance?.EmitStatus($"Browsing textures for {slotName}");
            });
        }

        private void CreateParamRow(Transform parent, string label, string ax1, string val1, string ax2, string val2, Vector2 pos)
        {
            GameObject row = new GameObject("ParamRow_" + label);
            row.transform.SetParent(parent, false);
            RectTransform rowRect = row.AddComponent<RectTransform>();
            rowRect.anchoredPosition = pos;
            rowRect.sizeDelta = new Vector2(210, 36);

            CreateTMPText("Label", row.transform, $"<size=11>{label}</size>", 12, TextAlignmentOptions.Left, colTextMuted, new Vector2(-75, 0), new Vector2(45, 30));

            // Ax1
            CreateTMPText("Ax1", row.transform, $"<size=11><color=#8899aa>{ax1}</color></size>", 12, TextAlignmentOptions.Center, colTextMuted, new Vector2(-25, 0), new Vector2(15, 30));
            GameObject box1 = CreateUIPanel("Box1", row.transform, new Vector2(10, 0), new Vector2(45, 24), colInsetField);
            CreateTMPText("V1", box1.transform, val1, 11, TextAlignmentOptions.Center, colTextLight, Vector2.zero, new Vector2(45, 24));

            // Ax2
            CreateTMPText("Ax2", row.transform, $"<size=11><color=#8899aa>{ax2}</color></size>", 12, TextAlignmentOptions.Center, colTextMuted, new Vector2(42, 0), new Vector2(15, 30));
            GameObject box2 = CreateUIPanel("Box2", row.transform, new Vector2(75, 0), new Vector2(45, 24), colInsetField);
            CreateTMPText("V2", box2.transform, val2, 11, TextAlignmentOptions.Center, colTextLight, Vector2.zero, new Vector2(45, 24));
        }

        private void CreateColumnHeader(Transform parent, string text, Vector2 pos)
        {
            CreateTMPText("ColHeader_" + text, parent, $"<size=11><b>{text}</b></size>", 12, TextAlignmentOptions.Center, colTextMuted, pos, new Vector2(140, 20));
        }

        private Button CreateDockButton(string name, Transform parent, Vector2 pos, Vector2 size, string text, Action onClick)
        {
            return CreateStyledButton(name, parent, pos, size, text, colBtnNormal, colBtnBorder, onClick);
        }

        private Button CreateColoredButton(string name, Transform parent, Vector2 pos, Vector2 size, string text, Color color, Action onClick)
        {
            return CreateStyledButton(name, parent, pos, size, text, color, color * 1.2f, onClick);
        }

        private Button CreateButtonWithLabel(string name, Transform parent, Vector2 pos, Vector2 size, string text, Action onClick)
        {
            return CreateStyledButton(name, parent, pos, size, text, colBtnNormal, colBtnBorder, onClick);
        }

        private Button CreateSwatchButton(string name, Transform parent, Vector2 pos, Vector2 size, Color swatchColor, Action onClick)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);

            RectTransform rect = obj.AddComponent<RectTransform>();
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;

            Image img = obj.AddComponent<Image>();
            img.color = swatchColor;

            Button btn = obj.AddComponent<Button>();
            if (onClick != null) btn.onClick.AddListener(() => onClick());

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
            cb.highlightedColor = bgColor * 1.3f;
            cb.pressedColor = colCyanActive;
            cb.selectedColor = bgColor * 1.3f;
            btn.colors = cb;

            if (onClick != null)
            {
                btn.onClick.AddListener(() => onClick());
            }

            // Text
            TextMeshProUGUI tmp = CreateTMPText("Label", obj.transform, text, 13, TextAlignmentOptions.Center, colTextLight);
            RectTransform tmpRect = tmp.GetComponent<RectTransform>();
            tmpRect.anchoredPosition = Vector2.zero;
            tmpRect.sizeDelta = size;

            return btn;
        }

        private GameObject CreateUIPanel(string name, Transform parent, Vector2 pos, Vector2 size, Color color)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);

            RectTransform rect = obj.AddComponent<RectTransform>();
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;

            Image img = obj.AddComponent<Image>();
            img.color = color;

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
            tmp.enableWordWrapping = true;

            return tmp;
        }

        #endregion

        #region Logic & State Updates

        private void SwitchBottomTab(int index)
        {
            activeTabIndex = index;
            for (int i = 0; i < tabLabels.Count; i++)
            {
                tabLabels[i].color = (i == index) ? colCyanActive : colTextMuted;
            }

            if (tabUnderline != null)
            {
                float dockW = 1260f;
                float startTabX = -dockW * 0.5f + 110f;
                float tabSpacing = 160f;
                tabUnderline.GetComponent<RectTransform>().anchoredPosition = new Vector2(startTabX + index * tabSpacing, 260 * 0.5f - 36);
            }
        }

        private void UpdateUniformButtonState()
        {
            if (uniformBtn != null)
            {
                ColorBlock cb = uniformBtn.colors;
                cb.normalColor = uniformScaleEnabled ? new Color(0.00f, 0.45f, 0.85f, 1f) : colBtnNormal;
                uniformBtn.colors = cb;
            }
        }

        private void ApplyColorUpdate()
        {
            currentColor = new Color(redVal / 255f, greenVal / 255f, blueVal / 255f, 1f);
            CADManagerHub.Instance?.SetSelectedColor(currentColor);
        }

        private void SetColorFromPreset(Color c)
        {
            redVal = c.r * 255f;
            greenVal = c.g * 255f;
            blueVal = c.b * 255f;

            if (sliderR != null) sliderR.value = redVal;
            if (sliderG != null) sliderG.value = greenVal;
            if (sliderB != null) sliderB.value = blueVal;

            if (colRValText != null) colRValText.text = Mathf.RoundToInt(redVal).ToString();
            if (colGValText != null) colGValText.text = Mathf.RoundToInt(greenVal).ToString();
            if (colBValText != null) colBValText.text = Mathf.RoundToInt(blueVal).ToString();

            ApplyColorUpdate();
        }

        private void UpdateLiveTransformReadouts()
        {
            CADObject selected = CADManagerHub.Instance?.SelectionManager?.SelectedObject;
            if (selected != null)
            {
                Vector3 pos = selected.transform.localPosition;
                Vector3 rot = selected.transform.localEulerAngles;
                Vector3 scale = selected.transform.localScale;

                if (posXText != null) posXText.text = $"{pos.x:F3}";
                if (posYText != null) posYText.text = $"{pos.y:F3}";
                if (posZText != null) posZText.text = $"{pos.z:F3}";

                if (rotXText != null) rotXText.text = $"{rot.x:F2}°";
                if (rotYText != null) rotYText.text = $"{rot.y:F2}°";
                if (rotZText != null) rotZText.text = $"{rot.z:F2}°";

                if (scaleXText != null) scaleXText.text = $"{scale.x:F3}";
                if (scaleYText != null) scaleYText.text = $"{scale.y:F3}";
                if (scaleZText != null) scaleZText.text = $"{scale.z:F3}";
            }
        }

        private void SubscribeToEvents()
        {
            if (CADManagerHub.Instance != null)
            {
                CADManagerHub.Instance.SelectionUpdated += OnSelectionUpdated;
            }
        }

        private void OnSelectionUpdated(CADObject selected)
        {
            if (selected != null)
            {
                Color c = selected.GetColor();
                redVal = c.r * 255f;
                greenVal = c.g * 255f;
                blueVal = c.b * 255f;

                if (sliderR != null) sliderR.SetValueWithoutNotify(redVal);
                if (sliderG != null) sliderG.SetValueWithoutNotify(greenVal);
                if (sliderB != null) sliderB.SetValueWithoutNotify(blueVal);

                if (colRValText != null) colRValText.text = Mathf.RoundToInt(redVal).ToString();
                if (colGValText != null) colGValText.text = Mathf.RoundToInt(greenVal).ToString();
                if (colBValText != null) colBValText.text = Mathf.RoundToInt(blueVal).ToString();
            }
        }

        private void OnDestroy()
        {
            if (CADManagerHub.Instance != null)
            {
                CADManagerHub.Instance.SelectionUpdated -= OnSelectionUpdated;
            }
        }

        #endregion
    }
}
