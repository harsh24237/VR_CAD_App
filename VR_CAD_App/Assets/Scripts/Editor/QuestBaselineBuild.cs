using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;
using VRCAD.Core;

namespace CADApp.EditorTools
{
    // Batch entry points deliberately throw on failure so automation cannot report false success.
    public static class QuestBaselineBuild
    {
        public const string ScenePath = "Assets/Scenes/SampleScene.unity";
        public const string PackageId = "com.vrcad.questbaseline";
        private static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
        private static string ReportDirectory => Path.Combine(ProjectRoot, "Logs/QuestBaseline");

        [MenuItem("CAD/Quest Baseline/Configure Hardware Baseline")]
        public static void Configure()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before configuring XR.");
            if (Application.isBatchMode)
                Require(File.Exists(Path.Combine(ProjectRoot, "baseline-copy.json")), "Batch configuration is permitted only in the synchronized validation copy.");
            else if (!EditorUtility.DisplayDialog("Change this project's XR/build settings?",
                "Enable hardware OpenXR/Meta Quest, disable simulator auto-start, set the baseline Android application ID, " +
                "ARM64/IL2CPP, SDK levels, Vulkan and Windows D3D11. These changes will be saved in this project.",
                "Apply settings", "Cancel"))
                return;

            foreach (var group in new[] { BuildTargetGroup.Android, BuildTargetGroup.Standalone })
            {
                var general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(group);
                Require(general != null && general.Manager != null, $"Missing XR settings for {group}.");
                Require(XRPackageMetadataStore.AssignLoader(general.Manager, typeof(OpenXRLoader).FullName, group),
                    $"Unable to assign OpenXR for {group}.");
                general.InitManagerOnStart = true;
                general.Manager.automaticLoading = true;
                general.Manager.automaticRunning = true;
                EditorUtility.SetDirty(general);
                EditorUtility.SetDirty(general.Manager);

                var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
                Require(settings != null, $"Missing OpenXR settings for {group}.");
                var touch = settings.GetFeature<OculusTouchControllerProfile>();
                Require(touch != null, $"Missing Oculus Touch profile for {group}.");
                touch.enabled = true;
                EditorUtility.SetDirty(touch);
                if (group == BuildTargetGroup.Android)
                {
                    var quest = settings.GetFeature<MetaQuestFeature>();
                    Require(quest != null, "Meta Quest Support feature is missing.");
                    quest.enabled = true;
                    var serializedQuest = new SerializedObject(quest);
                    var devices = serializedQuest.FindProperty("targetDevices");
                    for (int i = 0; i < devices.arraySize; i++)
                    {
                        var device = devices.GetArrayElementAtIndex(i);
                        if (device.FindPropertyRelative("manifestName").stringValue == "quest2")
                            device.FindPropertyRelative("enabled").boolValue = true;
                    }
                    serializedQuest.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(quest);
                }
                EditorUtility.SetDirty(settings);
            }

            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, PackageId);
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            // Local sideload baseline, not a declaration of current store submission requirements.
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
            EditorUserBuildSettings.buildAppBundle = false;

