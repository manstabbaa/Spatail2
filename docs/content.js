/* Per-app setup steps and the verification table for the Spatail Developer Guide. Edit here, not in docs.js. */
window.SPATAIL_APPS = [
  {
    id: 'quest',
    platform: 'Meta Quest 3 / 3S',
    name: 'Spatail',
    summary: 'The mixed reality app. Passthrough, the room from Space Setup, hands and controllers, and the floating prompt bar from the film. Unity project: <code>apps/spatail-quest</code> (Meta XR Core SDK and MR Utility Kit 209, OpenXR).',
    features: [
      'Reads your floor, walls and tables through MR Utility Kit. The workbench binds to a real table if you have one; the control box mounts on your nearest wall.',
      'The pump stands in as a hologram so the demo works in any room. Switch the twin to <b>Ghost</b> or <b>Occluder</b> to align it over real equipment.',
      'Prompt bar with suggestions, the "Built ..." chip, the spoken-style reply, and tools: Move pump, Twin mode, Share.',
      'Share hosts a session on the local network; the dev tool (or another headset) joins by address.'
    ],
    controls: [
      ['Point + trigger', 'Select suggestions, pins, card buttons, checklist rows.'],
      ['Hand ray + pinch', 'The same, with tracked hands.'],
      ['Mic button', 'Speech, when the headset offers a recogniser; otherwise the system keyboard, which has a dictation key.'],
      ['Prompt bar', 'Opens the system keyboard.'],
      ['Move pump', 'Point at the floor, trigger to drop. Right thumbstick turns it.']
    ],
    steps: [
      { text: 'Install <b>Unity 6.3 LTS</b> (6000.3.x) with Android Build Support, including OpenJDK and the Android SDK and NDK tools.' },
      { text: 'Open <code>apps/spatail-quest</code> in Unity Hub. Answer <b>Yes</b> if Unity asks about the input backends or Meta asks to enable its feature set.' },
      { text: 'Wait for the automatic setup: it creates the scene, applies Android and Meta settings, enables OpenXR with Meta\'s features and switches the build target. Re-run any time from <b>Spatail &gt; Setup Project</b>.' },
      { text: 'Press <b>Play</b> to try the simulator before touching a headset.' },
      { text: 'Commit what Unity generated: <code>.meta</code> files, <code>Assets/Scenes</code>, <code>Assets/XR</code>, <code>Assets/Oculus</code>, <code>Assets/Resources</code>, <code>ProjectSettings</code>.' },
      { text: 'Build with <b>Spatail &gt; Build Quest APK</b>, or from a terminal (do not add <code>-quit</code>; the build exits by itself):', cmd: 'Unity -batchmode -projectPath apps/spatail-quest -buildTarget Android -executeMethod Spatail.Quest.EditorTools.SpatailQuestBuild.BuildFromCommandLine -logFile -' },
      { text: 'Put the headset in developer mode, connect it and install:', cmd: 'adb install -r apps/spatail-quest/Builds/Spatail-Quest.apk' },
      { text: 'On the headset, allow spatial data and run Space Setup if asked. Without a room scan the app carries on with a flat floor.' }
    ],
    note: 'Not yet run in Unity or on a headset. Speech through Android\'s recogniser is untested on Horizon OS; if the headset has none, the mic button opens the keyboard.'
  },
  {
    id: 'ios',
    platform: 'iPhone / iPad',
    name: 'Spatail DevTool',
    summary: 'The same runtime in ARKit, wrapped in developer tooling, and a remote console for a headset. Unity project: <code>apps/spatail-devtool-ios</code> (AR Foundation and ARKit 6.3.5).',
    features: [
      'AR view with the pump placed on your floor from detected planes; walls and tables are picked up as ARKit classifies them.',
      'Prompt field with send and mic, suggested requests, the reply and "Built ..." chip.',
      'Drawer with six tabs: <b>Room</b> (every entity, real or stand-in), <b>Builds</b> (step, remove), <b>Spec</b> (the last plan as JSON, copyable), <b>Log</b>, <b>Settings</b> (planner URL and token, AI on or off, twin mode, move or re-place the pump), <b>Link</b>.',
      '<b>Link</b> joins a headset session by address. Requests typed on the phone are planned by the headset and appear for everyone; the phone can push its planner settings to the headset.'
    ],
    controls: [
      ['Tap', 'Select pins, card buttons, checklist rows in AR.'],
      ['Type or dictate', 'In the prompt field; the mic uses on-device speech recognition.'],
      ['Move pump', 'From Settings: drag on the floor, tap to drop, twist with two fingers to turn.'],
      ['Drawer handle', 'Open the inspector tabs.']
    ],
    steps: [
      { text: 'On a Mac, install <b>Unity 6.3 LTS</b> (6000.3.x) with iOS Build Support, and Xcode.' },
      { text: 'Open <code>apps/spatail-devtool-ios</code> in Unity Hub and let the automatic setup finish (scene, player settings, ARKit loader). Re-run from <b>Spatail &gt; Setup Project</b>.' },
      { text: 'Press <b>Play</b>: the dev tool UI runs over the simulated room, so the tooling can be worked on without a phone.' },
      { text: 'Commit what Unity generated (<code>.meta</code> files, <code>Assets/Scenes</code>, <code>Assets/XR</code>, <code>ProjectSettings</code>).' },
      { text: 'Export the Xcode project with <b>Spatail &gt; Build iOS (Xcode project)</b>, or from a terminal. Set <code>SPATAIL_IOS_TEAM_ID</code> first to pre-fill automatic signing.', cmd: 'Unity -batchmode -projectPath apps/spatail-devtool-ios -buildTarget iOS -executeMethod Spatail.DevTool.EditorTools.DevToolBuild.BuildFromCommandLine -logFile -' },
      { text: 'Open <code>apps/spatail-devtool-ios/Builds/iOS</code> in Xcode, choose your team if it is not set, and run on a device with ARKit.' },
      { text: 'Allow camera, microphone, speech recognition and local network access when asked. Move the phone until the floor is found; the pump appears in front of you.' }
    ],
    note: 'Not yet run in Unity, Xcode or on a phone. The native speech plugin is Objective-C++ written without an Apple toolchain, so expect to fix small things on its first compile.'
  }
];

