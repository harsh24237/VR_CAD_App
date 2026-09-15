using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace VRCAD.Core
{
    public enum HandleType
    {
        ObjectScale,
        FaceExtrude,
        EdgeMove,
        VertexMove
    }

    [RequireComponent(typeof(XRGrabInteractable), typeof(Rigidbody))]
    public class CAD_InteractionHandle : MonoBehaviour
    {
        public CADObject TargetObject { get; private set; }
        public HandleType Type { get; private set; }
        public int Index1 { get; private set; } // Can be Face Index, Vertex Index, or Edge V1
        public int Index2 { get; private set; } // Can be Edge V2
        public Vector3 CornerDirection { get; private set; }

        private XRGrabInteractable grabInteractable;
        private Rigidbody rb;
        private bool isGrabbed = false;
        private Vector3 lastWorldPosition;

        public event Action<CAD_InteractionHandle, Vector3> OnHandleDragged; // Delta World Position
        public event Action<CAD_InteractionHandle> OnHandleGrabbed;
        public event Action<CAD_InteractionHandle> OnHandleReleased;

        private void Awake()
        {
            grabInteractable = GetComponent<XRGrabInteractable>();
            rb = GetComponent<Rigidbody>();
            
            rb.isKinematic = true;
            rb.useGravity = false;

            grabInteractable.movementType = XRBaseInteractable.MovementType.Instantaneous;
            grabInteractable.selectEntered.AddListener(OnGrabbed);
            grabInteractable.selectExited.AddListener(OnReleased);
        }

        public void InitializeForVertex(CADObject target, int vIdx)
        {
            TargetObject = target;
            Type = HandleType.VertexMove;
            Index1 = vIdx;
            SetVisualColor(new Color(0.9f, 0.2f, 0.2f, 0.8f));
        }

        public void InitializeForEdge(CADObject target, int v1, int v2)
        {
            TargetObject = target;
            Type = HandleType.EdgeMove;
            Index1 = v1;
            Index2 = v2;
            SetVisualColor(new Color(1f, 0.8f, 0.1f, 0.8f));
        }

        public void InitializeForFace(CADObject target, int triIdx)
        {
            TargetObject = target;
            Type = HandleType.FaceExtrude;
            Index1 = triIdx;
            SetVisualColor(new Color(0.2f, 0.9f, 0.2f, 0.8f));
        }

        public void InitializeForObjectScale(CADObject target, Vector3 cornerDir)
        {
            TargetObject = target;
            Type = HandleType.ObjectScale;
            CornerDirection = cornerDir;
            SetVisualColor(new Color(0.2f, 0.6f, 0.9f, 0.8f));
        }

        private void SetVisualColor(Color col)
        {
            var mr = GetComponent<MeshRenderer>();
            if (mr != null)
            {
                Material mat = new Material(Shader.Find("Unlit/Color") ?? Shader.Find("Standard"));
                mat.color = col;
                mr.material = mat;
            }
        }

        private void OnGrabbed(SelectEnterEventArgs arg0)
        {
            isGrabbed = true;
            lastWorldPosition = transform.position;
            OnHandleGrabbed?.Invoke(this);
        }

        private void OnReleased(SelectExitEventArgs arg0)
        {
            isGrabbed = false;
            OnHandleReleased?.Invoke(this);
        }

        private void Update()
        {
            if (isGrabbed && TargetObject != null)
            {
                Vector3 currentPos = transform.position;
                Vector3 delta = currentPos - lastWorldPosition;
                
                if (delta.sqrMagnitude > 0.000001f)
                {
                    OnHandleDragged?.Invoke(this, delta);
                    lastWorldPosition = currentPos;
                }
            }
        }

        private float _mouseZCoord;

        private void OnMouseDown()
        {
            if (isGrabbed) return;
            isGrabbed = true;
            lastWorldPosition = transform.position;
            _mouseZCoord = Camera.main.WorldToScreenPoint(gameObject.transform.position).z;
            OnHandleGrabbed?.Invoke(this);
        }

        private void OnMouseUp()
        {
            isGrabbed = false;
            OnHandleReleased?.Invoke(this);
        }

        private void OnMouseDrag()
        {
            if (isGrabbed && Camera.main != null)
            {
                Vector3 mousePoint = Input.mousePosition;
                mousePoint.z = _mouseZCoord;
                transform.position = Camera.main.ScreenToWorldPoint(mousePoint);
            }
        }

        private void OnDestroy()
        {
            if (grabInteractable != null)
            {
                grabInteractable.selectEntered.RemoveListener(OnGrabbed);
                grabInteractable.selectExited.RemoveListener(OnReleased);
            }
        }
    }
}
