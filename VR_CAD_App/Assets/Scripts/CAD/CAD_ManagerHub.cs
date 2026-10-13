using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace VRCAD.Core
{
    public class CADManagerHub : MonoBehaviour
    {
        public static CADManagerHub Instance { get; private set; }

        [Header("Sub-Managers")]
        [SerializeField] private CAD_ShapeManager shapeManager;
        [SerializeField] private CAD_TransformManager transformManager;
        [SerializeField] private CAD_SelectionManager selectionManager;
        [SerializeField] private CAD_ExtrusionManager extrusionManager;
        [SerializeField] private CAD_BooleanManager booleanManager;
        [SerializeField] private CAD_ModifierManager modifierManager;
        [SerializeField] private CAD_ConstraintManager constraintManager;
        [SerializeField] private CAD_ExportManager exportManager;
        [SerializeField] private CAD_UndoRedoManager undoRedoManager;

        [Header("Spawn Anchor")]
        [SerializeField] private Transform defaultSpawnAnchor;

        // Events
        public event Action<CADObject> ShapeCreated;
        public event Action<CADObject> ShapeDeleted;
        public event Action<CADObject> MeshModified;
        public event Action<SelectionMode> SelectionModeChanged;
        public event Action<CADObject> SelectionUpdated;
        public event Action<string> StatusMessageEmitted;

        public CAD_ShapeManager ShapeManager => shapeManager;
        public CAD_TransformManager TransformManager => transformManager;
        public CAD_SelectionManager SelectionManager => selectionManager;
        public CAD_ExtrusionManager ExtrusionManager => extrusionManager;
        public CAD_BooleanManager BooleanManager => booleanManager;
        public CAD_ModifierManager ModifierManager => modifierManager;
        public CAD_ConstraintManager ConstraintManager => constraintManager;
        public CAD_ExportManager ExportManager => exportManager;
        public CAD_UndoRedoManager UndoRedoManager => undoRedoManager;

        public bool Undo() => undoRedoManager != null && undoRedoManager.Undo();
        public bool Redo() => undoRedoManager != null && undoRedoManager.Redo();

        public void UndoShapeCreation(CADObject obj)
        {
            if (obj != null) Destroy(obj.gameObject);
        }

        public void ToggleIntrusionTool()
        {
            var tool = GetComponentInChildren<VRCAD.Tools.IntrusionTool>();
            if (tool != null)
            {
                tool.IsActive = !tool.IsActive;
                EmitStatus(tool.IsActive ? "Intrusion Tool Active: Point & Pull Trigger to Push Face" : "Intrusion Tool Deactivated");
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            AutoAcquireSubManagers();
        }

        private void AutoAcquireSubManagers()
        {
            shapeManager ??= GetComponentInChildren<CAD_ShapeManager>() ?? gameObject.AddComponent<CAD_ShapeManager>();
            transformManager ??= GetComponentInChildren<CAD_TransformManager>() ?? gameObject.AddComponent<CAD_TransformManager>();
            selectionManager ??= GetComponentInChildren<CAD_SelectionManager>() ?? gameObject.AddComponent<CAD_SelectionManager>();
            extrusionManager ??= GetComponentInChildren<CAD_ExtrusionManager>() ?? gameObject.AddComponent<CAD_ExtrusionManager>();
            booleanManager ??= GetComponentInChildren<CAD_BooleanManager>() ?? gameObject.AddComponent<CAD_BooleanManager>();
            modifierManager ??= GetComponentInChildren<CAD_ModifierManager>() ?? gameObject.AddComponent<CAD_ModifierManager>();
            constraintManager ??= GetComponentInChildren<CAD_ConstraintManager>() ?? gameObject.AddComponent<CAD_ConstraintManager>();
            exportManager ??= GetComponentInChildren<CAD_ExportManager>() ?? gameObject.AddComponent<CAD_ExportManager>();
            undoRedoManager ??= GetComponentInChildren<CAD_UndoRedoManager>() ?? gameObject.AddComponent<CAD_UndoRedoManager>();

            if (FindObjectOfType<HapticFeedbackManager>() == null)
                gameObject.AddComponent<HapticFeedbackManager>();
            if ((Application.isEditor || Debug.isDebugBuild) && GetComponent<QuestBaselineRecorder>() == null)
                gameObject.AddComponent<QuestBaselineRecorder>();
            
            // Add interaction handle manager for dynamic VR grabbing
            if (GetComponentInChildren<CAD_InteractionHandleManager>() == null)
            {
                gameObject.AddComponent<CAD_InteractionHandleManager>();
            }

            if (GetComponentInChildren<VRCAD.Tools.MeshSlicerTool>() == null)
            {
                gameObject.AddComponent<VRCAD.Tools.MeshSlicerTool>();
            }

            if (GetComponentInChildren<VRCAD.Tools.IntrusionTool>() == null)
            {
                gameObject.AddComponent<VRCAD.Tools.IntrusionTool>();
            }

            if (FindObjectOfType<VRCAD.UI.WristUIManager>() == null)
            {
                GameObject uiManagerObj = new GameObject("WristUIManager");
                uiManagerObj.AddComponent<VRCAD.UI.WristUIManager>();
            }
        }

        public Vector3 GetSpawnPosition()
        {
            if (defaultSpawnAnchor != null)
            {
                return defaultSpawnAnchor.position + defaultSpawnAnchor.forward * 0.5f;
            }

            // Try to spawn above the environment grid floor
            CAD_EnvironmentManager env = FindObjectOfType<CAD_EnvironmentManager>();
            if (env != null)
            {
                // Spawn above the grid center with a slight random offset
                Vector3 gridPos = env.GridCenter;
                float spawnY = env.GridSurfaceY + 0.14f; // 14cm above grid
                float rx = UnityEngine.Random.Range(-0.15f, 0.15f);
                float rz = UnityEngine.Random.Range(-0.10f, 0.10f);
                Vector3 spawnPos = new Vector3(gridPos.x + rx, spawnY, gridPos.z + rz);
                return transformManager != null ? transformManager.ApplyPositionSnap(spawnPos) : spawnPos;
            }

            Camera mainCam = Camera.main;
            if (mainCam != null)
            {
                Vector3 forwardPos = mainCam.transform.position + mainCam.transform.forward * 0.8f;
                return transformManager != null ? transformManager.ApplyPositionSnap(forwardPos) : forwardPos;
            }

            return new Vector3(0, 1.2f, 0.5f);
        }

        #region High Level CAD Actions

        [Header("Default Creation & Render Style")]
        [SerializeField] private Color currentColor = new Color(0.18f, 0.55f, 0.95f, 1f);
        [SerializeField] private float currentRoughness = 0.4f;
        [SerializeField] private float currentMetallic = 0.1f;
        [SerializeField] private float currentOpacity = 1.0f;
        [SerializeField] private bool isWireframeMode = false;

        public Color CurrentColor => currentColor;
        public float CurrentRoughness => currentRoughness;
        public float CurrentMetallic => currentMetallic;
        public float CurrentOpacity => currentOpacity;
        public bool IsWireframeMode => isWireframeMode;

        public CADObject CreatePrimitive(CADShapeType shapeType)
        {
            Vector3 pos = GetSpawnPosition();
            Quaternion rot = Quaternion.identity;
            Vector3 scale = Vector3.one * 0.2f; // 20cm initial scale

            CADObject newObj = shapeManager.SpawnPrimitive(shapeType, pos, rot, scale);
            if (newObj != null)
            {
                newObj.SetColor(currentColor);
                newObj.SetMaterialProperties(currentRoughness, currentMetallic);
                newObj.SetOpacity(currentOpacity);
                if (isWireframeMode) newObj.SetWireframeMode(true);
                undoRedoManager?.RecordCommand(new CreateShapeCommand(newObj));
            }
            selectionManager.Select(newObj, new RaycastHit());
            EmitStatus($"Created {shapeType}");
            return newObj;
        }

        public void SetSelectionMode(SelectionMode mode)
        {
            selectionManager.ActiveMode = mode;
            EmitStatus($"Mode: {mode} Selection");
        }

        public bool ExtrudeSelection(float distance = 0.05f)
        {
            CADObject selected = selectionManager.SelectedObject;
            if (selected == null)
            {
                EmitStatus("No object selected to extrude");
                return false;
            }

            int faceTriIdx = selectionManager.SelectedFaceTriangleIndex;
            if (faceTriIdx < 0) faceTriIdx = 0; // Default to first face if in object mode

            Mesh beforeMesh = (selected.MeshFilter.sharedMesh != null) ? UnityEngine.Object.Instantiate(selected.MeshFilter.sharedMesh) : null;
            bool success = extrusionManager.ExtrudeSelectedFace(selected, faceTriIdx, distance);
            if (success && beforeMesh != null)
            {
                undoRedoManager?.RecordCommand(new MeshSnapshotCommand(selected, beforeMesh, selected.MeshFilter.sharedMesh, $"Extrude {selected.name}"));
            }
            EmitStatus(success ? $"Extruded face by {distance * 1000:F0}mm" : "Extrusion failed");
            return success;
        }

        public bool CutHoleInSelection(float radius = 0.03f, float depth = 0.25f, HoleType holeType = HoleType.Through)
        {
            CADObject selected = selectionManager.SelectedObject;
            if (selected == null)
            {
                EmitStatus("Select a base object first to cut hole");
                return false;
            }

            Vector3 faceCenter;
            Vector3 faceNormal;
            Vector3 surfacePoint;

            if (!selectionManager.GetSelectedFaceGeometry(out faceCenter, out faceNormal, out surfacePoint))
            {
                surfacePoint = selected.transform.position;
                faceNormal = selected.transform.up;
            }

            var options = new HoleOptions
            {
                holeType = holeType,
                radius = radius,
                depth = depth,
                radialSegments = 24,
                overcutMargin = VRCAD.Core.CSG.CSGTolerance.ThroughHoleOvercutMargin
            };

            Mesh beforeMesh = UnityEngine.Object.Instantiate(selected.MeshFilter.sharedMesh);
            BooleanResult result = booleanManager.CutHoleAdvanced(selected, surfacePoint, faceNormal, options);

            if (result.status == BooleanResultStatus.Success)
            {
                selected.SetMesh(result.resultMesh);
                OnMeshModified(selected);
                undoRedoManager?.RecordCommand(new MeshSnapshotCommand(selected, beforeMesh, selected.MeshFilter.sharedMesh, $"Cut Hole on {selected.name}"));
                EmitStatus(result.message);
                return true;
            }
            else
            {
                EmitStatus($"Hole cut failed: {result.message}");
                return false;
            }
        }

        public bool ApplyBevelToSelection(float amount = 0.03f)
        {
            CADObject selected = selectionManager.SelectedObject;
            if (selected == null && shapeManager.RegisteredObjects.Count > 0)
            {
                selected = shapeManager.RegisteredObjects[shapeManager.RegisteredObjects.Count - 1];
                selectionManager.Select(selected, new RaycastHit());
            }

            if (selected == null)
            {
                EmitStatus("Select or create an object first to bevel");
                return false;
            }

            Mesh beforeMesh = UnityEngine.Object.Instantiate(selected.MeshFilter.sharedMesh);
            bool success = modifierManager.ApplyBevel(selected, amount);
            if (success)
            {
                undoRedoManager?.RecordCommand(new MeshSnapshotCommand(selected, beforeMesh, selected.MeshFilter.sharedMesh, $"Bevel {selected.name}"));
                EmitStatus($"Bevel applied ({amount * 1000:F0}mm)");
            }
            else
            {
                EmitStatus("Bevel failed on selected object");
            }
            return success;
        }

        public bool ApplyChamferToSelection(float amount = 0.03f)
        {
            CADObject selected = selectionManager.SelectedObject;
            if (selected == null && shapeManager.RegisteredObjects.Count > 0)
            {
                selected = shapeManager.RegisteredObjects[shapeManager.RegisteredObjects.Count - 1];
                selectionManager.Select(selected, new RaycastHit());
            }

            if (selected == null)
            {
                EmitStatus("Select or create an object first to chamfer");
                return false;
            }

            Mesh beforeMesh = UnityEngine.Object.Instantiate(selected.MeshFilter.sharedMesh);
            bool success = modifierManager.ApplyChamfer(selected, amount);
            if (success)
            {
                undoRedoManager?.RecordCommand(new MeshSnapshotCommand(selected, beforeMesh, selected.MeshFilter.sharedMesh, $"Chamfer {selected.name}"));
                EmitStatus($"Chamfer applied ({amount * 1000:F0}mm)");
            }
            else
            {
                EmitStatus("Chamfer failed on selected object");
            }
            return success;
        }

        public bool ApplyFilletToSelection(float radius = 0.02f)
        {
            CADObject selected = selectionManager.SelectedObject;
            if (selected == null)
            {
                EmitStatus("Select an object first to fillet");
                return false;
            }

            (int v1, int v2) edge = selectionManager.SelectedEdge;
            if (edge.v1 < 0 || edge.v2 < 0) edge = (0, 1);

            Mesh beforeMesh = UnityEngine.Object.Instantiate(selected.MeshFilter.sharedMesh);
            bool success = modifierManager.ApplyFilletToEdge(selected, edge, radius, 4);
            if (success)
            {
                undoRedoManager?.RecordCommand(new MeshSnapshotCommand(selected, beforeMesh, selected.MeshFilter.sharedMesh, $"Fillet {selected.name}"));
                EmitStatus($"Fillet applied ({radius * 1000:F0}mm)");
            }
            else
            {
                EmitStatus("Fillet failed on edge");
            }
            return success;
        }

        [Header("CSG Operand Tracking")]
        private CADObject markedCombineOperand;
        private CADObject markedUnionOperand;

        public CADObject MarkedCombineOperand => markedCombineOperand;
        public CADObject MarkedUnionOperand => markedUnionOperand;

        public void MarkForCombine()
        {
            CADObject selected = selectionManager.SelectedObject;
            if (selected != null)
            {
                markedCombineOperand = selected;
                EmitStatus($"Marked {selected.name} as Cutter Tool. Now select the object to cut and click Subtract!");
            }
            else
            {
                EmitStatus("Select an object first to mark for subtract");
            }
        }

        public void MarkForUnion()
        {
            CADObject selected = selectionManager.SelectedObject;
            if (selected != null)
            {
                markedUnionOperand = selected;
                EmitStatus($"Marked {selected.name} for UNION. Now select target object and click Union again!");
            }
            else
            {
                EmitStatus("Select an object first to mark for union");
            }
        }

        public bool PerformCombine()
        {
            CADObject target = selectionManager.SelectedObject;
            var allObjects = shapeManager.RegisteredObjects;

            if (target == null && allObjects.Count > 0)
            {
                target = allObjects[allObjects.Count - 1];
                selectionManager.Select(target, new RaycastHit());
            }

            if (target == null)
            {
                EmitStatus("Create or select an object first to Subtract");
                return false;
            }

            // If an operand was previously marked as cutter
            if (markedCombineOperand != null && markedCombineOperand != target)
            {
                return ExecuteSubtract(target, markedCombineOperand);
            }

            // Auto-pair if exactly 2 objects exist in workspace
            if (allObjects.Count == 2)
            {
                CADObject cutter = (allObjects[0] == target) ? allObjects[1] : allObjects[0];
                return ExecuteSubtract(target, cutter);
            }

            if (allObjects.Count < 2)
            {
                EmitStatus("Subtract requires a second shape. Click '+Add' to spawn a cutter shape!");
                return false;
            }

            // More than 2 objects: mark current as cutter tool
            markedCombineOperand = target;
            EmitStatus($"Step 1/2: Marked {target.name} as Cutter Tool. Now select the object to cut and click Subtract!");
            return true;
        }

        private bool ExecuteSubtract(CADObject target, CADObject cutter)
        {
            Mesh targetBefore = UnityEngine.Object.Instantiate(target.MeshFilter.sharedMesh);
            BooleanResult res = booleanManager.ExecuteBoolean(target, cutter, BooleanOperation.Subtract);
            markedCombineOperand = null;

            switch (res.status)
            {
                case BooleanResultStatus.Success:
                    target.SetMesh(res.resultMesh);
                    OnMeshModified(target);
                    cutter.gameObject.SetActive(false);
                    shapeManager.UnregisterObject(cutter);
                    undoRedoManager?.RecordCommand(new BooleanCommand(target, cutter, targetBefore, res.resultMesh, $"Subtract from {target.name}"));
                    EmitStatus($"Subtract completed: cut {target.name} using {cutter.name}");
                    return true;

                case BooleanResultStatus.Empty:
                    target.gameObject.SetActive(false);
                    shapeManager.UnregisterObject(target);
                    cutter.gameObject.SetActive(false);
                    shapeManager.UnregisterObject(cutter);
                    undoRedoManager?.RecordCommand(new BooleanCommand(target, cutter, targetBefore, null, $"Subtract (empty) {target.name}", targetBecameEmpty: true));
                    EmitStatus($"Subtract completed: target {target.name} completely removed");
                    return true;

                case BooleanResultStatus.Unchanged:
                    EmitStatus(res.message);
                    return false;

                case BooleanResultStatus.Failure:
                default:
                    EmitStatus($"Subtract failed: {res.message}");
                    return false;
            }
        }

        public bool PerformUnion()
        {
            CADObject target = selectionManager.SelectedObject;
            var allObjects = shapeManager.RegisteredObjects;

            if (target == null && allObjects.Count > 0)
            {
                target = allObjects[allObjects.Count - 1];
                selectionManager.Select(target, new RaycastHit());
            }

            if (target == null)
            {
                EmitStatus("Create or select an object first for Union");
                return false;
            }

            // If an operand was previously marked for union
            if (markedUnionOperand != null && markedUnionOperand != target)
            {
                return ExecuteUnion(target, markedUnionOperand);
            }

            // Auto-pair if exactly 2 objects exist in workspace
            if (allObjects.Count == 2)
            {
                CADObject other = (allObjects[0] == target) ? allObjects[1] : allObjects[0];
                return ExecuteUnion(target, other);
            }

            if (allObjects.Count < 2)
            {
                EmitStatus("Union requires 2 shapes. Click '+Add' to spawn another shape to merge!");
                return false;
            }

            // More than 2 objects: mark current and prompt for second
            markedUnionOperand = target;
            EmitStatus($"Step 1/2: Marked {target.name} for Union. Now select the other shape and click Union again!");
            return true;
        }

        private bool ExecuteUnion(CADObject target, CADObject tool)
        {
            Mesh targetBefore = UnityEngine.Object.Instantiate(target.MeshFilter.sharedMesh);
            BooleanResult res = booleanManager.ExecuteBoolean(target, tool, BooleanOperation.Union);
            markedUnionOperand = null;

            if (res.status == BooleanResultStatus.Success)
            {
                target.SetMesh(res.resultMesh);
                OnMeshModified(target);
                tool.gameObject.SetActive(false);
                shapeManager.UnregisterObject(tool);
                undoRedoManager?.RecordCommand(new BooleanCommand(target, tool, targetBefore, res.resultMesh, $"Union {target.name} + {tool.name}"));
                EmitStatus($"Union completed: merged {target.name} and {tool.name}");
                return true;
            }
            else
            {
                EmitStatus($"Union failed: {res.message}");
                return false;
            }
        }

        private CADObject markedIntersectOperand = null;

        public bool PerformIntersect()
        {
            CADObject target = selectionManager.SelectedObject;
            var allObjects = shapeManager.RegisteredObjects;

            if (target == null && allObjects.Count > 0)
            {
                target = allObjects[allObjects.Count - 1];
                selectionManager.Select(target, new RaycastHit());
            }

            if (target == null)
            {
                EmitStatus("Create or select an object first for Intersect");
                return false;
            }

            if (markedIntersectOperand != null && markedIntersectOperand != target)
            {
                return ExecuteIntersect(target, markedIntersectOperand);
            }

            if (allObjects.Count == 2)
            {
                CADObject other = (allObjects[0] == target) ? allObjects[1] : allObjects[0];
                return ExecuteIntersect(target, other);
            }

            if (allObjects.Count < 2)
            {
                EmitStatus("Intersect requires 2 shapes. Click '+Add' to spawn another shape!");
                return false;
            }

            markedIntersectOperand = target;
            EmitStatus($"Step 1/2: Marked {target.name} for Intersect. Now select the other shape and click Intersect!");
            return true;
        }

        private bool ExecuteIntersect(CADObject target, CADObject tool)
        {
            Mesh targetBefore = UnityEngine.Object.Instantiate(target.MeshFilter.sharedMesh);
            BooleanResult res = booleanManager.ExecuteBoolean(target, tool, BooleanOperation.Intersect);
            markedIntersectOperand = null;

            switch (res.status)
            {
                case BooleanResultStatus.Success:
                    target.SetMesh(res.resultMesh);
                    OnMeshModified(target);
                    tool.gameObject.SetActive(false);
                    shapeManager.UnregisterObject(tool);
                    undoRedoManager?.RecordCommand(new BooleanCommand(target, tool, targetBefore, res.resultMesh, $"Intersect {target.name} and {tool.name}"));
                    EmitStatus($"Intersect completed ({res.validation.triangleCount} triangles)");
                    return true;

                case BooleanResultStatus.Empty:
                    target.gameObject.SetActive(false);
                    shapeManager.UnregisterObject(target);
                    tool.gameObject.SetActive(false);
                    shapeManager.UnregisterObject(tool);
                    undoRedoManager?.RecordCommand(new BooleanCommand(target, tool, targetBefore, null, $"Intersect (empty) {target.name}", targetBecameEmpty: true));
                    EmitStatus("Intersection resulted in empty geometry.");
                    return true;

                case BooleanResultStatus.Failure:
                default:
                    EmitStatus($"Intersect failed: {res.message}");
                    return false;
            }
        }

        public void SetSelectedColor(Color color)
        {
            currentColor = color;
            CADObject selected = selectionManager.SelectedObject;
            if (selected == null && shapeManager.RegisteredObjects.Count > 0)
            {
                selected = shapeManager.RegisteredObjects[shapeManager.RegisteredObjects.Count - 1];
                selectionManager.Select(selected, new RaycastHit());
            }

            if (selected != null)
            {
                selected.SetColor(color);
                EmitStatus($"Applied color to {selected.name}");
            }
            else
            {
                EmitStatus("Color preset chosen for next shape");
            }
        }

        public void SetSelectedMaterialProperties(float roughness, float metallic)
        {
            currentRoughness = roughness;
            currentMetallic = metallic;
            CADObject selected = selectionManager.SelectedObject;
            if (selected == null && shapeManager.RegisteredObjects.Count > 0)
            {
                selected = shapeManager.RegisteredObjects[shapeManager.RegisteredObjects.Count - 1];
                selectionManager.Select(selected, new RaycastHit());
            }

            if (selected != null)
            {
                selected.SetMaterialProperties(roughness, metallic);
                EmitStatus($"Roughness: {roughness:F2}, Metallic: {metallic:F2}");
            }
        }

        public void SetSelectedOpacity(float opacity)
        {
            currentOpacity = opacity;
            CADObject selected = selectionManager.SelectedObject;
            if (selected == null && shapeManager.RegisteredObjects.Count > 0)
            {
                selected = shapeManager.RegisteredObjects[shapeManager.RegisteredObjects.Count - 1];
                selectionManager.Select(selected, new RaycastHit());
            }

            if (selected != null)
            {
                selected.SetOpacity(opacity);
                EmitStatus($"Opacity: {opacity * 100f:F0}%");
            }
            else
            {
                EmitStatus($"Opacity set to {opacity * 100f:F0}% for next shape");
            }
        }

        public void ApplyChromePresetToSelection()
        {
            currentRoughness = 0.02f;
            currentMetallic = 1.0f;
            currentColor = new Color(0.96f, 0.97f, 1.0f, 1.0f);

            CADObject selected = selectionManager.SelectedObject;
            if (selected == null && shapeManager.RegisteredObjects.Count > 0)
            {
                selected = shapeManager.RegisteredObjects[shapeManager.RegisteredObjects.Count - 1];
                selectionManager.Select(selected, new RaycastHit());
            }

            if (selected != null)
            {
                selected.ApplyChromePreset();
                EmitStatus($"Applied CHROME preset to {selected.name}");
            }
            else
            {
                EmitStatus("CHROME preset ready for next shape");
            }
        }

        public void SetWireframeMode(bool enabled)
        {
            isWireframeMode = enabled;
            CADObject selected = selectionManager.SelectedObject;
            if (selected != null)
            {
                selected.SetWireframeMode(enabled);
            }
            foreach (var obj in shapeManager.RegisteredObjects)
            {
                if (obj != null) obj.SetWireframeMode(enabled);
            }
            EmitStatus(enabled ? "Wireframe View: ON (Vector wireframe)" : "Wireframe View: OFF (Solid render)");
        }

        public void ResetSelectedMaterialAndEffects(Color fallbackColor)
        {
            currentRoughness = 0.35f;
            currentMetallic = 0.10f;
            currentOpacity = 1.0f;
            isWireframeMode = false;
            currentColor = fallbackColor;

            CADObject selected = selectionManager.SelectedObject;
            if (selected != null)
            {
                selected.ResetMaterialAndEffects(fallbackColor);
                EmitStatus($"Reset textures & effects on {selected.name}");
            }
            else
            {
                foreach (var obj in shapeManager.RegisteredObjects)
                {
                    if (obj != null)
                    {
                        obj.SetWireframeMode(false);
                    }
                }
                EmitStatus("Reset textures & effects to default");
            }
        }

        public void AdjustSelectedPosition(int axis, float delta)
        {
            CADObject selected = selectionManager.SelectedObject;
            if (selected != null)
            {
                selected.AdjustPosition(axis, delta);
            }
        }

        public void SetSelectedPositionValue(int axis, float value)
        {
            CADObject selected = selectionManager.SelectedObject;
            if (selected != null)
            {
                selected.SetPositionValue(axis, value);
                EmitStatus($"Set Pos {((axis == 0) ? "X" : (axis == 1) ? "Y" : "Z")} to {value:F3}");
            }
        }

        public void AdjustSelectedRotation(int axis, float delta)
        {
            CADObject selected = selectionManager.SelectedObject;
            if (selected != null)
            {
                selected.AdjustRotation(axis, delta);
            }
        }

        public void SetSelectedRotationValue(int axis, float value)
        {
            CADObject selected = selectionManager.SelectedObject;
            if (selected != null)
            {
                selected.SetRotationValue(axis, value);
                EmitStatus($"Set Rot {((axis == 0) ? "X" : (axis == 1) ? "Y" : "Z")} to {value:F1}°");
            }
        }

        public void AdjustSelectedScale(int axis, float delta, bool uniform)
        {
            CADObject selected = selectionManager.SelectedObject;
            if (selected != null)
            {
                selected.AdjustScale(axis, delta, uniform);
            }
        }

        public void SetSelectedScaleValue(int axis, float value)
        {
            CADObject selected = selectionManager.SelectedObject;
            if (selected != null)
            {
                selected.SetScaleValue(axis, value);
                EmitStatus($"Set Scale {((axis == 0) ? "X" : (axis == 1) ? "Y" : "Z")} to {value:F3}");
            }
        }

        public void DeleteSelected()
        {
            CADObject selected = selectionManager.SelectedObject;
            if (selected != null)
            {
                string name = selected.name;
                selectionManager.ClearSelection();
                shapeManager.DeleteObject(selected);
                EmitStatus($"Deleted {name}");
            }
        }

        public void ExportSelectedSTL()
        {
            CADObject selected = selectionManager.SelectedObject;
            if (selected == null && shapeManager.RegisteredObjects.Count > 0)
            {
                selected = shapeManager.RegisteredObjects[0];
            }

            if (selected != null)
            {
                string path = exportManager.ExportToSTL(selected, true);
                EmitStatus($"Exported STL: {System.IO.Path.GetFileName(path)}");
            }
            else
            {
                EmitStatus("No geometry to export");
            }
        }

        public void ExportSelectedOBJ()
        {
            CADObject selected = selectionManager.SelectedObject;
            if (selected == null && shapeManager.RegisteredObjects.Count > 0)
            {
                selected = shapeManager.RegisteredObjects[0];
            }

            if (selected != null)
            {
                string path = exportManager.ExportToOBJ(selected);
                EmitStatus($"Exported OBJ: {System.IO.Path.GetFileName(path)}");
            }
            else
            {
                EmitStatus("No geometry to export");
            }
        }

        #endregion

        #region Event Dispatchers

        public void OnObjectGrabbed(CADObject obj, SelectEnterEventArgs args)
        {
            if (obj == null) return;
            string interactorName = args?.interactorObject?.transform != null ? args.interactorObject.transform.name : "Mouse/Desktop";
            Debug.Log($"[QuestBaseline Interaction] Grab: {obj.name}; interactor={interactorName}");
            selectionManager?.Select(obj, new RaycastHit());
            transformManager?.StartObjectManipulation(obj);
        }

        public void OnObjectReleased(CADObject obj, SelectExitEventArgs args)
        {
            if (obj == null) return;
            transformManager?.EndObjectManipulation(obj);
            Debug.Log($"[QuestBaseline Interaction] Release: {obj.name}; position={obj.transform.position}");
        }

        public void OnShapeCreated(CADObject obj) => ShapeCreated?.Invoke(obj);
        public void OnShapeDeleted(CADObject obj) => ShapeDeleted?.Invoke(obj);
        public void OnMeshModified(CADObject obj) => MeshModified?.Invoke(obj);
        public void OnSelectionModeChanged(SelectionMode mode) => SelectionModeChanged?.Invoke(mode);
        public void OnSelectionUpdated(CADObject obj) => SelectionUpdated?.Invoke(obj);

        public void EmitStatus(string message)
        {
            Debug.Log($"[CAD Status] {message}");
            StatusMessageEmitted?.Invoke(message);
        }

        #endregion
    }
}
