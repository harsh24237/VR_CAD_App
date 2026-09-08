using System;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Transformers;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace VRCAD.UI
{
    /// <summary>
    /// Grab transformer and interaction handler for world-space CAD UI panels.
    /// Enables:
    /// - Smooth 3D repositioning and rotation in mid-air via VR hand controllers
    /// - Distance push/pull zoom using controller primary 2D axis (thumbstick up/down)
    /// - Full keyboard movement for desktop/simulation (Left/Right, Up/Down, Zoom In/Out)
    /// - Dynamic uniform scaling while grabbed or adjusted via keyboard
    /// - Non-conflicting laser raycast passthrough
    /// </summary>
    [AddComponentMenu("VRCAD/UI/CAD UI Panel Manipulator")]
    public class CAD_UIPanelManipulator : XRBaseGrabTransformer
    {
        public enum PanelZoomMode
        {
            DistanceAndScale, // Adjusts distance (push/pull) and scales smoothly
            DistanceOnly,     // Only adjust distance (push/pull)
            ScaleOnly         // Only adjust panel scale
        }

        [Header("Zoom & Distance Settings")]
        [SerializeField] private PanelZoomMode zoomMode = PanelZoomMode.DistanceAndScale;
        [SerializeField] private float distanceSpeed = 2.2f;
        [SerializeField] private float scaleSpeed = 0.9f;
        [SerializeField] private float minDistance = 0.45f;
        [SerializeField] private float maxDistance = 6.0f;
        [SerializeField] private float minScaleMultiplier = 0.35f;
        [SerializeField] private float maxScaleMultiplier = 3.5f;
        [SerializeField] private float thumbstickDeadzone = 0.12f;

        [Header("Keyboard Controls (Desktop / Simulation)")]
        [SerializeField] private bool enableKeyboardControls = true;
        [SerializeField] private float keyboardMoveSpeed = 1.4f;
        [SerializeField] private float keyboardZoomSpeed = 1.8f;
        [SerializeField] private float keyboardFastMultiplier = 2.5f;
        [SerializeField] private bool enableWASD = true;

        [Header("Status Feedback")]
        [SerializeField] private bool emitStatusOnGrab = true;

        protected override RegistrationMode registrationMode => RegistrationMode.SingleAndMultiple;

        private float currentDistanceOffset = 0f;
        private float currentScaleMultiplier = 1f;
        private Vector3 initialLocalScale = Vector3.one;
        private XRGrabInteractable linkedInteractable;

        public PanelZoomMode ZoomMode { get => zoomMode; set => zoomMode = value; }
        public float DistanceSpeed { get => distanceSpeed; set => distanceSpeed = value; }
        public float ScaleSpeed { get => scaleSpeed; set => scaleSpeed = value; }
        public bool EnableKeyboardControls { get => enableKeyboardControls; set => enableKeyboardControls = value; }

        private void Awake()
        {
            initialLocalScale = transform.localScale;
            if (initialLocalScale == Vector3.zero)
            {
                initialLocalScale = Vector3.one * 0.00085f;
            }
        }

        private void Update()
        {
            if (enableKeyboardControls)
            {
                HandleKeyboardMovement();
                HandleKeyboardShortcuts();
            }
        }

        private void HandleKeyboardShortcuts()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
            {
                bool ctrl = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;
                bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
                if (ctrl && kb.zKey.wasPressedThisFrame)
                {
                    if (shift)
                        Core.CADManagerHub.Instance?.Redo();
                    else
                        Core.CADManagerHub.Instance?.Undo();
                }
                else if (ctrl && kb.yKey.wasPressedThisFrame)
                {
                    Core.CADManagerHub.Instance?.Redo();
                }
            }
#endif
            try
            {
                bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                if (ctrl && Input.GetKeyDown(KeyCode.Z))
                {
                    if (shift)
                        Core.CADManagerHub.Instance?.Redo();
                    else
                        Core.CADManagerHub.Instance?.Undo();
                }
                else if (ctrl && Input.GetKeyDown(KeyCode.Y))
                {
                    Core.CADManagerHub.Instance?.Redo();
                }
            }
            catch
            {
                // Fallback catch if legacy input is disabled
            }
        }

        public override void OnLink(XRGrabInteractable grabInteractable)
        {
            base.OnLink(grabInteractable);
            linkedInteractable = grabInteractable;
            initialLocalScale = grabInteractable.transform.localScale;
            currentDistanceOffset = 0f;
            currentScaleMultiplier = 1f;

            grabInteractable.selectEntered.AddListener(OnGrabStarted);
            grabInteractable.selectExited.AddListener(OnGrabEnded);
        }

        public override void OnUnlink(XRGrabInteractable grabInteractable)
        {
            if (grabInteractable != null)
            {
                grabInteractable.selectEntered.RemoveListener(OnGrabStarted);
                grabInteractable.selectExited.RemoveListener(OnGrabEnded);
            }

            base.OnUnlink(grabInteractable);
            linkedInteractable = null;
        }

        private void OnGrabStarted(SelectEnterEventArgs args)
        {
            initialLocalScale = transform.localScale;
            currentDistanceOffset = 0f;
            currentScaleMultiplier = 1f;

            if (emitStatusOnGrab)
            {
                Core.CADManagerHub.Instance?.EmitStatus("Dashboard Grabbed - Use Thumbstick or Keyboard to Zoom");
            }
        }

        private void OnGrabEnded(SelectExitEventArgs args)
        {
            currentDistanceOffset = 0f;
            currentScaleMultiplier = 1f;
            initialLocalScale = transform.localScale;

            // Ensure floating kinematic physics state remains intact
            if (TryGetComponent<Rigidbody>(out var rb))
            {
                rb.useGravity = false;
                rb.isKinematic = true;
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            if (emitStatusOnGrab)
            {
                Core.CADManagerHub.Instance?.EmitStatus("Dashboard Repositioned");
            }
        }

        /// <summary>
        /// Executes every dynamic update frame during grab to process pose and scale transformations.
        /// </summary>
        public override void Process(XRGrabInteractable grabInteractable, XRInteractionUpdateOrder.UpdatePhase updatePhase, ref Pose targetPose, ref Vector3 localScale)
        {
            if (updatePhase != XRInteractionUpdateOrder.UpdatePhase.Dynamic)
                return;

            if (grabInteractable == null || !grabInteractable.isSelected || grabInteractable.interactorsSelecting.Count == 0)
                return;

            IXRSelectInteractor interactor = grabInteractable.interactorsSelecting[0];
            if (interactor == null)
                return;

            float thumbstickY = ReadThumbstickInput(interactor);
            if (Mathf.Abs(thumbstickY) < thumbstickDeadzone)
                return;

            Transform interactorTransform = interactor.transform;
            Vector3 handPos = interactorTransform.position;

            // 1. Distance Adjustment (Push / Pull)
            if (zoomMode == PanelZoomMode.DistanceAndScale || zoomMode == PanelZoomMode.DistanceOnly)
            {
                Vector3 toTarget = targetPose.position - handPos;
                float currentDist = toTarget.magnitude;
                Vector3 dir = currentDist > 0.001f ? (toTarget / currentDist) : interactorTransform.forward;

                float distanceDelta = thumbstickY * distanceSpeed * Time.deltaTime;
                currentDistanceOffset += distanceDelta;

                float desiredDist = currentDist + currentDistanceOffset;
                float clampedDist = Mathf.Clamp(desiredDist, minDistance, maxDistance);
                currentDistanceOffset = clampedDist - currentDist;

                targetPose.position = handPos + dir * clampedDist;
            }

            // 2. Uniform Scale Zoom
            if (zoomMode == PanelZoomMode.DistanceAndScale || zoomMode == PanelZoomMode.ScaleOnly)
            {
                float scaleDelta = thumbstickY * scaleSpeed * Time.deltaTime;
                currentScaleMultiplier = Mathf.Clamp(currentScaleMultiplier * (1f + scaleDelta), minScaleMultiplier, maxScaleMultiplier);
                localScale = initialLocalScale * currentScaleMultiplier;
            }
        }

        /// <summary>
        /// Handles keyboard arrow, WASD, and zoom keys for translation and zooming.
        /// Works both when floating freely and during testing.
        /// </summary>
        private void HandleKeyboardMovement()
        {
            // If grabbed by VR controller, let VR grab handle primary transform
            if (linkedInteractable != null && linkedInteractable.isSelected)
                return;

            float moveX = 0f;
            float moveY = 0f;
            float zoomZ = 0f;
            bool isFast = false;

#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
            {
                // Left / Right: LeftArrow / RightArrow, A / D, Numpad 4 / 6
                if (kb.leftArrowKey.isPressed || (enableWASD && kb.aKey.isPressed) || kb.numpad4Key.isPressed) moveX -= 1f;
                if (kb.rightArrowKey.isPressed || (enableWASD && kb.dKey.isPressed) || kb.numpad6Key.isPressed) moveX += 1f;

                // Up / Down: UpArrow / DownArrow, W / S, Numpad 8 / 2
                if (kb.downArrowKey.isPressed || (enableWASD && kb.sKey.isPressed) || kb.numpad2Key.isPressed) moveY -= 1f;
                if (kb.upArrowKey.isPressed || (enableWASD && kb.wKey.isPressed) || kb.numpad8Key.isPressed) moveY += 1f;

                // Zoom In / Out: PageUp / PageDown, Equals / Minus, Numpad +/- , ] / [, E / Q
                if (kb.pageUpKey.isPressed || kb.equalsKey.isPressed || kb.numpadPlusKey.isPressed || kb.rightBracketKey.isPressed || (enableWASD && kb.eKey.isPressed)) zoomZ += 1f;
                if (kb.pageDownKey.isPressed || kb.minusKey.isPressed || kb.numpadMinusKey.isPressed || kb.leftBracketKey.isPressed || (enableWASD && kb.qKey.isPressed)) zoomZ -= 1f;

                // Speed boost: Shift
                if (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed) isFast = true;
            }
#endif

            // Fallback for legacy input if Input System didn't register keys
            if (Mathf.Approximately(moveX, 0f) && Mathf.Approximately(moveY, 0f) && Mathf.Approximately(zoomZ, 0f))
            {
                try
                {
                    if (Input.GetKey(KeyCode.LeftArrow) || (enableWASD && Input.GetKey(KeyCode.A)) || Input.GetKey(KeyCode.Keypad4)) moveX -= 1f;
                    if (Input.GetKey(KeyCode.RightArrow) || (enableWASD && Input.GetKey(KeyCode.D)) || Input.GetKey(KeyCode.Keypad6)) moveX += 1f;

                    if (Input.GetKey(KeyCode.DownArrow) || (enableWASD && Input.GetKey(KeyCode.S)) || Input.GetKey(KeyCode.Keypad2)) moveY -= 1f;
                    if (Input.GetKey(KeyCode.UpArrow) || (enableWASD && Input.GetKey(KeyCode.W)) || Input.GetKey(KeyCode.Keypad8)) moveY += 1f;

                    if (Input.GetKey(KeyCode.PageUp) || Input.GetKey(KeyCode.Equals) || Input.GetKey(KeyCode.KeypadPlus) || Input.GetKey(KeyCode.RightBracket) || (enableWASD && Input.GetKey(KeyCode.E))) zoomZ += 1f;
                    if (Input.GetKey(KeyCode.PageDown) || Input.GetKey(KeyCode.Minus) || Input.GetKey(KeyCode.KeypadMinus) || Input.GetKey(KeyCode.LeftBracket) || (enableWASD && Input.GetKey(KeyCode.Q))) zoomZ -= 1f;

                    if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) isFast = true;
                }
                catch
                {
                    // Ignore if legacy input throws in new input system mode
                }
            }

            if (Mathf.Approximately(moveX, 0f) && Mathf.Approximately(moveY, 0f) && Mathf.Approximately(zoomZ, 0f))
                return;

            float dt = Time.deltaTime;
            float speedMult = isFast ? keyboardFastMultiplier : 1.0f;
            float currentMoveSpeed = keyboardMoveSpeed * speedMult;
            float currentZoomSpeed = keyboardZoomSpeed * speedMult;

            Transform camTr = Camera.main != null ? Camera.main.transform : null;
            Vector3 camRight = camTr != null ? camTr.right : transform.right;
            Vector3 camUp = camTr != null ? camTr.up : Vector3.up;
            Vector3 camFwd = camTr != null ? camTr.forward : transform.forward;

            // 1. Move Up / Down / Left / Right
            Vector3 deltaTranslation = (camRight * moveX + camUp * moveY) * (currentMoveSpeed * dt);

            // 2. Zoom In / Out (Distance push/pull towards or away from viewer)
            if (camTr != null && !Mathf.Approximately(zoomZ, 0f))
            {
                Vector3 toPanel = transform.position - camTr.position;
                float currentDist = toPanel.magnitude;
                Vector3 viewDir = currentDist > 0.001f ? (toPanel / currentDist) : camFwd;

                // zoomZ > 0 brings panel closer (Zoom In), zoomZ < 0 pushes panel further (Zoom Out)
                float targetDist = Mathf.Clamp(currentDist - (zoomZ * currentZoomSpeed * dt), minDistance, maxDistance);
                transform.position = camTr.position + (viewDir * targetDist) + deltaTranslation;
            }
            else
            {
                deltaTranslation += camFwd * (zoomZ * currentZoomSpeed * dt);
                transform.position += deltaTranslation;
            }

            // 3. Dynamic Scale Zoom
            if (!Mathf.Approximately(zoomZ, 0f) && (zoomMode == PanelZoomMode.DistanceAndScale || zoomMode == PanelZoomMode.ScaleOnly))
            {
                float scaleDelta = zoomZ * (scaleSpeed * 0.9f) * dt;
                currentScaleMultiplier = Mathf.Clamp(currentScaleMultiplier * (1f + scaleDelta), minScaleMultiplier, maxScaleMultiplier);
                transform.localScale = initialLocalScale * currentScaleMultiplier;
            }
        }

        /// <summary>
        /// Reads thumbstick Y-axis input from the controller currently holding the panel.
        /// </summary>
        private float ReadThumbstickInput(IXRSelectInteractor interactor)
        {
            // 1. Check ActionBasedController actions if bound
            if (interactor is XRBaseControllerInteractor controllerInteractor && controllerInteractor.xrController is ActionBasedController abc)
            {
                if (abc.translateAnchorAction.action != null && abc.translateAnchorAction.action.enabled)
                {
                    Vector2 val = abc.translateAnchorAction.action.ReadValue<Vector2>();
                    if (Mathf.Abs(val.y) >= thumbstickDeadzone) return val.y;
                }

                if (abc.scaleDeltaAction.action != null && abc.scaleDeltaAction.action.enabled)
                {
                    Vector2 val = abc.scaleDeltaAction.action.ReadValue<Vector2>();
                    if (Mathf.Abs(val.y) >= thumbstickDeadzone) return val.y;
                }
            }

            // 2. Determine hand node from interactor hierarchy
            string nameLower = interactor.transform.name.ToLower();
            XRNode primaryNode = nameLower.Contains("left") ? XRNode.LeftHand : XRNode.RightHand;

            var primaryDevice = InputDevices.GetDeviceAtXRNode(primaryNode);
            if (primaryDevice.isValid && primaryDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 axisVal))
            {
                if (Mathf.Abs(axisVal.y) >= thumbstickDeadzone) return axisVal.y;
            }

            // 3. Fallback to other hand node
            XRNode secondaryNode = (primaryNode == XRNode.RightHand) ? XRNode.LeftHand : XRNode.RightHand;
            var secondaryDevice = InputDevices.GetDeviceAtXRNode(secondaryNode);
            if (secondaryDevice.isValid && secondaryDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 secondaryAxisVal))
            {
                if (Mathf.Abs(secondaryAxisVal.y) >= thumbstickDeadzone) return secondaryAxisVal.y;
            }

            // 4. Input System Gamepad / Controller fallback
#if ENABLE_INPUT_SYSTEM
            if (Gamepad.current != null)
            {
                float rightStickY = Gamepad.current.rightStick.y.ReadValue();
                if (Mathf.Abs(rightStickY) >= thumbstickDeadzone) return rightStickY;

                float leftStickY = Gamepad.current.leftStick.y.ReadValue();
                if (Mathf.Abs(leftStickY) >= thumbstickDeadzone) return leftStickY;
            }
#endif

            return 0f;
        }
    }
}
