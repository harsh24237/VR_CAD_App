using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Management;
using InputDevice = UnityEngine.XR.InputDevice;
using InputDevices = UnityEngine.XR.InputDevices;
using InputDeviceCharacteristics = UnityEngine.XR.InputDeviceCharacteristics;

namespace VRCAD.Core
{
    /// <summary>
    /// Manages player locomotion for both VR (Meta Quest 2) and PC/Editor environments.
    ///
    /// VR Mode  – Delegates movement to XRI's ActionBasedContinuousMoveProvider (left stick)
    ///            and ActionBasedContinuousTurnProvider (right stick).
    /// PC Mode  – Implements a full flycam: WASD/Arrows for translation, Q/E for elevation,
    ///            and right-mouse-drag for mouselook.
    ///
    /// Attach this script to the XR Origin GameObject. It will automatically locate or add
    /// the required XRI components on Awake.
    /// </summary>
    [RequireComponent(typeof(LocomotionSystem))]
    [RequireComponent(typeof(ActionBasedContinuousMoveProvider))]
    [RequireComponent(typeof(ActionBasedContinuousTurnProvider))]
    public class PlayerLocomotionManager : MonoBehaviour
    {
        // ──────────────────────────────────────────────
        //  Inspector-Tunable Parameters
        // ──────────────────────────────────────────────

        [Header("VR Locomotion")]
        [Tooltip("Translation speed for continuous VR movement (m/s).")]
        [SerializeField] private float vrMoveSpeed = 2.0f;

        [Tooltip("Rotation speed for continuous VR turning (°/s).")]
        [SerializeField] private float vrTurnSpeed = 60.0f;

        [Header("PC / Editor Flycam")]
        [Tooltip("Translation speed for keyboard movement (m/s).")]
        [SerializeField] private float pcMoveSpeed = 5.0f;

        [Tooltip("Mouse look sensitivity (°/pixel).")]
        [SerializeField] private float pcLookSensitivity = 2.0f;

        [Tooltip("Speed multiplier when holding Shift.")]
        [SerializeField] private float pcSprintMultiplier = 2.5f;

        // ──────────────────────────────────────────────
        //  Public Accessors (for runtime UI sliders, etc.)
        // ──────────────────────────────────────────────

        public float VRMoveSpeed
        {
            get => vrMoveSpeed;
            set { vrMoveSpeed = value; SyncVRProviderSettings(); }
        }

        public float VRTurnSpeed
        {
            get => vrTurnSpeed;
            set { vrTurnSpeed = value; SyncVRProviderSettings(); }
        }

        public float PCMoveSpeed       { get => pcMoveSpeed;        set => pcMoveSpeed = value; }
        public float PCLookSensitivity  { get => pcLookSensitivity;  set => pcLookSensitivity = value; }

        /// <summary>
        /// True when the flycam (PC mode) is active.
        /// Other scripts (e.g. CAD_UIPanelManipulator) can check this to avoid input conflicts.
        /// </summary>
        public bool IsFlycamActive => !_vrActive;

        /// <summary>
        /// True when a physical VR headset (Meta Quest 2) is active and driving the camera & controllers.
        /// </summary>
        public bool IsVRActive => _vrActive;

        /// <summary>
        /// Singleton-style accessor so other scripts can query locomotion state.
        /// </summary>
        public static PlayerLocomotionManager Instance { get; private set; }

        // ──────────────────────────────────────────────
        //  Internal State
        // ──────────────────────────────────────────────

        private ActionBasedContinuousMoveProvider _moveProvider;
        private ActionBasedContinuousTurnProvider _turnProvider;
        private LocomotionSystem _locomotionSystem;

        [Header("Camera Reference")]
        [Tooltip("The camera under the XR Origin. Auto-detected if left empty.")]
        [SerializeField] private Transform _cameraTransform;

        private float _pitch; // accumulated vertical (up/down) look angle
        private bool _vrActive;
        private float _lastVRCheckTime;
        private const float VR_CHECK_INTERVAL = 0.5f;

        // ──────────────────────────────────────────────
        //  Lifecycle
        // ──────────────────────────────────────────────

        private void Awake()
        {
            // Lightweight singleton (non-DontDestroyOnLoad).
            if (Instance == null) Instance = this;
            else if (Instance != this) { Destroy(this); return; }

            CacheComponents();
            InputDevices.deviceConnected += OnXRDeviceConnected;
            InputDevices.deviceDisconnected += OnXRDeviceDisconnected;

            _vrActive = CheckIsVRPresent();
            ConfigureMode();
        }