            var simulator = AssetDatabase.LoadAssetAtPath<ScriptableObject>(
                "Assets/XRI/Settings/Resources/XRDeviceSimulatorSettings.asset");
            Require(simulator != null, "Simulator settings asset missing.");
            var simulatorSettings = new SerializedObject(simulator);
            simulatorSettings.FindProperty("m_AutomaticallyInstantiateSimulatorPrefab").boolValue = false;
            simulatorSettings.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(simulator);
            AssetDatabase.SaveAssets();
            Debug.Log("[QuestBaseline] Hardware configuration saved. This is not a device test result.");
        }

        [MenuItem("CAD/Quest Baseline/Validate Active Target and Export")]
        public static void Validate()
        {
            Directory.CreateDirectory(ReportDirectory);
            var target = EditorUserBuildSettings.activeBuildTarget;
            var group = BuildPipeline.GetBuildTargetGroup(target);
            var lines = new List<string> { $"UTC: {DateTime.UtcNow:O}", $"Unity: {Application.unityVersion}", $"Target: {target}" };
            int errors = 0;
            void Check(bool condition, string message)
            {
                lines.Add((condition ? "PASS: " : "FAIL: ") + message);
                if (!condition) errors++;
            }

            Check(target == BuildTarget.Android || target == BuildTarget.StandaloneWindows64, "Android or Windows x64 target");
            var general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(group);
            Check(general != null && general.InitManagerOnStart && general.Manager != null &&
                general.Manager.automaticLoading && general.Manager.automaticRunning &&
                general.Manager.activeLoaders.Any(loader => loader is OpenXRLoader), "OpenXR startup and loader");
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            Check(settings != null && settings.GetFeature<OculusTouchControllerProfile>()?.enabled == true, "Oculus Touch profile");
            if (group == BuildTargetGroup.Android)
            {
                Check(settings != null && settings.GetFeature<MetaQuestFeature>()?.enabled == true, "Meta Quest Support");
                Check(PlayerSettings.GetScriptingBackend(group) == ScriptingImplementation.IL2CPP &&
                    PlayerSettings.Android.targetArchitectures == AndroidArchitecture.ARM64, "IL2CPP / ARM64");
                Check(PlayerSettings.GetApplicationIdentifier(group) == PackageId, "Baseline application ID");
            }
            var simulator = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/XRI/Settings/Resources/XRDeviceSimulatorSettings.asset");
            Check(simulator != null && !new SerializedObject(simulator).FindProperty("m_AutomaticallyInstantiateSimulatorPrefab").boolValue,
                "Simulator auto-spawn disabled for hardware testing");
            var issues = new List<OpenXRFeature.ValidationRule>();
            OpenXRProjectValidation.GetCurrentValidationIssues(issues, group);
            foreach (var issue in issues)
            {
                lines.Add((issue.error ? "FAIL: " : "WARNING: ") + issue.message);
                if (issue.error) errors++;
            }

            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(ScenePath);
            bool openedForValidation = !scene.IsValid() || !scene.isLoaded;
            if (openedForValidation) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var roots = scene.GetRootGameObjects();
                Check(roots.Sum(root => root.GetComponentsInChildren<CADManagerHub>(true).Length) == 1, "One CAD manager hub");
                // Gaze and teleport interactors also carry ActionBasedController components;
                // they do not need the physical hand's complete grab/UI action set.
                var controllers = roots.SelectMany(root => root.GetComponentsInChildren<ActionBasedController>(true))
                    .Where(c => c.name == "Left Controller" || c.name == "Right Controller").ToArray();
                Check(controllers.Count(c => c.name == "Left Controller") == 1 &&
                    controllers.Count(c => c.name == "Right Controller") == 1, "Left and right action-based controllers");
                foreach (var controller in controllers)
                    Check(controller.positionAction.action != null && controller.rotationAction.action != null &&
                        controller.selectAction.action != null && controller.uiPressAction.action != null,
                        $"{controller.name}: pose, grab and UI action references");
                Check(roots.Sum(root => root.GetComponentsInChildren<Camera>(true).Count(c => c.CompareTag("MainCamera"))) == 1,
                    "One main camera");
                Check(roots.All(root => root.GetComponentsInChildren<Transform>(true).All(t =>
                    GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) == 0)), "No missing scene scripts");
            }
            finally { if (openedForValidation) EditorSceneManager.CloseScene(scene, true); }

            try { VerifyExport(); lines.Add("PASS: STL/OBJ export smoke checks (200 mm cube, decimal-comma locale)"); }
            catch (Exception ex) { lines.Add("FAIL: Export: " + ex); errors++; }
            lines.Add("Tracking, input, grabbing and UI still require hardware acceptance; static checks do not pass them.");
            File.WriteAllLines(Path.Combine(ReportDirectory, $"validation-{target}.txt"), lines);
            foreach (var line in lines) Debug.Log("[QuestBaseline] " + line);
            Require(errors == 0, $"{errors} baseline validation failure(s). See Logs/QuestBaseline.");
        }

        public static void BuildAndroid() => Build(BuildTarget.Android, "Builds/Quest2/VR_CAD_Quest2.apk");
        public static void BuildLink() => Build(BuildTarget.StandaloneWindows64, "Builds/QuestLink/VR_CAD_QuestLink.exe");

        private static void Build(BuildTarget target, string path)
        {
            path = Path.Combine(ProjectRoot, path);
            Require(EditorUserBuildSettings.activeBuildTarget == target, "Start Unity with the matching -buildTarget argument.");
            Configure();
            Validate();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath }, target = target, locationPathName = path,
                options = BuildOptions.Development
            });
            var summary = report.summary;
            File.WriteAllLines(Path.Combine(ReportDirectory, $"build-{target}.txt"), new[]
            {
                $"UTC: {DateTime.UtcNow:O}", $"Result: {summary.result}", $"Errors: {summary.totalErrors}",
                $"Warnings: {summary.totalWarnings}", $"Bytes: {summary.totalSize}", $"Output: {summary.outputPath}"
            });
            Require(summary.result == BuildResult.Succeeded, $"{target} build failed: {summary.result}");
        }

        private static void VerifyExport()
        {
            var previousCulture = CultureInfo.CurrentCulture;
            var fixture = new GameObject("BaselineCube");
            var mesh = CAD_ShapeManager.CreateBoxMesh(1f, 1f, 1f);
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
                var cad = fixture.AddComponent<CADObject>();
                fixture.transform.localScale = Vector3.one * 0.2f;
                cad.SetMesh(mesh);
                var exporter = fixture.AddComponent<CAD_ExportManager>();
                var exportSettings = new SerializedObject(exporter);
                exportSettings.FindProperty("customExportPath").stringValue = Path.Combine(ReportDirectory, "export-smoke");
                exportSettings.ApplyModifiedPropertiesWithoutUndo();
                string binary = exporter.ExportToSTL(cad);
                using (var reader = new BinaryReader(File.OpenRead(binary)))
                {
                    Require(reader.BaseStream.Length == 84 + 12 * 50, "Unexpected binary STL size.");
                    reader.BaseStream.Position = 80;
                    Require(reader.ReadUInt32() == 12, "Expected 12 cube triangles.");
                    var bounds = new Bounds();
                    for (int triangle = 0; triangle < 12; triangle++)
                    {
                        reader.BaseStream.Position += 12;
                        for (int vertex = 0; vertex < 3; vertex++)
                            bounds.Encapsulate(new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()));
                        reader.ReadUInt16();
                    }
                    Require((bounds.size - Vector3.one * 0.2f).sqrMagnitude < 0.000001f, "STL world dimensions changed.");
                }
                // Same timestamp can reuse the STL name; the binary has already been checked.
                string ascii = File.ReadAllText(exporter.ExportToSTL(cad, false));
                Require(!ascii.Contains(",") && ascii.Contains("facet normal"), "ASCII STL is not locale-independent.");
                string obj = File.ReadAllText(exporter.ExportToOBJ(cad));
                Require(!obj.Contains(",") && obj.Split('\n').Count(line => line.StartsWith("f ")) == 12,
                    "OBJ locale or face count is incorrect.");
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
                UnityEngine.Object.DestroyImmediate(fixture);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new BuildFailedException(message);
        }
    }
}
