# Spatail for Meta Quest

The mixed-reality app: passthrough, live sensing of the place you are in, hands and controllers, and the floating
prompt bar. All of Spatail's behaviour lives in the shared package (`packages/com.spatail.core`); this project only
adapts it to the headset.

Nothing about the room is prepared in advance. There is no room scan to load and Space Setup is never opened: put the
headset on anywhere, and Spatail reads the place as you look around it.

## Requirements

- Unity 6.3 LTS (6000.3.x) with **Android Build Support** (including OpenJDK and the Android SDK & NDK tools).
  The Meta XR SDK 209 needs Unity 6000.0.66f2 or newer.
- A Meta Quest 3 or 3S in developer mode, on Horizon OS v74 or newer. These are the headsets with a depth sensor
  and app access to the passthrough camera. On older headsets Spatail still starts, with a flat floor and no camera
  image (see [What each headset gets](#what-each-headset-gets)).

## First open

1. Open `apps/spatail-quest` in Unity Hub. Package Manager fetches the Meta XR Core SDK and MR Utility Kit (209.0.0)
   from Meta's registry, and OpenXR and Recorder (5.1.8) from Unity's.
2. If Unity asks to enable the new input system backends, or Meta asks to enable the **Meta XR feature set**,
   answer **Yes**.
3. The project configures itself (`Spatail > Setup Project` runs automatically once per editor session): it
   creates `Assets/Scenes/Spatail.unity`, sets the Android player settings, enables OpenXR with Meta's features,
   turns on passthrough, passthrough camera access, the Spatial data permission, hands and the system keyboard in
   the Meta project config, and switches the build target to Android. Let the editor finish reloading.
4. Commit the files Unity generated (`.meta` files, `Assets/Scenes`, `Assets/XR`, `Assets/Oculus`, `ProjectSettings`).

Press **Play** without a headset attached to run the desktop simulator: hold right mouse to look, type a prompt and
press Enter, or press F1-F6 for the six demo prompts.

## Build and install

- In the editor: `Spatail > Build Quest APK` writes `Builds/Spatail-Quest.apk`.
- From the command line (no `-quit`; the build exits the editor itself):

  ```bash
  Unity -batchmode -projectPath apps/spatail-quest -buildTarget Android \
    -executeMethod Spatail.Quest.EditorTools.SpatailQuestBuild.BuildFromCommandLine -logFile -
  ```

- Install on the headset:

  ```bash
  adb install -r apps/spatail-quest/Builds/Spatail-Quest.apk
  ```

On first launch the headset asks two things, one after the other:

- **Spatial data** — lets Spatail cast rays into the headset's live depth. Refuse it and Spatail runs on a flat floor.
- **Headset camera** — lets the planner see what you are looking at. Refuse it and the planner is only told where
  you are and which way you face.

Either can be changed later under Settings > Privacy and safety > App permissions; Spatail picks the change up
without a reinstall. Whenever what is running changes, Spatail's log (and `adb logcat -s Unity`) says so, for example
`Sensing: Depth sensing on, 3 surfaces so far. Camera on (1280x960).`

## Live sensing

`QuestSensor` is the whole of it. It implements the shared `ISpaceSensor` contract with three parts, each of which
works or falls back on its own:

| Part | How | Without it |
| --- | --- | --- |
| Rays into the world | MR Utility Kit's `EnvironmentRaycastManager`, which casts into the headset's live depth. | Rays hit the surfaces found so far, then a flat floor at the tracking origin's floor level. |
| Floors, walls, table tops | A sparse grid of depth rays (8 x 6) through your view, four times a second and at most 12 rays a frame, fed to the shared `SurfaceTracker`. Surfaces appear as you look at them and grow as you move. | No surfaces; programs still get the floor. |
| The camera frame | MR Utility Kit's `PassthroughCameraAccess` (left camera, 1280x960). Each frame is sent with the lens intrinsics and the camera's pose at that frame's timestamp, scaled to 1024 px on its long side and compressed off the main thread. | The planner gets the head pose and no image. |

Things worth knowing:

- Depth only exists where the depth sensor is looking. A ray that leaves its view falls back to the surfaces already
  tracked, so things behind you stay where they were found.
- Spatail keeps no memory of a place between sessions. Start it again and it starts from nothing.
- The camera runs at up to 15 frames a second while the app is open, so a frame is ready the moment you ask for
  something.
- If a captured image ever arrives upside down on some OS version, set `QuestSensor.FlipCapturedImage`.

### What each headset gets

| Headset | Depth rays and surfaces | Camera frame |
| --- | --- | --- |
| Quest 3, Quest 3S (Horizon OS v74+) | Yes, once Spatial data is allowed | Yes, once the headset camera is allowed |
| Quest 2, Quest Pro | Whatever the headset reports at runtime; expect a flat floor | No (the SDK only supports Quest 3 and 3S) |

The project config lists Quest 3 and 3S as target devices.

## Play mode with an XR runtime (Link or the Meta XR Simulator)

Play mode uses the desktop XR settings, not Android's, and out of the box no XR runtime is enabled there, so Play
runs Spatail's own desktop simulator. To run the real Quest platform in Play mode:

1. `Spatail > Use XR Runtime In Play Mode (Link or Meta XR Simulator)` enables OpenXR with Meta's feature set for
   the desktop target. (Undo it under Project Settings > XR Plug-in Management, desktop tab.)
2. Either connect a Quest 3/3S over Link, or install the Meta XR Simulator and switch it on with
   `Window > Meta > Meta XR Simulator > Activate` (or the simulator button in the toolbar).
3. Press Play. The Console line starting `[Spatail] This device:` says what the runtime offers.

Over Link, depth needs **Settings > Developer > Spatial Data over Meta Quest Link** in the Link app, and the camera
needs Link v85 or newer.

## Recording a demo

`Spatail > Record Demo (Play Mode)` enters Play mode, waits for Spatail to be ready, sends the six requests from the
concept film one after another (with "next" twice during the walkthrough), pauses on each result, and writes
`Recordings/spatail-<date>-<time>.mp4` (1920x1080, 30 fps) with Unity Recorder. It then leaves Play mode and shows
the file. Stop Play mode yourself at any point and whatever was recorded so far is kept.

- Keep the Game view visible while it records; that is what is captured. You can fly the camera while it runs.
- With the planner service configured and AI switched on, each request really goes to the planner, so the pauses
  include its thinking time. Otherwise the built-in demo programs are used and the whole run takes about a minute.
- On a Linux editor the file is `.webm`, because Unity's encoder has no H.264 there.
- `Recordings/` is ignored by git.

It records whichever Play mode you have:

- **Desktop simulator** (no XR runtime): a synthetic room, the stand-in pump and everything the programs build.
  This is the recording that shows the most, and it needs nothing installed.
- **Meta XR Simulator or Link**: the Quest platform runs for real, and the recording is Unity's Game view.

### What the Meta XR Simulator can and cannot show

From the SDK 209 sources (`com.meta.xr.sdk.core/Editor/MetaXRSimulator`, and MR Utility Kit):

- It is a separate desktop application (Windows, or macOS on Apple Silicon; not Linux), not part of the Unity
  package. Activating it points the OpenXR loader at the simulator's runtime through the `XR_RUNTIME_JSON` and
  `XR_SELECTED_RUNTIME_JSON` environment variables, so Play mode talks to it as if it were a headset.
- The passthrough camera path is written to work there: it needs Meta XR Simulator v85 or newer.
- Whether depth ray casting works there is decided by the simulator at runtime. The SDK asks it and the sources do
  not say what it answers. The Console tells you: `This device: depth ray casting supported` or `not supported`.

What could not be determined from the sources, and should be checked the first time you try it:

- Whether the simulator's synthetic room appears in Unity's Game view. Passthrough is composited by the XR runtime
  rather than drawn by Unity, so expect the recording to show Spatail's content on a black background while the
  simulator's own window shows it in the room. If so, capture the simulator window with a screen recorder to get
  both.
- Whether the simulator offers the Spatial data and camera streams without further setup in its own settings.

## What lives where

| File | Purpose |
| --- | --- |
| `Assets/Spatail.Quest/Runtime/QuestBootstrap.cs` | Starts the Quest platform on a headset or XR runtime, the desktop simulator otherwise. |
| `Assets/Spatail.Quest/Runtime/QuestPlatform.cs` | Builds the camera rig, passthrough, sensing and input at runtime. |
| `Assets/Spatail.Quest/Runtime/QuestSensor.cs` | Live sensing: depth rays, surfaces as you look around, camera frames. |
| `Assets/Spatail.Quest/Runtime/QuestPointers.cs` | Controller and hand rays. |
| `Assets/Spatail.Quest/Runtime/SystemKeyboardTextEntry.cs` | Typing through the system keyboard overlay. |
| `Assets/Spatail.Quest/Runtime/AndroidSpeechInput.cs` | Speech input through Android's recogniser (experimental; see the file). |
| `Assets/Spatail.Quest/Editor/SpatailQuestSetup.cs` | Project configuration. |
| `Assets/Spatail.Quest/Editor/SpatailQuestBuild.cs` | Menu and command-line builds. |
| `Assets/Spatail.Quest/Editor/SpatailQuestManifest.cs` | Android manifest additions (spatial data, headset camera, microphone, speech query, cleartext HTTP). |
| `Assets/Spatail.Quest/Editor/SpatailDemoRecorder.cs` | `Spatail > Record Demo (Play Mode)`. |

## Notes

- **Not yet run on a headset.** The sensing code was written against the SDK 209 sources and compiles against
  their signatures, but it has not been run on a device or in the Meta XR Simulator. The first run should check:
  both permission prompts appear, the log reaches `Depth sensing on` and `Camera on`, surfaces appear as you look
  around, and things placed from a camera frame land where they were seen.
- **Planner service over HTTP.** Setup allows cleartext HTTP and turns off Meta's default network security config so
  the headset can reach a planner service on your LAN. For a store build served over HTTPS, set
  `enableNSCConfig` back to true in `SpatailQuestSetup.ApplyMetaConfig` and drop the cleartext lines.
- **Speech.** Voice uses Android's speech recogniser when the headset reports one. This has not been verified on
  Horizon OS; when it is unavailable the mic button opens the system keyboard, which has its own dictation key.
