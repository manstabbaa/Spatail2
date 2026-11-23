# Spatail for Meta Quest

The mixed-reality app: passthrough, room understanding, hands and controllers, and the floating prompt bar. All of
Spatail's behaviour lives in the shared package (`packages/com.spatail.core`); this project only adapts it to the
headset.

## Requirements

- Unity 6.3 LTS (6000.3.x) with **Android Build Support** (including OpenJDK and the Android SDK & NDK tools).
  The Meta XR SDK 209 needs Unity 6000.0.66f2 or newer.
- A Meta Quest 3 or 3S in developer mode, with Space Setup completed for the room you will use.

## First open

1. Open `apps/spatail-quest` in Unity Hub. Package Manager fetches the Meta XR Core SDK and MR Utility Kit (209.0.0)
   from Meta's registry and OpenXR from Unity's.
2. If Unity asks to enable the new input system backends, or Meta asks to enable the **Meta XR feature set**,
   answer **Yes**.
3. The project configures itself (`Spatail > Setup Project` runs automatically once per editor session): it
   creates `Assets/Scenes/Spatail.unity`, sets the Android player settings, enables OpenXR with Meta's features,
   turns on passthrough / scene / hands / system keyboard in the Meta project config, and switches the build
   target to Android. Let the editor finish reloading.
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

On first launch the headset asks for access to spatial data (the room scan). If there is no room scan, Spatail offers
Space Setup, and otherwise carries on with a flat floor.

## What lives where

| File | Purpose |
| --- | --- |
| `Assets/Spatail.Quest/Runtime/QuestBootstrap.cs` | Starts the Quest platform on a headset, the simulator in the Editor. |
| `Assets/Spatail.Quest/Runtime/QuestPlatform.cs` | Builds the camera rig, passthrough, MRUK and input at runtime. |
| `Assets/Spatail.Quest/Runtime/MrukRoomProvider.cs` | Turns MR Utility Kit anchors into Spatail's room model. |
| `Assets/Spatail.Quest/Runtime/QuestPointers.cs` | Controller and hand rays. |
| `Assets/Spatail.Quest/Runtime/SystemKeyboardTextEntry.cs` | Typing through the system keyboard overlay. |
| `Assets/Spatail.Quest/Runtime/AndroidSpeechInput.cs` | Speech input through Android's recogniser (experimental; see the file). |
| `Assets/Spatail.Quest/Editor/SpatailQuestSetup.cs` | Project configuration. |
| `Assets/Spatail.Quest/Editor/SpatailQuestBuild.cs` | Menu and command-line builds. |
| `Assets/Spatail.Quest/Editor/SpatailQuestManifest.cs` | Android manifest additions (microphone, speech query, cleartext HTTP). |

## Notes

- **Planner service over HTTP.** Setup allows cleartext HTTP and turns off Meta's default network security config so
  the headset can reach a planner service on your LAN. For a store build served over HTTPS, set
  `enableNSCConfig` back to true in `SpatailQuestSetup.ApplyMetaConfig` and drop the cleartext lines.
- **Speech.** Voice uses Android's speech recogniser when the headset reports one. This has not been verified on
  Horizon OS; when it is unavailable the mic button opens the system keyboard, which has its own dictation key.