        private void Start()
        {
            // Re-evaluate on Start as OpenXR subsystems often finish initializing after Awake
            EvaluateVRState(forceReconfigure: true);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            InputDevices.deviceConnected -= OnXRDeviceConnected;
            InputDevices.deviceDisconnected -= OnXRDeviceDisconnected;
        }

        private void OnXRDeviceConnected(InputDevice device)
        {
            if ((device.characteristics & InputDeviceCharacteristics.HeadMounted) != 0 ||
                (device.characteristics & InputDeviceCharacteristics.Controller) != 0)
            {
                Debug.Log($"[PlayerLocomotionManager] XR device connected: {device.name} ({device.characteristics})");
                EvaluateVRState();
            }
        }

        private void OnXRDeviceDisconnected(InputDevice device)
        {
            if ((device.characteristics & InputDeviceCharacteristics.HeadMounted) != 0)
            {
                Debug.Log($"[PlayerLocomotionManager] XR HMD disconnected: {device.name}");
                EvaluateVRState();
            }
        }

        private void Update()
        {
            if (!_vrActive)
            {
                // Periodically check if Quest Link / OpenXR headset connected during runtime
                if (Time.time - _lastVRCheckTime > VR_CHECK_INTERVAL)
                {
                    _lastVRCheckTime = Time.time;
                    if (CheckIsVRPresent())
                    {
                        EvaluateVRState();
                        return;
                    }
                }

                HandlePCLocomotion();
            }
        }

        private void OnValidate()
        {
            // Keep Inspector tweaks in sync with the XRI providers at edit-time.
            if (_moveProvider != null && _turnProvider != null)
            {
                SyncVRProviderSettings();
            }
        }

        // ──────────────────────────────────────────────
        //  Initialization Helpers
        // ──────────────────────────────────────────────

        /// <summary>
        /// Caches references to the required XRI components and the main camera.
        /// </summary>
        private void CacheComponents()
        {
            _moveProvider     = GetComponent<ActionBasedContinuousMoveProvider>();
            _turnProvider     = GetComponent<ActionBasedContinuousTurnProvider>();
            _locomotionSystem = GetComponent<LocomotionSystem>();

            // Auto-detect camera if not assigned in the Inspector.
            if (_cameraTransform == null)
            {
                _cameraTransform = Camera.main != null
                    ? Camera.main.transform
                    : GetComponentInChildren<Camera>()?.transform;
            }

            if (_cameraTransform == null)
            {
                Debug.LogError("[PlayerLocomotionManager] No camera found. " +
                               "Ensure a Camera exists under the XR Origin hierarchy, " +
                               "or assign it manually in the Inspector.");
            }
            else
            {
                // Seed pitch from the camera's current local X rotation.
                float localX = _cameraTransform.localEulerAngles.x;
                _pitch = localX > 180f ? localX - 360f : localX;
            }
        }

        /// <summary>
        /// Checks whether a real VR headset (Meta Quest 2 via Quest Link / OpenXR) is actively connected.
        /// </summary>
        public static bool CheckIsVRPresent()
        {
            // 1. Check XRDisplaySubsystem for an actively running stereoscopic display
            var displaySubsystems = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displaySubsystems);
            for (int i = 0; i < displaySubsystems.Count; i++)
            {
                if (displaySubsystems[i] != null && displaySubsystems[i].running)
                {
                    return true;
                }
            }

            // 2. Check XR Input Devices for an active HeadMounted device
            var hmdDevices = new List<InputDevice>();
            InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.HeadMounted, hmdDevices);
            for (int i = 0; i < hmdDevices.Count; i++)
            {
                var device = hmdDevices[i];
                if (device.isValid)
                {
#if UNITY_EDITOR
                    // In Editor, ignore Mock HMD if user is looking for real Quest Link hardware
                    if (!string.IsNullOrEmpty(device.name) && device.name.Contains("Mock"))
                        continue;
#endif
                    return true;
                }
            }

            // 3. Check XRGeneralSettings active loader and running display subsystem
            var xrSettings = XRGeneralSettings.Instance;
            if (xrSettings != null && xrSettings.Manager != null && xrSettings.Manager.activeLoader != null)
            {
                if (displaySubsystems.Count > 0)
                {
                    return true;
                }
            }

