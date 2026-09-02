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
        }

        public Vector3 GetSpawnPosition()
        {
            if (defaultSpawnAnchor != null)
            {
                return defaultSpawnAnchor.position + defaultSpawnAnchor.forward * 0.5f;
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

        public CADObject CreatePrimitive(CADShapeType shapeType)
        {
            Vector3 pos = GetSpawnPosition();
            Quaternion rot = Quaternion.identity;
            Vector3 scale = Vector3.one * 0.2f; // 20cm initial scale

            CADObject newObj = shapeManager.SpawnPrimitive(shapeType, pos, rot, scale);
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

            bool success = extrusionManager.ExtrudeSelectedFace(selected, faceTriIdx, distance);
            EmitStatus(success ? $"Extruded face by {distance * 1000:F0}mm" : "Extrusion failed");
            return success;
        }

        public bool CutHoleInSelection(float radius = 0.03f, float depth = 0.25f)
        {
            CADObject selected = selectionManager.SelectedObject;
            if (selected == null)
            {
                EmitStatus("Select a base object first to cut hole");
                return false;
            }

            Vector3 center = selected.transform.position;
            Vector3 axis = selected.transform.up;

            bool success = booleanManager.CutHole(selected, center, axis, radius, depth);
            EmitStatus(success ? "Hole cut successfully" : "Hole cut failed");
            return success;
        }

        public bool ChamferSelectedEdge(float size = 0.02f)
        {
            CADObject selected = selectionManager.SelectedObject;
            if (selected == null)
            {
                EmitStatus("Select an edge first");
                return false;
            }

            var edge = selectionManager.SelectedEdge;
            if (edge.v1 < 0 || edge.v2 < 0)
            {
                // Fallback default edge
                edge = (0, 1);
            }

            bool success = modifierManager.ApplyChamferToEdge(selected, edge, size);
            EmitStatus(success ? $"Chamfer applied ({size * 1000:F0}mm)" : "Chamfer failed on edge");
            return success;
        }

        public bool FilletSelectedEdge(float radius = 0.03f)
        {
            CADObject selected = selectionManager.SelectedObject;
            if (selected == null)
            {
                EmitStatus("Select an edge first");
                return false;
            }

            var edge = selectionManager.SelectedEdge;
            if (edge.v1 < 0 || edge.v2 < 0) edge = (0, 1);

            bool success = modifierManager.ApplyFilletToEdge(selected, edge, radius, 4);
            EmitStatus(success ? $"Fillet applied ({radius * 1000:F0}mm)" : "Fillet failed on edge");
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
                EmitStatus($"Marked {selected.name} for COMBINE");
            }
            else
            {
                EmitStatus("Select an object first to mark for combine");
            }
        }

        public void MarkForUnion()
        {
            CADObject selected = selectionManager.SelectedObject;
            if (selected != null)
            {
                markedUnionOperand = selected;
                EmitStatus($"Marked {selected.name} for UNION");
            }
            else
            {
                EmitStatus("Select an object first to mark for union");
            }
        }

        public bool PerformCombine()
        {
            CADObject target = selectionManager.SelectedObject;
            if (target == null || markedCombineOperand == null)
            {
                EmitStatus("Need both a Selected target and a Marked tool object for combine");
                return false;
            }

            if (target == markedCombineOperand)
            {
                EmitStatus("Cannot combine an object with itself");
                return false;
            }

            bool success = booleanManager.PerformBoolean(target, markedCombineOperand, BooleanOperation.Subtract);
            markedCombineOperand = null;
            EmitStatus(success ? "Combine (Cut) completed" : "Combine operation failed");
            return success;
        }

        public bool PerformUnion()
        {
            CADObject target = selectionManager.SelectedObject;
            if (target == null || markedUnionOperand == null)
            {
                EmitStatus("Need both a Selected target and a Marked tool object for union");
                return false;
            }

            if (target == markedUnionOperand)
            {
                EmitStatus("Cannot union an object with itself");
                return false;
            }

            bool success = booleanManager.PerformBoolean(target, markedUnionOperand, BooleanOperation.Union);
            markedUnionOperand = null;
            EmitStatus(success ? "Union completed" : "Union operation failed");
            return success;
        }

        public void SetSelectedColor(Color color)
        {
            CADObject selected = selectionManager.SelectedObject;
            if (selected != null)
            {
                selected.SetColor(color);
                EmitStatus($"Applied color to {selected.name}");
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

        public void AdjustSelectedRotation(int axis, float delta)
        {
            CADObject selected = selectionManager.SelectedObject;
            if (selected != null)
            {
                selected.AdjustRotation(axis, delta);
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
            selectionManager.Select(obj, new RaycastHit());
            transformManager.StartObjectManipulation(obj);
        }

        public void OnObjectReleased(CADObject obj, SelectExitEventArgs args)
        {
            transformManager.EndObjectManipulation(obj);
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
