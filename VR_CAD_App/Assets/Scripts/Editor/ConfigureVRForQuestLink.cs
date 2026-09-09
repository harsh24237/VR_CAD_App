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
    public static class ConfigureVRForQuestLink
    {
        [MenuItem("CAD/Configure VR for Quest Link (PC)")]
        public static void EnsureVRConfigured()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            try
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

                if (buildTargetSettings != null)
                {
                    // 2. Ensure Standalone settings exist
                    XRGeneralSettings standaloneSettings = buildTargetSettings.SettingsForBuildTarget(BuildTargetGroup.Standalone);
                    if (standaloneSettings == null)
                    {
                        buildTargetSettings.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Standalone);
                        standaloneSettings = buildTargetSettings.SettingsForBuildTarget(BuildTargetGroup.Standalone);
                    }

                    if (standaloneSettings != null)
                    {
                        standaloneSettings.InitManagerOnStart = true;
                        XRManagerSettings manager = standaloneSettings.Manager;
                        if (manager == null)
                        {
                            manager = ScriptableObject.CreateInstance<XRManagerSettings>();
                            standaloneSettings.Manager = manager;
                            AssetDatabase.AddObjectToAsset(manager, AssetDatabase.GetAssetOrScenePath(buildTargetSettings));
                        }

                        // 3. Assign OpenXRLoader to Standalone
                        string loaderName = typeof(OpenXRLoader).FullName;
                        XRPackageMetadataStore.AssignLoader(manager, loaderName, BuildTargetGroup.Standalone);

                        EditorUtility.SetDirty(manager);
                        EditorUtility.SetDirty(standaloneSettings);
                        EditorUtility.SetDirty(buildTargetSettings);
                    }
                }

                // 4. Ensure Oculus Touch Controller Profile is enabled for Standalone OpenXR
                OpenXRSettings openXRSettings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
                if (openXRSettings != null)
                {
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

                // 5. Ensure ActionBasedContinuousMoveProvider and ActionBasedContinuousTurnProvider in the scene are wired to XRI input actions
                string[] inputGuids = AssetDatabase.FindAssets("XRI Default Input Actions t:InputActionAsset");
                if (inputGuids.Length > 0)
                {
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

                AssetDatabase.SaveAssets();
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
                Debug.Log("[ConfigureVRForQuestLink] Successfully configured OpenXR for Standalone / Quest Link! 3D VR and independent controllers are now active.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ConfigureVRForQuestLink] Error configuring XR: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}
