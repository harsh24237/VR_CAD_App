using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEngine.XR.Management;

namespace VRCAD.XR
{
    /// <summary>
    /// Enhances the Meta Quest 2 VR Device Simulator by automatically orienting the
    /// simulated controller so its ray interactor laser points directly at whatever UI
    /// element or 3D object is under the mouse cursor in Desktop / Simulator mode.
    /// Also handles instant Left-Click interaction with world-space UI buttons.
    /// </summary>
    public class XRSimulatorLaserPointer : MonoBehaviour
    {
        [Header("Controller Targets")]
        [Tooltip("The right controller GameObject (dominant hand by default).")]
        [SerializeField] private GameObject rightController;

        [Tooltip("The left controller GameObject.")]
        [SerializeField] private GameObject leftController;

        [Tooltip("Use right hand as dominant laser pointer hand in simulation.")]
        [SerializeField] private bool useRightHand = true;

        [Header("Ergonomic Positioning in Simulator")]
        [Tooltip("Offset relative to Main Camera representing natural Quest 2 controller resting pose.")]
        [SerializeField] private Vector3 rightHandOffset = new Vector3(0.18f, -0.14f, 0.42f);
        [SerializeField] private Vector3 leftHandOffset = new Vector3(-0.18f, -0.14f, 0.42f);

        [Header("Hover Laser Settings")]
        [Tooltip("Smooth speed for laser aiming interpolation (0 for instant snap).")]
        [SerializeField] private float aimSmoothSpeed = 40f;

        [Tooltip("Max ray distance for hover detection.")]
        [SerializeField] private float maxHoverDistance = 25f;

        [Tooltip("Layer mask for 3D physics raycasting.")]
        [SerializeField] private LayerMask raycastMask = ~0;

        private Camera mainCamera;
        private XRRayInteractor activeRayInteractor;
        private XRInteractorLineVisual activeLineVisual;
        private LineRenderer activeLineRenderer;
        private Transform activeControllerTransform;
        private bool isSimulatorActive = false;
        private bool _hasDestroyedDeviceSimulator = false;

        private void Start()
        {
            mainCamera = Camera.main;
            ResolveReferences();
            CheckSimulatorMode();
            TryDestroyXRDeviceSimulator();
        }

        private void Update()
        {
            if (mainCamera == null)
            {
                mainCamera = Camera.main;
                if (mainCamera == null) return;
            }

            CheckSimulatorMode();

            // Keep trying to destroy XR Device Simulator for the first few frames
            // (it can spawn after Start via XRDeviceSimulatorSettings auto-instantiation)
            if (!_hasDestroyedDeviceSimulator)
            {
                TryDestroyXRDeviceSimulator();
            }

            if (!isSimulatorActive)
            {
                // In physical VR mode (Meta Quest 2), ensure both controllers are active
                // so they track and interact completely independently via OpenXR hardware input.
                if (leftController != null && !leftController.activeSelf) leftController.SetActive(true);
                if (rightController != null && !rightController.activeSelf) rightController.SetActive(true);
                return;
            }

            EnsureControllerActive();
            UpdateControllerPosition();
            UpdateLaserAiming();
        }

        /// <summary>
        /// Automatically discovers controllers and ray interactors in the scene hierarchy.
        /// </summary>
        public void ResolveReferences()
        {
            if (rightController == null)
            {
                var rc = GameObject.Find("Right Controller");
                if (rc != null) rightController = rc;
            }

            if (leftController == null)
            {
                var lc = GameObject.Find("Left Controller");
                if (lc != null) leftController = lc;
            }

            GameObject activeObj = useRightHand ? rightController : leftController;
            if (activeObj != null)
            {
                activeControllerTransform = activeObj.transform;
                activeRayInteractor = activeObj.GetComponentInChildren<XRRayInteractor>(true);
                activeLineVisual = activeObj.GetComponentInChildren<XRInteractorLineVisual>(true);
                activeLineRenderer = activeObj.GetComponentInChildren<LineRenderer>(true);
            }
        }

