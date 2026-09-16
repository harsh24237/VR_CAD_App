using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using System.Collections.Generic;

namespace VRCAD.Core
{
    public class GridTransformHandle : MonoBehaviour
    {
        [Header("Linked Objects")]
        public Transform gridFloorTransform;
        public Transform shapesRootTransform;

        [Header("Handle Settings")]
        [SerializeField] private float handleWidth = 0.8f;
        [SerializeField] private float handleThickness = 0.04f;
        [SerializeField] private Color normalColor = new Color(0.8f, 0.85f, 0.9f, 1f);
        [SerializeField] private Color hoverColor = new Color(1.0f, 1.0f, 1.0f, 1f);
        [SerializeField] private float hoverEmission = 0.5f;

        private GameObject visualObject;
        private Material handleMaterial;
        private XRGrabInteractable grabInteractable;
        private Rigidbody rb;

        // State for transform sync
        private Vector3 lastHandlePosition;
        private Quaternion lastHandleRotation;

        private void Start()
        {
            GenerateHandleVisuals();
            SetupXRInteraction();
            SetupMouseInteraction();

            // Initialize last known state
            lastHandlePosition = transform.position;
            lastHandleRotation = transform.rotation;
        }

        private void GenerateHandleVisuals()
        {
            visualObject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visualObject.name = "GridHandle_Visual";
            visualObject.transform.SetParent(transform, false);
            
            // Make it an elongated bar along the X axis
            visualObject.transform.localScale = new Vector3(handleThickness, handleWidth * 0.5f, handleThickness);
            visualObject.transform.localRotation = Quaternion.Euler(0, 0, 90);
            
            // Remove the default capsule collider from the visual child so we can put it on the root
            Destroy(visualObject.GetComponent<CapsuleCollider>());

            // Material Setup
            MeshRenderer renderer = visualObject.GetComponent<MeshRenderer>();
            handleMaterial = new Material(Shader.Find("Standard"));
            handleMaterial.color = normalColor;
            handleMaterial.EnableKeyword("_EMISSION");
            handleMaterial.SetColor("_EmissionColor", Color.black);
            renderer.material = handleMaterial;
        }

        private void SetupXRInteraction()
        {
            // Physics
            BoxCollider col = gameObject.AddComponent<BoxCollider>();
            col.size = new Vector3(handleWidth, handleThickness * 1.5f, handleThickness * 1.5f);

            rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            // XRI
            grabInteractable = gameObject.AddComponent<XRGrabInteractable>();
            grabInteractable.movementType = XRBaseInteractable.MovementType.Instantaneous;
            grabInteractable.throwOnDetach = false;
            grabInteractable.selectMode = InteractableSelectMode.Multiple; // Allows 2-handed rotation

            // Hover Events
            grabInteractable.hoverEntered.AddListener(OnHoverEntered);
            grabInteractable.hoverExited.AddListener(OnHoverExited);

            // Grab Events for Interpolation sync
            grabInteractable.selectEntered.AddListener(OnHandGrabbed);
            grabInteractable.selectExited.AddListener(OnHandReleased);
        }

        private void OnHandGrabbed(SelectEnterEventArgs args)
        {
            SetGrabState(true);
            HapticFeedbackManager.Instance?.TriggerHaptic(args, 0.6f, 0.1f);
        }

        private void OnHandReleased(SelectExitEventArgs args)
        {
            SetGrabState(false);
        }

        public void SetGrabState(bool isGrabbed)
        {
            if (shapesRootTransform != null)
            {
                Rigidbody[] rbs = shapesRootTransform.GetComponentsInChildren<Rigidbody>();
                foreach (var r in rbs)
                {
                    r.interpolation = isGrabbed ? RigidbodyInterpolation.None : RigidbodyInterpolation.Interpolate;
                }
            }
        }

        private void OnHoverEntered(HoverEnterEventArgs args)
        {
            SetHoverState(true);
        }

        private void OnHoverExited(HoverExitEventArgs args)
        {
            SetHoverState(false);
        }

        public void SetHoverState(bool isHovered)
        {
            if (handleMaterial != null)
            {
                handleMaterial.color = isHovered ? hoverColor : normalColor;
                handleMaterial.SetColor("_EmissionColor", isHovered ? (hoverColor * hoverEmission) : Color.black);
            }
        }

        private void LateUpdate()
        {
            if (shapesRootTransform == null)
            {
                GameObject rootObj = GameObject.Find("CAD_Geometry_Root");
                if (rootObj != null) shapesRootTransform = rootObj.transform;
            }

            EnforceAxisConstraints();
            SyncLinkedTransforms();
        }

        private void EnforceAxisConstraints()
        {
            // Force Pitch and Roll to exactly 0 (world space). Only allow Yaw (Y-axis).
            Vector3 euler = transform.eulerAngles;
            if (Mathf.Abs(euler.x) > 0.001f || Mathf.Abs(euler.z) > 0.001f)
            {
                euler.x = 0;
                euler.z = 0;
                transform.eulerAngles = euler;
            }
        }

        private void SyncLinkedTransforms()
        {
            // Calculate delta movement
            Vector3 posDelta = transform.position - lastHandlePosition;
            Quaternion rotDelta = transform.rotation * Quaternion.Inverse(lastHandleRotation);

            if (posDelta.sqrMagnitude > 0.000001f || Quaternion.Angle(Quaternion.identity, rotDelta) > 0.01f)
            {
                // Apply to grid floor
                if (gridFloorTransform != null)
                {
                    gridFloorTransform.position += posDelta;
                    
                    // Rotate grid floor around the handle's pivot
                    Vector3 pivotToGrid = gridFloorTransform.position - transform.position;
                    gridFloorTransform.position = transform.position + (rotDelta * pivotToGrid);
                    gridFloorTransform.rotation = rotDelta * gridFloorTransform.rotation;
                }

                // Apply to shapes root
                if (shapesRootTransform != null)
                {
                    shapesRootTransform.position += posDelta;

                    // Rotate shapes root around the handle's pivot
                    Vector3 pivotToShapes = shapesRootTransform.position - transform.position;
                    shapesRootTransform.position = transform.position + (rotDelta * pivotToShapes);
                    shapesRootTransform.rotation = rotDelta * shapesRootTransform.rotation;
                }

                lastHandlePosition = transform.position;
                lastHandleRotation = transform.rotation;
            }
        }

        private void SetupMouseInteraction()
        {
            gameObject.AddComponent<GridHandleMouseDrag>().Init(this);
        }
    }

    public class GridHandleMouseDrag : MonoBehaviour
    {
        private GridTransformHandle handle;
        private float zCoord;
        private bool isGrabbed;

        public void Init(GridTransformHandle h)
        {
            handle = h;
        }

        private void OnMouseDown()
        {
            zCoord = Camera.main.WorldToScreenPoint(transform.position).z;
            isGrabbed = true;
            // Fake hover for testing
            handle.SetHoverState(true);
            handle.SetGrabState(true);
        }

        private void OnMouseDrag()
        {
            if (isGrabbed && Camera.main != null)
            {
                Vector3 mousePoint = Input.mousePosition;
                mousePoint.z = zCoord;
                transform.position = Camera.main.ScreenToWorldPoint(mousePoint);
            }
        }

        private void OnMouseUp()
        {
            isGrabbed = false;
            handle.SetHoverState(false);
            handle.SetGrabState(false);
        }
    }
}