            // 4. Legacy fallback
            if (XRSettings.isDeviceActive && !string.IsNullOrEmpty(XRSettings.loadedDeviceName))
            {
#if UNITY_EDITOR
                if (XRSettings.loadedDeviceName.Contains("Mock"))
                    return false;
#endif
                return true;
            }

            return false;
        }

        /// <summary>
        /// Evaluates VR hardware presence and updates locomotion configuration if changed.
        /// </summary>
        public void EvaluateVRState(bool forceReconfigure = false)
        {
            bool newVRState = CheckIsVRPresent();
            if (newVRState != _vrActive || forceReconfigure)
            {
                _vrActive = newVRState;
                ConfigureMode();
            }
        }

        /// <summary>
        /// Enables the appropriate locomotion mode and disables the other.
        /// </summary>
        private void ConfigureMode()
        {
            if (_vrActive)
            {
                ConfigureVRLocomotion();
            }
            else
            {
                ConfigurePCLocomotion();
            }
        }

        // ──────────────────────────────────────────────
        //  VR Configuration
        // ──────────────────────────────────────────────

        /// <summary>
        /// Wires the XRI continuous move and turn providers to the Inspector-tuned speeds.
        /// Re-enables TrackedPoseDriver on the camera so headset 6DoF tracking drives the view.
        /// Auto-binds left stick move and right stick turn if not already set.
        /// </summary>
        private void ConfigureVRLocomotion()
        {
            if (_moveProvider != null) _moveProvider.enabled = true;
            if (_turnProvider != null) _turnProvider.enabled = true;

            SyncVRProviderSettings();
            AutoBindLocomotionActions();

            // Move should use head-relative direction for comfortable traversal.
            if (_moveProvider != null && _cameraTransform != null)
            {
                _moveProvider.forwardSource = _cameraTransform;
            }

            // CRUCIAL: Re-enable TrackedPoseDriver on the camera so the Quest 2 headset
            // drives camera position & rotation in 3D VR stereoscopic space.
            if (_cameraTransform != null)
            {
                var trackedPose = _cameraTransform.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
                if (trackedPose != null)
                {
                    trackedPose.enabled = true;
                }

                var legacyPose = _cameraTransform.GetComponent<UnityEngine.SpatialTracking.TrackedPoseDriver>();
                if (legacyPose != null)
                {
                    legacyPose.enabled = true;
                }

                // Reset manual PC flycam pitch override so head tracking has 1:1 orientation
                _cameraTransform.localRotation = Quaternion.identity;
                _pitch = 0f;
            }

            Debug.Log($"[PlayerLocomotionManager] VR mode active. Head & controller 6DoF tracking enabled. Move: {vrMoveSpeed} m/s, Turn: {vrTurnSpeed} °/s.");
        }

        /// <summary>
        /// Automatically binds continuous Move and Turn input actions from InputActionManager
        /// if they are not already bound on the provider components.
        /// </summary>
        private void AutoBindLocomotionActions()
        {
            var inputActionMgr = FindObjectOfType<InputActionManager>();
            if (inputActionMgr == null || inputActionMgr.actionAssets == null) return;

            foreach (var asset in inputActionMgr.actionAssets)
            {
                if (asset == null) continue;

                if (_moveProvider != null && (_moveProvider.leftHandMoveAction.action == null || _moveProvider.leftHandMoveAction.reference == null))
                {
                    var moveAction = asset.FindAction("XRI LeftHand Locomotion/Move") ?? asset.FindAction("Move");
                    if (moveAction != null)
                    {
                        _moveProvider.leftHandMoveAction = new InputActionProperty(moveAction);
                    }
                }

                if (_turnProvider != null && (_turnProvider.rightHandTurnAction.action == null || _turnProvider.rightHandTurnAction.reference == null))
                {
                    var turnAction = asset.FindAction("XRI RightHand Locomotion/Turn") ?? asset.FindAction("Turn");
                    if (turnAction != null)
                    {
                        _turnProvider.rightHandTurnAction = new InputActionProperty(turnAction);
                    }
                }
            }
        }

        /// <summary>
        /// Pushes the current Inspector speed values into the XRI provider components.
        /// </summary>
        private void SyncVRProviderSettings()
        {
            if (_moveProvider != null) _moveProvider.moveSpeed = vrMoveSpeed;
            if (_turnProvider != null) _turnProvider.turnSpeed  = vrTurnSpeed;
        }

        // ──────────────────────────────────────────────
        //  PC / Editor Flycam
        // ──────────────────────────────────────────────

        /// <summary>
        /// Disables the VR providers and prepares for keyboard + mouse input.
        /// Also disables the TrackedPoseDriver on the camera so manual transforms
        /// aren't overridden by XRI's tracked-pose pipeline each frame.
        /// </summary>
        private void ConfigurePCLocomotion()
        {
            // Disable XRI providers so they don't fight with manual transforms.
            if (_moveProvider != null) _moveProvider.enabled = false;
            if (_turnProvider != null) _turnProvider.enabled = false;

            // Disable TrackedPoseDriver on the camera so manual flycam rotation/position
            // aren't overwritten by the XR tracking pipeline.
            if (_cameraTransform != null)
            {
                var trackedPose = _cameraTransform.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
                if (trackedPose != null) trackedPose.enabled = false;

                // Also check legacy TrackedPoseDriver
                var legacyPose = _cameraTransform.GetComponent<UnityEngine.SpatialTracking.TrackedPoseDriver>();
                if (legacyPose != null) legacyPose.enabled = false;

                // Set initial downward pitch to naturally frame both the UI panel and the CAD workspace table
                _pitch = 14f;
                _cameraTransform.localEulerAngles = new Vector3(_pitch, 0f, 0f);
            }

            Debug.Log("[PlayerLocomotionManager] PC flycam enabled. " +
                      $"Move: {pcMoveSpeed} m/s, Look Sensitivity: {pcLookSensitivity}.");
        }

        /// <summary>
        /// Processes keyboard translation (WASD / Arrows / Q-E) and
        /// right-mouse-button mouselook each frame.
        /// </summary>
        private void HandlePCLocomotion()
        {
            if (_cameraTransform == null) return;

            HandleMouseLook();
            HandleKeyboardMovement();
        }

        /// <summary>
        /// Applies mouselook rotation while the right mouse button is held.
        ///
        /// Split-axis approach:
        ///   • Yaw  (Mouse X) → rotates the ROOT transform so WASD forward stays aligned.
        ///   • Pitch (Mouse Y) → rotates only the CAMERA via localEulerAngles so it tilts
        ///     up/down independently without affecting the movement plane.
        /// </summary>
        private void HandleMouseLook()
        {
            if (!Input.GetMouseButton(1)) return; // RMB not held

            float mouseX = Input.GetAxis("Mouse X");
            float mouseY = Input.GetAxis("Mouse Y");

            // ── Yaw: rotate the XR Origin root around world Y ──
            // This keeps transform.forward aligned with where the player is looking,
            // so WASD movement naturally follows the view direction.
            transform.Rotate(0f, mouseX * pcLookSensitivity, 0f, Space.World);

            // ── Pitch: tilt the camera up/down locally ──
            _pitch -= mouseY * pcLookSensitivity;
            _pitch  = Mathf.Clamp(_pitch, -89f, 89f);
            _cameraTransform.localEulerAngles = new Vector3(_pitch, 0f, 0f);
        }

        /// <summary>
        /// Translates the rig based on WASD / Arrow Keys plus Q (down) and E (up).
        /// Movement is relative to the ROOT's forward/right (which yaw keeps aligned
        /// with the view) so that WASD always matches where the player is looking.
        /// </summary>
        private void HandleKeyboardMovement()
        {
            // Read axes — these map to WASD and Arrow Keys by default.
            float horizontal = Input.GetAxis("Horizontal"); // A/D or Left/Right
            float vertical   = Input.GetAxis("Vertical");   // W/S or Up/Down

            float elevation = 0f;
            if (Input.GetKey(KeyCode.E)) elevation =  1f;
            if (Input.GetKey(KeyCode.Q)) elevation = -1f;

            if (Mathf.Approximately(horizontal, 0f) && Mathf.Approximately(vertical, 0f) && Mathf.Approximately(elevation, 0f))
                return;

            // Sprint modifier
            float speed = pcMoveSpeed;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                speed *= pcSprintMultiplier;

            // Use the ROOT's forward/right so movement stays on the horizontal plane
            // and always matches the yaw direction the player is facing.
            Vector3 direction = transform.right   * horizontal
                              + transform.forward * vertical
                              + Vector3.up        * elevation;

            // Move the XR Origin rig root so the camera (child) moves with it.
            transform.position += direction.normalized * speed * Time.deltaTime;
        }
    }
}
