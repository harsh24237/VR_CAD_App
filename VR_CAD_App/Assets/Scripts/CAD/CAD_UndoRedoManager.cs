using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRCAD.Core
{
    public interface ICADCommand
    {
        string Description { get; }
        void Undo();
        void Redo();
    }

    /// <summary>
    /// Stores before/after mesh states for modifications like Chamfer, Bevel, Extrude, etc.
    /// </summary>
    public class MeshSnapshotCommand : ICADCommand
    {
        private readonly CADObject target;
        private readonly Mesh beforeMesh;
        private readonly Mesh afterMesh;
        private readonly string description;

        public string Description => description;

        public MeshSnapshotCommand(CADObject target, Mesh beforeMesh, Mesh afterMesh, string description)
        {
            this.target = target;
            this.beforeMesh = beforeMesh;
            this.afterMesh = afterMesh;
            this.description = description;
        }

        public void Undo()
        {
            if (target != null && beforeMesh != null)
            {
                target.SetMesh(UnityEngine.Object.Instantiate(beforeMesh));
                CADManagerHub.Instance?.OnMeshModified(target);
            }
        }

        public void Redo()
        {
            if (target != null && afterMesh != null)
            {
                target.SetMesh(UnityEngine.Object.Instantiate(afterMesh));
                CADManagerHub.Instance?.OnMeshModified(target);
            }
        }
    }

    /// <summary>
    /// Reversible command for shape creation.
    /// </summary>
    public class CreateShapeCommand : ICADCommand
    {
        private readonly CADObject createdObj;
        public string Description => $"Create {createdObj?.name}";

        public CreateShapeCommand(CADObject createdObj)
        {
            this.createdObj = createdObj;
        }

        public void Undo()
        {
            if (createdObj != null)
            {
                createdObj.gameObject.SetActive(false);
                CADManagerHub.Instance?.ShapeManager?.UnregisterObject(createdObj);
            }
        }

        public void Redo()
        {
            if (createdObj != null)
            {
                createdObj.gameObject.SetActive(true);
                CADManagerHub.Instance?.ShapeManager?.RegisterObject(createdObj);
            }
        }
    }

    /// <summary>
    /// Reversible command for Boolean operations (Union, Subtract).
    /// </summary>
    public class BooleanCommand : ICADCommand
    {
        private readonly CADObject target;
        private readonly CADObject toolObj;
        private readonly Mesh targetBeforeMesh;
        private readonly Mesh targetAfterMesh;
        private readonly string description;

        public string Description => description;

        public BooleanCommand(CADObject target, CADObject toolObj, Mesh targetBeforeMesh, Mesh targetAfterMesh, string description)
        {
            this.target = target;
            this.toolObj = toolObj;
            this.targetBeforeMesh = targetBeforeMesh;
            this.targetAfterMesh = targetAfterMesh;
            this.description = description;
        }

        public void Undo()
        {
            if (target != null && targetBeforeMesh != null)
            {
                target.SetMesh(UnityEngine.Object.Instantiate(targetBeforeMesh));
                CADManagerHub.Instance?.OnMeshModified(target);
            }

            if (toolObj != null)
            {
                toolObj.gameObject.SetActive(true);
                CADManagerHub.Instance?.ShapeManager?.RegisterObject(toolObj);
            }
        }

        public void Redo()
        {
            if (target != null && targetAfterMesh != null)
            {
                target.SetMesh(UnityEngine.Object.Instantiate(targetAfterMesh));
                CADManagerHub.Instance?.OnMeshModified(target);
            }

            if (toolObj != null)
            {
                toolObj.gameObject.SetActive(false);
                CADManagerHub.Instance?.ShapeManager?.UnregisterObject(toolObj);
            }
        }
    }

    /// <summary>
    /// Manages the Undo and Redo history stack for all CAD operations.
    /// </summary>
    public class CAD_UndoRedoManager : MonoBehaviour
    {
        private readonly Stack<ICADCommand> undoStack = new Stack<ICADCommand>();
        private readonly Stack<ICADCommand> redoStack = new Stack<ICADCommand>();
        private const int MaxHistory = 50;

        public bool CanUndo => undoStack.Count > 0;
        public bool CanRedo => redoStack.Count > 0;

        public void RecordCommand(ICADCommand command)
        {
            if (command == null) return;
            undoStack.Push(command);
            redoStack.Clear();

            if (undoStack.Count > MaxHistory)
            {
                // Discard oldest command
                var array = undoStack.ToArray();
                undoStack.Clear();
                for (int i = MaxHistory - 1; i >= 0; i--)
                {
                    undoStack.Push(array[i]);
                }
            }
        }

        public bool Undo()
        {
            if (undoStack.Count == 0)
            {
                CADManagerHub.Instance?.EmitStatus("Nothing to undo");
                return false;
            }

            ICADCommand cmd = undoStack.Pop();
            cmd.Undo();
            redoStack.Push(cmd);
            CADManagerHub.Instance?.EmitStatus($"Undo: {cmd.Description}");
            return true;
        }

        public bool Redo()
        {
            if (redoStack.Count == 0)
            {
                CADManagerHub.Instance?.EmitStatus("Nothing to redo");
                return false;
            }

            ICADCommand cmd = redoStack.Pop();
            cmd.Redo();
            undoStack.Push(cmd);
            CADManagerHub.Instance?.EmitStatus($"Redo: {cmd.Description}");
            return true;
        }

        public void Clear()
        {
            undoStack.Clear();
            redoStack.Clear();
        }
    }
}