        private void CheckSimulatorMode()
        {
            // If real VR is active (e.g. Meta Quest 2 via Quest Link / OpenXR), simulation MUST BE DISABLED
            // so left and right controllers track independently with 6DoF hardware input rather than mouse emulation.
            bool vrActive = VRCAD.Core.PlayerLocomotionManager.CheckIsVRPresent();

            // Secondary check: if OpenXR loaded successfully, VR is definitely active
            if (!vrActive)
            {
                var xrSettings = XRGeneralSettings.Instance;
                if (xrSettings != null && xrSettings.Manager != null && xrSettings.Manager.activeLoader != null)
                {
                    vrActive = true;
                }
            }

            isSimulatorActive = !vrActive;
        }

        private void EnsureControllerActive()
        {
            if (activeControllerTransform == null)
            {
                ResolveReferences();
                if (activeControllerTransform == null) return;
            }

            if (!activeControllerTransform.gameObject.activeSelf)
            {
                activeControllerTransform.gameObject.SetActive(true);
            }

            if (activeRayInteractor != null && !activeRayInteractor.gameObject.activeSelf)
            {
                activeRayInteractor.gameObject.SetActive(true);
            }
        }

        private void UpdateControllerPosition()
        {
            if (activeControllerTransform == null || mainCamera == null) return;

            // Maintain natural Quest 2 hand position floating forward-right in camera space
            Vector3 offset = useRightHand ? rightHandOffset : leftHandOffset;
            Vector3 desiredWorldPos = mainCamera.transform.TransformPoint(offset);
            activeControllerTransform.position = Vector3.Lerp(activeControllerTransform.position, desiredWorldPos, Time.deltaTime * 25f);
        }

        private void UpdateLaserAiming()
        {
            if (activeControllerTransform == null || mainCamera == null) return;

            Vector2 mousePos = GetMousePosition();

            // Ignore if mouse is outside game view window
            if (mousePos.x < 0 || mousePos.x > Screen.width || mousePos.y < 0 || mousePos.y > Screen.height)
            {
                return;
            }

            Ray screenRay = mainCamera.ScreenPointToRay(mousePos);
            Vector3 targetWorldPoint = Vector3.zero;
            bool hitFound = false;

            // 1. Raycast against World Space UI first (GraphicRaycaster / EventSystem)
            if (EventSystem.current != null)
            {
                PointerEventData ped = new PointerEventData(EventSystem.current) { position = mousePos };
                List<RaycastResult> uiHits = new List<RaycastResult>();
                EventSystem.current.RaycastAll(ped, uiHits);

                for (int i = 0; i < uiHits.Count; i++)
                {
                    if (uiHits[i].gameObject != null)
                    {
                        targetWorldPoint = uiHits[i].worldPosition;
                        hitFound = true;
                        break;
                    }
                }
            }

            // 2. Physics Raycast fallback (CAD shapes, Canvas BoxCollider, Grid table)
            if (!hitFound)
            {
                if (Physics.Raycast(screenRay, out RaycastHit physHit, maxHoverDistance, raycastMask))
                {
                    targetWorldPoint = physHit.point;
                    hitFound = true;
                }
            }

            // 3. Air raycast fallback
            if (!hitFound)
            {
                targetWorldPoint = screenRay.GetPoint(10f);
            }

            // Orient the controller so its forward axis points straight at the hovered point
            Vector3 aimDirection = (targetWorldPoint - activeControllerTransform.position).normalized;
            if (aimDirection.sqrMagnitude > 0.001f)
            {
                Quaternion desiredRot = Quaternion.LookRotation(aimDirection, mainCamera.transform.up);
                if (aimSmoothSpeed > 0f)
                {
                    activeControllerTransform.rotation = Quaternion.Slerp(activeControllerTransform.rotation, desiredRot, Time.deltaTime * aimSmoothSpeed);
                }
                else
                {
                    activeControllerTransform.rotation = desiredRot;
                }
            }

            // Enhance line visual length so laser touches target
            if (activeLineRenderer != null && hitFound)
            {
                activeLineRenderer.enabled = true;
            }
        }

        private bool _wasClicking = false;