window.SPATAIL_STATUS = [
  ['Core library: planner, layout solver, site binder, wire format, build store', 'Tested', '34 tests in <code>tests/Spatail.Core.Tests</code>, run with <code>dotnet</code>. They cover the six film requests, follow-up edits, placement, sanitising bad plans and the AI service\'s plan shape.'],
  ['Session link', 'Tested', 'Host and client exchange plans, prompts, presence and settings over real TCP sockets on loopback, inside the same suite.'],
  ['Planner service', 'Tested', '12 tests with a stand-in for the Claude client: request shape, schema validity, refusal and error handling, the HTTP surface. Type-checks against the official Anthropic SDK.'],
  ['Planner service calling Claude for real', 'Not run', 'No Claude credentials were available. The first live call will be yours: <code>npm run plan -- "..."</code>.'],
  ['Shared Unity runtime: rendering, interaction, prompt bar, simulator', 'Compile-checked', 'Type-checks against Unity\'s reference assemblies with <code>dotnet build tools/UnityCompileCheck</code>, for both the legacy Input Manager and the Input System (built from source). Never run in Unity.'],
  ['Shaders (surface, panel, text, flow)', 'Not compiled', 'Written by hand; there was no shader compiler to run them through. Simple unlit shaders with stereo instancing macros.'],
  ['Font atlases', 'Tested', 'Baked from Inter with <code>tools/fontgen</code>, then rendered offline using the same glyph maths as <code>SpatialText</code> to confirm spacing and orientation.'],
  ['This guide\'s simulator', 'Tested', 'Headless browser run: every exported plan replays without errors; pin, card and checklist interactions, both themes and phone width checked.'],
  ['Quest adapter (camera rig, passthrough, MR Utility Kit, pointers, keyboard)', 'Checked against SDK source', 'Every Meta and OpenXR API it calls was located in the 209 / 1.19 package sources. Compiles against Unity reference assemblies plus stubs copied from those sources, in editor and Android configurations.'],
  ['Quest editor setup and build scripts', 'Checked against SDK source', 'Compiled against an older <code>UnityEditor</code> reference plus stubs; four newer editor APIs were confirmed by finding them used in the package sources.'],
  ['iOS adapter (AR session, plane-based room, touch, dev tool UI)', 'Checked against SDK source', 'AR Foundation 6.3.5 APIs located in the package source and compile-checked the same way.'],
  ['iOS native speech plugin', 'Not compiled', 'Objective-C++ against SFSpeechRecognizer and AVAudioEngine, read carefully but never built.'],
  ['First Unity import, builds, and everything on device', 'Not run', 'Tracking, passthrough compositing, scene permissions, text legibility in the headset, performance. These need your hardware.']
];
