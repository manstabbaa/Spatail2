# Spatail DevTool (iOS)

The iPhone and iPad app the team uses to develop and demo Spatail without a headset, and to drive a headset
session remotely. It runs the same runtime as the Quest app (`packages/com.spatail.core`) in ARKit and wraps it in
developer tooling.

## What it does

- **AR preview.** Finds the floor, walls and tables with ARKit, stands the pump twin in the room and renders
  everything the planner builds, exactly as the headset does.
- **Prompt console.** Type, dictate with the keyboard, or tap Mic for on-device speech recognition. Suggested
  prompts sit above the field.
- **Tools drawer.** Room (what Spatail sees), Builds (what is in the room, with step controls), Spec (the last
  plan as JSON, with Copy), Log, Settings (planner address, twin display, pump placement) and Link (join or host
  a shared session).
- **Remote console.** Join a headset from the Link tab and the prompts you send are planned on the headset and
  appear for everyone in the session.

Press Play in the Unity editor to run the same UI over the desktop simulator; no device needed.

## Requirements

- Unity 6.3 LTS (6000.3.x) with **iOS Build Support**
- macOS with Xcode 15 or newer, and an Apple developer team for signing
- An iPhone or iPad with ARKit (A12 chip or newer recommended: older chips do not classify planes, so walls and
  tables are guessed from size)

## From clone to device

1. Open `apps/spatail-devtool-ios` in Unity Hub with a 6000.3 editor. On first open the project sets itself up
   (scene, player settings, ARKit loader). If Unity or Spatail asks to enable the Input System backend, accept and
   let the editor restart. `Spatail > Setup Project` repeats the setup at any time and reports what it changed.
2. Press Play to try it in the simulator (optional).
3. `File > Build Profiles`, select iOS, **Switch Platform**.
4. `Spatail > Build iOS (Xcode project)`. The Xcode project is written to `Builds/iOS`.
5. Open `Builds/iOS/Unity-iPhone.xcodeproj`, choose your team under Signing & Capabilities for the `Unity-iPhone`
   target (or set `SPATAIL_IOS_TEAM_ID` before building and it is filled in), select your device, and Run.
6. On the phone: allow camera access, then move the phone slowly with the floor in view until the pump appears.

### Command line

```bash
# First pass configures the project and lets Unity recompile with ARKit enabled.
Unity -batchmode -quit -projectPath apps/spatail-devtool-ios -buildTarget iOS \
  -executeMethod Spatail.DevTool.Editor.DevToolBuild.SetupBatch
```

```bash
# Second pass exports Builds/iOS. Exit code is non-zero if the build fails.
SPATAIL_IOS_TEAM_ID=ABCDE12345 Unity -batchmode -quit -projectPath apps/spatail-devtool-ios -buildTarget iOS \
  -executeMethod Spatail.DevTool.Editor.DevToolBuild.BuildBatch
```

`SPATAIL_DEVELOPMENT_BUILD=1` makes a development build.

## Using the AI planner and a headset

- **AI planner.** Run the Spatail planner service on a machine on the same network, then enter its address
  (for example `http://192.168.1.20:8787`) in Tools > Settings. Without it the on-device planner answers.
- **Headset session.** On the headset choose Share; enter the address it shows in Tools > Link and tap Join.
  iOS asks once for local network access.

## Layout

| Path | What |
|---|---|
| `Assets/Spatail.DevTool/Runtime/DevToolBootstrap.cs` | Starts the AR platform on device, the simulator elsewhere |
| `Assets/Spatail.DevTool/Runtime/ArPlatform.cs` | Builds the AR Foundation rig in code and launches `SpatailApp` |
| `Assets/Spatail.DevTool/Runtime/ArRoomProvider.cs` | AR planes to floor, walls, tables and seats |
| `Assets/Spatail.DevTool/Runtime/TouchPointer.cs` | Touch as a pointer; drag-to-move and two-finger twist for the pump |
| `Assets/Spatail.DevTool/Runtime/IosSpeechInput.cs`, `Assets/Plugins/iOS/SpatailSpeech.mm` | On-device speech recognition |
| `Assets/Spatail.DevTool/Runtime/DevToolUi*.cs` | The screen UI (IMGUI, no assets) |
| `Assets/Spatail.DevTool/Editor/` | Project setup, build entry points, Xcode post-processing |

The scene (`Assets/Scenes/DevTool.unity`) is generated and intentionally empty: the app builds itself at runtime.
