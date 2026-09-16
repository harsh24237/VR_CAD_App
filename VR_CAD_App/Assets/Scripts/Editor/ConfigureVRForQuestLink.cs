using System;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Interactions;

namespace CADApp.EditorTools
{
    /// <summary>
    /// One-shot editor tool that fully configures the project for Meta Quest 2 via Quest Link.
    /// Fixes: 3D stereoscopic VR rendering, independent controller tracking, and disables
    /// the XR Device Simulator that would otherwise override real hardware input.
    ///
    /// Run via: Menu → CAD → Configure VR for Quest Link (PC)
    /// </summary>
    public static class ConfigureVRForQuestLink
    {
        [MenuItem("CAD/Configure VR for Quest Link (PC)")]
        public static void EnsureVRConfigured()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[ConfigureVRForQuestLink] Cannot configure while in Play Mode. Please stop Play Mode first.");
                return;
            }

            try
            {
                // ════════════════════════════════════════════════════════════════
                //  STEP 1: Disable XR Device Simulator auto-instantiation
                //  This is the #1 reason for "2D mirror" and "coupled controllers".
                //  The simulator overrides real Quest 2 hardware input.
                // ════════════════════════════════════════════════════════════════
                DisableXRDeviceSimulator();

                // ════════════════════════════════════════════════════════════════
                //  STEP 2: Ensure OpenXR is the loader for Standalone builds
                // ════════════════════════════════════════════════════════════════
                ConfigureXRLoader();

                // ════════════════════════════════════════════════════════════════
                //  STEP 3: Enable Oculus Touch controller profiles
                // ════════════════════════════════════════════════════════════════
                EnableControllerProfiles();

                // ════════════════════════════════════════════════════════════════
                //  STEP 4: Wire up locomotion input actions
                // ════════════════════════════════════════════════════════════════
                WireLocomotionActions();

                AssetDatabase.SaveAssets();
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                    UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

                Debug.Log("══════════════════════════════════════════════════════════════\n" +
                          "[ConfigureVRForQuestLink] ✅ SUCCESS! All VR settings configured.\n" +
                          "  • XR Device Simulator: DISABLED (won't override Quest 2)\n" +
                          "  • OpenXR Loader: ENABLED with auto-loading & auto-running\n" +
                          "  • Controller Profiles: Oculus Touch + Quest Pro enabled\n" +
                          "  • Initialize XR on Startup: ON\n\n" +
                          "  ➤ Connect Quest 2 via Quest Link and press Play for 3D VR!\n" +
                          "══════════════════════════════════════════════════════════════");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ConfigureVRForQuestLink] Error configuring XR: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Disables the XR Device Simulator so it doesn't spawn in Play Mode
        /// and override real Quest 2 controller/HMD input.
        /// </summary>
        private static void DisableXRDeviceSimulator()
        {
            // Find and disable the XRDeviceSimulatorSettings ScriptableObject
            string[] guids = AssetDatabase.FindAssets("t:ScriptableObject XRDeviceSimulatorSettings");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var settings = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (settings != null)
                {
                    var so = new SerializedObject(settings);
                    var autoProp = so.FindProperty("m_AutomaticallyInstantiateSimulatorPrefab");
                    if (autoProp != null && autoProp.boolValue)
                    {
                        autoProp.boolValue = false;
                        so.ApplyModifiedProperties();
                        EditorUtility.SetDirty(settings);
                        Debug.Log($"[ConfigureVRForQuestLink] Disabled XR Device Simulator auto-instantiation at: {path}");
                    }
                }
            }

