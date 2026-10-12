# Quest 2 baseline: build and acceptance procedure

This baseline has two independent targets: **Quest Link on Windows** and **standalone Quest 2 on Android**. A successful PC run does not pass the Android baseline. Desktop mouse simulation does not pass either hardware baseline.

## Requirements and configuration

- Open this `VR_CAD_App` directory with Unity **2022.3.44f1**, with an activated Editor license.
- Android Build Support, SDK/NDK and OpenJDK from that editor must be installed.
- Standalone: Quest 2 in Developer Mode, USB connected, USB debugging authorized inside the headset.
- Link: install/configure Meta Quest Link, set it as the active Windows OpenXR runtime, connect the Quest 2 and enter Link. The scripts inspect this setting but do not change the machine's runtime.
- Hardware baseline disables XRI Device Simulator auto-spawn. The existing desktop controls remain available. Re-enable simulator auto-spawn only when deliberately testing simulation; disable it again before hardware acceptance.

The original project is the single source of truth. Keep Unity open and make all development changes here. The build wrapper synchronizes saved `Assets`, `Packages`, `ProjectSettings` and `Tools` into the repository's `.baseline-workspace/VR_CAD_App` before **each** build, verifies SHA256 hashes, then builds there. It never copies project files back. Save any unsaved scene/prefab edits before a build so the copy includes them.

Only in the validation copy, the build configures Meta Quest Support and Oculus Touch, automatic OpenXR initialization on both targets, Android ARM64/IL2CPP, Vulkan on Android, D3D11 on Windows, and sideload application ID `com.vrcad.questbaseline`. SDK 29 minimum / 32 target is a local baseline, not a store submission policy. The original project's **CAD > Quest Baseline > Configure Hardware Baseline** menu asks for explicit confirmation before saving any settings. Do not use it unless you intend to change the original settings.

## Reproducible commands

Run PowerShell from the original project directory. **Keep your original Unity Editor open.** `-UnityPath` overrides the default editor location. Builds run sequentially because they share the validation copy.

```powershell
.\Tools\QuestBaseline.ps1 -Action Preflight
.\Tools\Build-QuestBaseline.ps1 -Target Link
.\Tools\Build-QuestBaseline.ps1 -Target Android
& ..\.baseline-workspace\VR_CAD_App\Tools\QuestBaseline.ps1 -Action Install
```

Builds run validation first and stop on errors. The active build target is selected before scripts execute so Android-only code is compiled. Validation inspects OpenXR rules, scene script references, controller input references, and binary STL/ASCII STL/OBJ export using a 0.2 m cube under a decimal-comma locale. It cannot validate real headset behavior.

Artifacts stay in `.baseline-workspace/VR_CAD_App` (paths below are relative to that copy):

- Link: `Builds/QuestLink/VR_CAD_QuestLink.exe`
- Standalone: `Builds/Quest2/VR_CAD_Quest2.apk`
- Process, validation and build evidence: `Logs/QuestBaseline/`
- Per-build synchronized input hashes: `Logs/QuestBaseline/source-Link.json` and `source-Android.json`

Earlier build artifacts under the original project's `Builds/QuestLink` are from the initial run; use the isolated copy's reports and artifacts for subsequent runs.

`Install` requires one authorized Quest 2, or `-Serial <device>`, installs the baseline APK with `adb install -r` and launches it. It does not uninstall apps or clear application data. An APK upgrade preserves previous exports, so match evidence to the current session timestamp and APK hash.

## Headset acceptance (repeat every row separately for each mode)

Start with a fresh application session and note its timestamp. For Link, run the Windows executable with Link active; optionally repeat in Unity Play Mode. For standalone, launch the installed APK on the headset without using Link.

| Case | Test steps | Pass condition |
|---|---|---|
| Tracking | Wear headset; translate and rotate it. Move only the left controller, then only the right. | Stereoscopic scene is stable; headset and each controller track independently; no mouse-driven/controller coupling. |
| ControllerInput | On each controller press trigger, grip, primary/secondary buttons; move thumbsticks. Exercise movement/turn controls. | Both hands report input, intended controls respond once, and unintended actions do not occur. Unassigned buttons need only report input. |
| Grabbing | Create a box; grip, move, rotate and release it with each hand. Repeat with grid snap. Grip with both controllers. | Object follows intended hand, releases predictably and snaps on release; no accidental slicing, disappearing objects or new exceptions. Throwing is intentionally disabled in this CAD app. |
| UI | Ray-click Create, select a color, change a transform number, toggle snapping, Undo/Redo from the top bar, and export. Test wrist palette with opposite controller. | Intended control activates once and gives correct visible feedback. Panel is legible and controller rays work. |
| Export | Export an unmodified 0.2 m cube to STL and OBJ through the in-headset UI; collect files. Open them in a mesh viewer using meters and check dimensions/triangle count. | Nonempty readable exports, 12 triangles, 0.2 m bounds on all axes; no exception; evidence files correspond to this run. STL has no unit metadata: use meters, or apply a 1000x conversion if importing as millimeters. |

This baseline does not certify holes, fillets, slicing, general CAD geometry validity, performance under heavy models, hand tracking, or passthrough.

## Evidence and result recording

Editor and development builds attach a recorder automatically to the CAD hub. It records JSON lines with session/device data, XR state, tracked poses every five seconds, controller button/stick state changes, CAD status, UI button activations, grab/release messages, export paths, and runtime errors. It records observations, not automatic passes. A button event alone does not prove it came from a physical controller. The tester must confirm the behavior in the headset.

Files are stored below `Application.persistentDataPath/QuestBaseline/Quest2-Standalone` or `QuestBaseline/QuestLink-PC`. Current desktop path is `%USERPROFILE%\AppData\LocalLow\DefaultCompany\VR_CAD_App`; Android path is `/sdcard/Android/data/com.vrcad.questbaseline/files`. Exports are in `CAD_Exports` below the same root.

```powershell
.\Tools\QuestBaseline.ps1 -Action Collect -Mode Quest2-Standalone
.\Tools\QuestBaseline.ps1 -Action Collect -Mode QuestLink-PC
.\Tools\QuestBaseline.ps1 -Action RecordCase -Mode Quest2-Standalone -Case Tracking -Result Pass -Notes 'Headset and both controllers tracked independently during the recorded session.' -Evidence 'Logs\QuestBaseline\Evidence\Quest2-Standalone\<session>'
```

Collect after both export formats have been written. Missing evidence causes collection to fail, rather than silently recording success. `RecordCase` requires an existing evidence path for a pass. Record all five cases in both modes; use `Fail` for incorrect behavior and `Blocked` when prerequisites are unavailable. Review logs for exceptions and associate results with the exact build and session. Keep the resulting `acceptance.jsonl` with the build reports.

## Baseline repairs included

- Enabled Android Meta Quest Support and explicit Vulkan; retained ARM64/IL2CPP.
- Disabled the existing simulator auto-spawn setting for hardware testing.
- Added missing haptic manager initialization.
- Fixed wrist anchor discovery and controller-compatible wrist UI raycasting.
- Required explicit activation of the unfinished slicer, preventing two-hand grips from silently cutting geometry. No UI action currently enables the slicer.
- Made OBJ and ASCII STL numeric output independent of the Windows/device locale.
- Added fail-fast build/validation entry points and development-only runtime recording.

See `Quest2-Baseline-Results.md` for the actual verification status. Do not infer passing tests merely from these implementations.
