using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Management;

namespace VRCAD.Core
{
    /// <summary>Development-build evidence only. Observations never automatically pass a hardware test.</summary>
    public class QuestBaselineRecorder : MonoBehaviour
    {
        [Serializable]
        private class Entry
        {
            public string utc, mode, category, detail;
            public float elapsedSeconds;
        }

        private readonly Dictionary<Button, UnityAction> buttons = new Dictionary<Button, UnityAction>();
        private readonly Dictionary<string, bool> inputState = new Dictionary<string, bool>();
        private StreamWriter writer;
        private string mode;
        private float nextPoseSample;
        private CADManagerHub hub;

        private IEnumerator Start()
        {
            if (!Application.isEditor && !Debug.isDebugBuild) { enabled = false; yield break; }
            mode = Application.platform == RuntimePlatform.Android ? "Quest2-Standalone" : "QuestLink-PC";
            var directory = Path.Combine(Application.persistentDataPath, "QuestBaseline", mode);
            try
            {
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + ".jsonl");
                writer = new StreamWriter(path, false) { AutoFlush = true };
                Debug.Log("[QuestBaseline] Recording observations to " + path);
            }
            catch (Exception ex) { Debug.LogWarning("[QuestBaseline] Cannot record evidence: " + ex.Message); yield break; }
            Application.logMessageReceived += OnLog;
            Record("session", $"Unity={Application.unityVersion}; app={Application.version}; platform={Application.platform}; " +
                $"device={SystemInfo.deviceModel}; graphics={SystemInfo.graphicsDeviceType}; " +
                "Observations only; visual tracking and interaction acceptance must be recorded by the tester.");
            yield return null; // Wait for runtime UI construction and the hub's Awake.
            hub = CADManagerHub.Instance;
            if (hub != null) hub.StatusMessageEmitted += OnStatus;
            while (enabled)
            {
                foreach (var button in FindObjectsOfType<Button>(true))
                {
                    if (button == null || buttons.ContainsKey(button)) continue;
                    var captured = button;
                    UnityAction action = () => {
                        if (captured != null)
                            Record("ui-click", captured.name);
                    };
                    buttons.Add(button, action);
                    button.onClick.AddListener(action);
                }
                yield return new WaitForSecondsRealtime(2f);
            }
        }

        private void Update()
        {
            if (writer == null) return;
            bool samplePose = Time.unscaledTime >= nextPoseSample;
            if (samplePose)
            {
                nextPoseSample = Time.unscaledTime + 5f;
                Record("xr-state", $"deviceActive={XRSettings.isDeviceActive}; " +
                    $"loader={XRGeneralSettings.Instance?.Manager?.activeLoader?.name ?? "none"}");
            }
            foreach (var node in new[] { XRNode.Head, XRNode.LeftHand, XRNode.RightHand })
            {
                var device = InputDevices.GetDeviceAtXRNode(node);
                device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked);
                Observe(node + ":valid", device.isValid);
                Observe(node + ":tracked", tracked);
                if (samplePose && device.isValid)
                {
                    device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 position);
                    device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rotation);
                    Record("pose", FormattableString.Invariant($"{node}; name={device.name}; tracked={tracked}; position={position:F4}; rotation={rotation:F4}"));
                }
                if (node == XRNode.Head || !device.isValid) continue;
                ObserveButton(device, node, CommonUsages.triggerButton);
                ObserveButton(device, node, CommonUsages.gripButton);
                ObserveButton(device, node, CommonUsages.primaryButton);
                ObserveButton(device, node, CommonUsages.secondaryButton);
                if (device.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 stick))
                    Observe(node + ":thumbstick-moved", stick.sqrMagnitude > 0.1f);
            }
        }

        private void ObserveButton(InputDevice device, XRNode node, InputFeatureUsage<bool> usage)
        {
            if (device.TryGetFeatureValue(usage, out bool pressed)) Observe(node + ":" + usage.name, pressed);
        }

        private void Observe(string key, bool value)
        {
            if (inputState.TryGetValue(key, out bool previous) && previous == value) return;
            inputState[key] = value;
            Record("input-state", key + "=" + value);
        }

        private void OnStatus(string message) => Record("cad-status", message);

        private void OnLog(string message, string stack, LogType type)
        {
            if (string.IsNullOrEmpty(message)) return;
            if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
                Record("runtime-error", message + "\n" + stack);
            else if (message.StartsWith("[CAD_ExportManager]") || message.StartsWith("[QuestBaseline Interaction]"))
                Record("operation", message);
        }

        private void Record(string category, string detail)
        {
            if (writer == null) return;
            try
            {
                writer.WriteLine(JsonUtility.ToJson(new Entry
                {
                    utc = DateTime.UtcNow.ToString("O"), mode = mode, category = category,
                    detail = detail, elapsedSeconds = Time.unscaledTime
                }));
            }
            catch (Exception)
            {
                try { writer?.Dispose(); } catch {}
                writer = null;
            }
        }

        private void OnApplicationPause(bool paused) => Record("pause", paused.ToString());

        private void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
            if (hub != null) hub.StatusMessageEmitted -= OnStatus;
            foreach (var pair in buttons)
                if (pair.Key != null) pair.Key.onClick.RemoveListener(pair.Value);
            buttons.Clear();
            Record("session-end", "Recorder destroyed");
            try { writer?.Dispose(); } catch {}
            writer = null;
        }
    }
}