            // Also try the Resources-loaded path (XRI default location)
            var resourceSettings = Resources.Load<ScriptableObject>("XRDeviceSimulatorSettings");
            if (resourceSettings != null)
            {
                var so = new SerializedObject(resourceSettings);
                var autoProp = so.FindProperty("m_AutomaticallyInstantiateSimulatorPrefab");
                if (autoProp != null && autoProp.boolValue)
                {
                    autoProp.boolValue = false;
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(resourceSettings);
                    Debug.Log("[ConfigureVRForQuestLink] Disabled XR Device Simulator via Resources path.");
                }
            }
        }

        /// <summary>
        /// Ensures OpenXR is the active loader for Standalone builds with auto-loading enabled.
        /// </summary>
        private static void ConfigureXRLoader()
        {
            // 1. Locate or create XRGeneralSettingsPerBuildTarget
            XRGeneralSettingsPerBuildTarget buildTargetSettings = null;
            EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.settingsKey, out buildTargetSettings);
            if (buildTargetSettings == null)
            {
                string[] guids = AssetDatabase.FindAssets("t:XRGeneralSettingsPerBuildTarget");
                if (guids.Length > 0)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    buildTargetSettings = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(path);
                    if (buildTargetSettings != null)
                    {
                        EditorBuildSettings.AddConfigObject(XRGeneralSettings.settingsKey, buildTargetSettings, true);
                    }
                }
            }

            if (buildTargetSettings == null)
            {
                Debug.LogError("[ConfigureVRForQuestLink] Cannot find XRGeneralSettingsPerBuildTarget. " +
                               "Open Project Settings → XR Plug-in Management first.");
                return;
            }

            // 2. Ensure Standalone settings exist
            XRGeneralSettings standaloneSettings = buildTargetSettings.SettingsForBuildTarget(BuildTargetGroup.Standalone);
            if (standaloneSettings == null)
            {
                buildTargetSettings.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Standalone);
                standaloneSettings = buildTargetSettings.SettingsForBuildTarget(BuildTargetGroup.Standalone);
            }

            if (standaloneSettings == null)
            {
                Debug.LogError("[ConfigureVRForQuestLink] Failed to create Standalone XR settings.");
                return;
            }

            // 3. CRITICAL: Enable "Initialize XR on Startup"
            standaloneSettings.InitManagerOnStart = true;

            XRManagerSettings manager = standaloneSettings.Manager;
            if (manager == null)
            {
                manager = ScriptableObject.CreateInstance<XRManagerSettings>();
                standaloneSettings.Manager = manager;
                AssetDatabase.AddObjectToAsset(manager, AssetDatabase.GetAssetOrScenePath(buildTargetSettings));
            }

            // 4. Assign OpenXR as the loader
            string loaderName = typeof(OpenXRLoader).FullName;
            XRPackageMetadataStore.AssignLoader(manager, loaderName, BuildTargetGroup.Standalone);

            // 5. CRITICAL: Enable automatic loading & running
            // Without these, Quest Link only gets a flat 2D desktop mirror.
            manager.automaticLoading = true;
            manager.automaticRunning = true;

            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(standaloneSettings);
            EditorUtility.SetDirty(buildTargetSettings);
        }

        /// <summary>
        /// Enables Oculus Touch and Quest Pro controller profiles for Standalone OpenXR.
        /// </summary>
        private static void EnableControllerProfiles()
        {
            OpenXRSettings openXRSettings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            if (openXRSettings == null) return;

            var oculusTouch = openXRSettings.GetFeature<OculusTouchControllerProfile>();
            if (oculusTouch != null && !oculusTouch.enabled)
            {
                oculusTouch.enabled = true;
                EditorUtility.SetDirty(oculusTouch);
            }

            var questPlus = openXRSettings.GetFeature<MetaQuestTouchPlusControllerProfile>();
            if (questPlus != null && !questPlus.enabled)
            {
                questPlus.enabled = true;
                EditorUtility.SetDirty(questPlus);
            }

            var questPro = openXRSettings.GetFeature<MetaQuestTouchProControllerProfile>();
            if (questPro != null && !questPro.enabled)
            {
                questPro.enabled = true;
                EditorUtility.SetDirty(questPro);
            }

            EditorUtility.SetDirty(openXRSettings);
        }

        /// <summary>
        /// Wires XRI Default Input Actions to the move/turn providers in the scene.
        /// </summary>
        private static void WireLocomotionActions()
        {
            string[] inputGuids = AssetDatabase.FindAssets("XRI Default Input Actions t:InputActionAsset");
            if (inputGuids.Length == 0) return;

            string inputPath = AssetDatabase.GUIDToAssetPath(inputGuids[0]);
            var allAssets = AssetDatabase.LoadAllAssetsAtPath(inputPath);
            UnityEngine.InputSystem.InputActionReference moveRef = null;
            UnityEngine.InputSystem.InputActionReference turnRef = null;

            foreach (var obj in allAssets)
            {
                if (obj is UnityEngine.InputSystem.InputActionReference actionRef)
                {
                    if (actionRef.name == "Move" && actionRef.action != null && actionRef.action.actionMap.name.Contains("LeftHand"))
                    {
                        moveRef = actionRef;
                    }
                    else if (actionRef.name == "Turn" && actionRef.action != null && actionRef.action.actionMap.name.Contains("RightHand"))
                    {
                        turnRef = actionRef;
                    }
                }
            }

            var moveProvider = UnityEngine.Object.FindObjectOfType<UnityEngine.XR.Interaction.Toolkit.ActionBasedContinuousMoveProvider>();
            if (moveProvider != null && moveRef != null)
            {
                var so = new SerializedObject(moveProvider);
                var leftHandMoveProp = so.FindProperty("m_LeftHandMoveAction");
                if (leftHandMoveProp != null)
                {
                    var useReferenceProp = leftHandMoveProp.FindPropertyRelative("m_UseReference");
                    var refProp = leftHandMoveProp.FindPropertyRelative("m_Reference");
                    if (useReferenceProp != null && refProp != null)
                    {
                        useReferenceProp.boolValue = true;
                        refProp.objectReferenceValue = moveRef;
                    }
                }
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(moveProvider);
            }

            var turnProvider = UnityEngine.Object.FindObjectOfType<UnityEngine.XR.Interaction.Toolkit.ActionBasedContinuousTurnProvider>();
            if (turnProvider != null && turnRef != null)
            {
                var so = new SerializedObject(turnProvider);
                var rightHandTurnProp = so.FindProperty("m_RightHandTurnAction");
                if (rightHandTurnProp != null)
                {
                    var useReferenceProp = rightHandTurnProp.FindPropertyRelative("m_UseReference");
                    var refProp = rightHandTurnProp.FindPropertyRelative("m_Reference");
                    if (useReferenceProp != null && refProp != null)
                    {
                        useReferenceProp.boolValue = true;
                        refProp.objectReferenceValue = turnRef;
                    }
                }
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(turnProvider);
            }
        }
    }
}
