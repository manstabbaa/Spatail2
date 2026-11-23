# Spatail DevTool (iOS)

The iPhone and iPad app the team uses to develop and demo Spatail without a headset, and to drive a headset
session remotely. It runs the same runtime as the Quest app (`packages/com.spatail.core`) in ARKit and wraps it in
developer tooling.

## What it does

- **AR preview.** Runs whatever the planner writes for a request, in the place the phone is in. Nothing about the
  place is loaded in advance: ARKit is read live, and each request is sent with the camera frame from that moment.
- **Prompt console.** Type, dictate with the keyboard, or tap Mic for on-device speech recognition. With the
  stand-in on, the suggestions are the requests the recorded demo can answer; with the planner connected and the
  stand-in off, they are examples of what to ask anywhere; otherwise there are none.
- **Tools drawer.**
  - *Space*: what the device senses now (tracking state, floor height, the surfaces found so far with kind and
    size, stand-in props), how ray casts are being answered, and the last camera frame that was sent.
  - *Experiences*: the programs running now, with title, prompt, node count, their latest log lines and Stop;
    programs that stopped with an error stay listed with the reason.
  - *Code*: the program behind an experience in an editable text area. Run restarts it with your changes and shows
    the error if it fails to start. Copy and Paste move programs to and from a proper editor.
  - *Log*, *Settings* (planner address and token, AI on or off, your name, the stand-in and how it is drawn, moving
    it, Clear everything, Save episode) and *Link* (join or host a shared session).
- **Remote console.** Join a headset from the Link tab and the prompts you send are built on the headset and
  appear for everyone in the session.

Press Play in the Unity editor to run the same UI over the desktop simulator; no device needed.

## How the phone senses the place

`ArSensor` is the dev tool's implementation of the shared `ISpaceSensor`. It is ready as soon as ARKit is tracking.

- **Ray casts.** ARKit's Unity provider only casts natively from a point on the screen, so a world ray is answered
  by the first of these that hits: the LiDAR mesh (iPhone and iPad Pro models with the scanner; exact for any
  ray); ARKit's own ray cast through the matching screen point when the ray starts at the camera (detected planes,
  and ARKit's estimate of the surface where no plane exists yet); the planes detected so far, tested inside their
  outlines; what ARKit answered for nearby directions when the frame was captured (approximate, for rays cast
  through an image position after the phone has moved); and finally plane bounding rectangles, furniture volumes
  and the detected floor continued beyond its mapped part. The Space tab counts which source answered.
- **Surfaces.** The planes found so far as floor, walls, doors, windows, tables, seats and ceiling. The list starts
  empty and grows as you look around. Until a floor is detected the floor height is a guess (1.35 m below where
  the phone was when tracking began) and is marked as one.
- **Camera frame.** Each request to the planner carries the camera image as a JPEG (long side 1024 px) turned
  upright for how the phone is held, with the view (pose, field of view and principal point from the camera's
  intrinsics) that maps a position in the picture to a ray in the room.

### Check the camera frame on first run

How the sensor image is turned upright could not be verified without a device. After the first request with the
planner connected, open Tools > Space:

- The thumbnail under **Last frame sent** must be upright and not mirrored, as you saw it on screen. If it is not,
  use **Turn 90°** or **Flip** below it (remembered on the device), and please report which setting was needed.
- The line under it must end in **intrinsics agree with the display**. If it says DISAGREE, positions the planner
  picks in the picture will land in the wrong place; report the whole line.

## Requirements

- Unity 6.3 LTS (6000.3.x) with **iOS Build Support**
- macOS with Xcode 15 or newer, and an Apple developer team for signing
- An iPhone or iPad with ARKit. A12 chip or newer is recommended (older chips do not classify planes, so walls
  and tables are guessed from size), and a model with a LiDAR scanner senses far more: without one, only detected
  planes and ARKit's surface estimates can be hit.
- For anything beyond the recorded demo: the Spatail planner service (`services/planner`) running on a machine the
  phone can reach

## From clone to device

1. Open `apps/spatail-devtool-ios` in Unity Hub with a 6000.3 editor. On first open the project sets itself up
   (scene, player settings, ARKit loader). If Unity or Spatail asks to enable the Input System backend, accept and
   let the editor restart. `Spatail > Setup Project` repeats the setup at any time and reports what it changed.
2. Press Play to try it in the simulator (optional).
3. `File > Build Profiles`, select iOS, **Switch Platform**.
4. `Spatail > Build iOS (Xcode project)`. The Xcode project is written to `Builds/iOS`.
5. Open `Builds/iOS/Unity-iPhone.xcodeproj`, choose your team under Signing & Capabilities for the `Unity-iPhone`
   target (or set `SPATAIL_IOS_TEAM_ID` before building and it is filled in), select your device, and Run.
6. On the phone: allow camera access. The status strip at the top shows ARKit starting, then "Tracking" with the
   number of surfaces found so far; the app is ready as soon as it is tracking.
7. Open Tools > Settings and enter the planner address (see below). Then point the phone at something and ask,
   for example "What am I looking at?". Tapping Mic asks once for microphone and speech recognition access.
8. Open Tools > Space and check the camera frame as described above.

Without a planner the app can still be tried: leave the stand-in on (the default), and the suggestions above the
prompt run the recorded demo against a virtual pump station placed on the floor in front of you.

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

- **AI planner.** Run the Spatail planner service on a machine on the same network (`services/planner`; it needs an
  Anthropic API key in its own environment, never on the phone). Enter its address, for example
  `http://192.168.1.20:8787`, and its access token if it has one, in Tools > Settings. The status strip shows
  "AI builder" when it is in use and, after each request, which builder answered and how long it took.
- **Stand-in.** A virtual pump station for demos and tests where there is no machine. Turn it off in Tools >
  Settings to work with the place as it is.
- **Episodes.** Tools > Settings > Save episode sends the last request (camera frame, depth samples, surfaces,
  what was said) and what was built for it to the planner service, which keeps it as a test case from a real
  place.
- **Headset session.** On the headset choose Share; enter the address it shows in Tools > Link and tap Join.
  iOS asks once for local network access. "Send my planner settings to the others" saves typing the address in
  the headset.

## Layout

| Path | What |
|---|---|
| `Assets/Spatail.DevTool/Runtime/DevToolBootstrap.cs` | Starts the AR platform on device, the simulator elsewhere |
| `Assets/Spatail.DevTool/Runtime/ArPlatform.cs` | Builds the AR Foundation rig in code and launches `SpatailApp` |
| `Assets/Spatail.DevTool/Runtime/ArSensor.cs` | The live space sensor: ray casts, surfaces, camera frame capture |
| `Assets/Spatail.DevTool/Runtime/ArMeshRaycaster.cs` | Ray casts into the LiDAR mesh |
| `Assets/Spatail.DevTool/Runtime/TouchPointer.cs` | Touch as a pointer; drag-to-move and two-finger twist for the stand-in |
| `Assets/Spatail.DevTool/Runtime/IosSpeechInput.cs`, `Assets/Plugins/iOS/SpatailSpeech.mm` | On-device speech recognition |
| `Assets/Spatail.DevTool/Runtime/DevToolUi*.cs` | The screen UI (IMGUI, no assets) |
| `Assets/Spatail.DevTool/Editor/` | Project setup, build entry points, Xcode post-processing |
| `Assets/link.xml` | Keeps the script engine (Jint, Acornima) and Spatail.Core from being stripped |

The scene (`Assets/Scenes/DevTool.unity`) is generated and intentionally empty: the app builds itself at runtime.