        private void HandleMouseClickInteraction()
        {
            bool isClicking = false;
            if (Mouse.current != null)
            {
                isClicking = Mouse.current.leftButton.isPressed;
            }
            else
            {
                isClicking = Input.GetMouseButton(0);
            }

            if (isClicking && !_wasClicking)
            {
                _wasClicking = true;
            }
            else if (!isClicking)
            {
                _wasClicking = false;
                return;
            }
            else
            {
                return; // Was already clicking
            }

            Vector2 mousePos = GetMousePosition();
            if (EventSystem.current == null) return;

            PointerEventData ped = new PointerEventData(EventSystem.current)
            {
                position = mousePos,
                button = PointerEventData.InputButton.Left
            };

            List<RaycastResult> uiHits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(ped, uiHits);

            foreach (var result in uiHits)
            {
                GameObject hitObj = result.gameObject;
                if (hitObj == null) continue;

                // Execute standard UI Button / Selectable click
                Button btn = hitObj.GetComponentInParent<Button>();
                if (btn != null && btn.interactable)
                {
                    ExecuteEvents.Execute(btn.gameObject, ped, ExecuteEvents.pointerClickHandler);
                    return;
                }

                Toggle toggle = hitObj.GetComponentInParent<Toggle>();
                if (toggle != null && toggle.interactable)
                {
                    toggle.isOn = !toggle.isOn;
                    ExecuteEvents.Execute(hitObj, ped, ExecuteEvents.pointerClickHandler);
                    return;
                }

                Slider slider = hitObj.GetComponentInParent<Slider>();
                if (slider != null && slider.interactable)
                {
                    ExecuteEvents.Execute(hitObj, ped, ExecuteEvents.pointerDownHandler);
                    return;
                }

                // Generic pointer click handler
                if (ExecuteEvents.Execute(hitObj, ped, ExecuteEvents.pointerClickHandler))
                {
                    return;
                }
            }
        }

        private Vector2 GetMousePosition()
        {
            if (Mouse.current != null)
            {
                return Mouse.current.position.ReadValue();
            }
            return Input.mousePosition;
        }

        /// <summary>
        /// Switch laser pointer between Right and Left controller hands.
        /// </summary>
        public void ToggleHand()
        {
            useRightHand = !useRightHand;
            ResolveReferences();
        }

        /// <summary>
        /// Finds and destroys the auto-spawned XR Device Simulator at runtime.
        /// The XRI Starter Assets include an XRDeviceSimulatorSettings that auto-spawns
        /// a simulator prefab in Play Mode. This simulator overrides real Quest 2 hardware,
        /// causing: (1) flat 2D rendering instead of stereoscopic VR, and
        ///          (2) both controllers moving together instead of independently.
        /// </summary>
        private void TryDestroyXRDeviceSimulator()
        {
            if (!UnityEngine.XR.XRSettings.isDeviceActive)
            {
                _hasDestroyedDeviceSimulator = true;
                return;
            }

            // Search for any GameObject with "XR Device Simulator" in its name
            // (the auto-spawned prefab is named "XR Device Simulator(Clone)")
            var allObjects = FindObjectsOfType<MonoBehaviour>(true);
            foreach (var mb in allObjects)
            {
                if (mb == null) continue;
                string typeName = mb.GetType().Name;
                if (typeName == "XRDeviceSimulator")
                {
                    Debug.Log($"[XRSimulatorLaserPointer] Found XR Device Simulator '{mb.gameObject.name}' — DESTROYING it to allow real Quest 2 hardware tracking.");
                    Destroy(mb.gameObject);
                    _hasDestroyedDeviceSimulator = true;
                    return;
                }
            }

            // Also check by name pattern
            var simObj = GameObject.Find("XR Device Simulator(Clone)");
            if (simObj == null) simObj = GameObject.Find("XR Device Simulator");
            if (simObj != null)
            {
                Debug.Log($"[XRSimulatorLaserPointer] Found XR Device Simulator by name '{simObj.name}' — DESTROYING.");
                Destroy(simObj);
                _hasDestroyedDeviceSimulator = true;
            }
        }
    }
}
