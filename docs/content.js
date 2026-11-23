/* Per-app setup steps, the request table and the verification table for the Spatail Developer Guide.
   Edit here, not in docs.js. */
window.SPATAIL_APPS = [
  {
    id: 'quest',
    platform: 'Meta Quest 3 / 3S',
    name: 'Spatail',
    summary: 'The mixed reality app. Passthrough, live depth, the passthrough camera, hands and controllers, and the floating prompt bar from the film. Unity project: <code>apps/spatail-quest</code> (Meta XR Core SDK and MR Utility Kit 209, OpenXR).',
    features: [
      '<b>No Space Setup.</b> The app never loads a stored room scan. It casts rays into the headset\'s live depth and grows floor, wall and table patches from the hits as you look around.',
      '<b>The planner sees what you see.</b> Each request carries a passthrough camera frame with that frame\'s lens data and pose, so the program can anchor to a pixel.',
      'Prompt bar with the reply and the "Built ..." chip. Tools: Stand-in, Move pump, Show mode, Clear, Share.',
      'The stand-in pump is an optional prop for replaying the film. Switch it off and the app starts from nothing but what it senses.'
    ],
    controls: [
      ['Point + trigger', 'Select anything a program made clickable.'],
      ['Hand ray + pinch', 'The same, with tracked hands.'],
      ['Mic button', 'Speech, when the headset offers a recogniser; otherwise the system keyboard, which has a dictation key.'],
      ['Short spoken commands', 'Words a running program registered ("next", "set", "done") are handled on the device at once, with no planner call.'],
      ['Move pump', 'Point at the floor, trigger to drop. Right thumbstick turns it.']
    ],
    steps: [
      { text: 'Install <b>Unity 6.3 LTS</b> (6000.3.x) with Android Build Support, including OpenJDK and the Android SDK and NDK tools.' },
      { text: 'Open <code>apps/spatail-quest</code> in Unity Hub. Answer <b>Yes</b> if Unity asks about the input backends or Meta asks to enable its feature set.' },
      { text: 'Wait for the automatic setup: it creates the scene, applies Android and Meta settings, enables OpenXR with Meta\'s features, turns on passthrough, camera access and the spatial data permission, and switches the build target. Re-run any time from <b>Spatail &gt; Setup Project</b>.' },
      { text: 'Press <b>Play</b> to try the desktop simulator before touching a headset. Type a request and press Enter; <kbd>F1</kbd> to <kbd>F6</kbd> send the six film requests.' },
      { text: 'Commit what Unity generated: <code>.meta</code> files, <code>Assets/Scenes</code>, <code>Assets/XR</code>, <code>Assets/Oculus</code>, <code>ProjectSettings</code>.' },
      { text: 'Build with <b>Spatail &gt; Build Quest APK</b>, or from a terminal (do not add <code>-quit</code>; the build exits by itself):', cmd: 'Unity -batchmode -projectPath apps/spatail-quest -buildTarget Android -executeMethod Spatail.Quest.EditorTools.SpatailQuestBuild.BuildFromCommandLine -logFile -' },
      { text: 'Put the headset in developer mode, connect it and install:', cmd: 'adb install -r apps/spatail-quest/Builds/Spatail-Quest.apk' },
      { text: 'On first launch allow <b>Spatial data</b> (depth rays) and <b>Headset camera</b> (the frame the planner sees). The log should reach <code>Depth sensing on</code> and <code>Camera on</code>.' },
      { text: 'Start the planner service on your network and set its address (from the dev tool over the session link, or at build time in <code>Assets/Resources/SpatailConfig.json</code>). Then walk somewhere and ask.' }
    ],
    note: 'Not yet run in Unity, on a headset or in the Meta XR Simulator. Camera frames need a Quest 3 or 3S on Horizon OS v74 or newer; on other headsets the planner is told only where you are and which way you face.'
  },
  {
    id: 'ios',
    platform: 'iPhone / iPad',
    name: 'Spatail DevTool',
    summary: 'The same runtime in ARKit, wrapped in developer tooling, and a remote console for a headset. Unity project: <code>apps/spatail-devtool-ios</code> (AR Foundation and ARKit 6.3.5).',
    features: [
      '<b>Space</b>: what the phone senses now. Tracking state, floor height, every surface found so far, how ray casts were answered, and the last camera frame that was sent to the planner.',
      '<b>Experiences</b>: the programs running now with their prompt, node count and latest log lines. Programs that failed stay listed with the error.',
      '<b>Code</b>: the program behind an experience in an editable text area. Run restarts it with your changes on the spot.',
      '<b>Settings</b> saves an episode (the request, frame and sensed context) to the planner service for replay. <b>Link</b> joins a headset so requests typed on the phone are built on the headset.'
    ],
    controls: [
      ['Tap', 'Select anything a program made clickable, in AR.'],
      ['Type or dictate', 'In the prompt field; the mic uses on-device speech recognition.'],
      ['Tools button', 'Opens the drawer: Space, Experiences, Code, Log, Settings, Link.'],
      ['Move pump', 'From Settings: drag on the floor, lift to drop, twist with two fingers to turn.']
    ],
    steps: [
      { text: 'On a Mac, install <b>Unity 6.3 LTS</b> (6000.3.x) with iOS Build Support, and Xcode 15 or newer.' },
      { text: 'Open <code>apps/spatail-devtool-ios</code> in Unity Hub and let the automatic setup finish (scene, player settings, ARKit loader). Re-run from <b>Spatail &gt; Setup Project</b>.' },
      { text: 'Press <b>Play</b>: the dev tool UI runs over the simulated room, so the tooling can be worked on without a phone.' },
      { text: 'Commit what Unity generated (<code>.meta</code> files, <code>Assets/Scenes</code>, <code>Assets/XR</code>, <code>ProjectSettings</code>).' },
      { text: 'Switch platform: <b>File &gt; Build Profiles</b>, select iOS, Switch Platform. Then export with <b>Spatail &gt; Build iOS (Xcode project)</b>.' },
      { text: 'Or from a terminal, in two passes. The first configures the project and lets Unity recompile with ARKit enabled:', cmd: 'Unity -batchmode -quit -projectPath apps/spatail-devtool-ios -buildTarget iOS -executeMethod Spatail.DevTool.Editor.DevToolBuild.SetupBatch' },
      { text: 'The second exports <code>Builds/iOS</code>. Set <code>SPATAIL_IOS_TEAM_ID</code> to pre-fill automatic signing:', cmd: 'SPATAIL_IOS_TEAM_ID=ABCDE12345 Unity -batchmode -quit -projectPath apps/spatail-devtool-ios -buildTarget iOS -executeMethod Spatail.DevTool.Editor.DevToolBuild.BuildBatch' },
      { text: 'Open <code>Builds/iOS/Unity-iPhone.xcodeproj</code> in Xcode, choose your team if it is not set, and run on a device with ARKit. Allow the camera. The app is ready as soon as the strip shows <b>Tracking</b>.' },
      { text: 'In Tools &gt; Settings enter the planner address, then point the phone at something and ask.' },
      { text: 'After the first request open Tools &gt; Space. The thumbnail under <b>Last frame sent</b> must be upright, and the line under it must end in <b>intrinsics agree with the display</b>. If not, use <b>Turn 90&deg;</b> or <b>Flip</b> and note which was needed.' }
    ],
    note: 'Not yet run in Unity, Xcode or on a phone. How the camera image is turned upright could not be confirmed from the package sources, which is why the last step exists. The native speech plugin is Objective-C++ written without an Apple toolchain.'
  }
];

/* The six requests from the film. Each is an ordinary program now; `id` names the recorded program. */
window.SPATAIL_FILM = [
  { id: 'walkthrough', prompt: 'Build me a walkthrough for this pump', does: 'Numbered pins on the parts, a step card that follows you, a turning arrow on the valve. "next" is handled on the device.' },
  { id: 'flow', prompt: 'Show me how the water flows', does: 'Animated tubes through the pipework with inlet and outlet labels.' },
  { id: 'faults', prompt: 'Has it failed before?', does: 'Pulsing highlights on the parts that faulted, with a card of last week\'s events.' },
  { id: 'dashboard', prompt: 'Put this month\'s output by the bench', does: 'A bar chart panel stood beside the bench, facing you.' },
  { id: 'checklist', prompt: 'What parts do I need?', does: 'A tickable parts list. Its ticks survive when a follow-up replaces the program.' },
  { id: 'session', prompt: 'Bring Sara in', does: 'A second person\'s marker and name in the room.' }
];

/* How each claim in this project was checked. [area, status, how] */
window.SPATAIL_STATUS = [
  ['Script runtime: sandbox, budgets, error containment, state carried across replacement', 'Tested', 'Part of the 33 tests in <code>tests/Spatail.Core.Tests</code>, run with <code>dotnet</code> against the same Jint binaries the apps ship.'],
  ['The six film programs', 'Tested', 'Each starts and runs in two room layouts in the core suite, and again in the browser lab.'],
  ['Live surface tracking, raycasting, the build pipeline with one repair round', 'Tested', 'Core suite: surfaces grow from depth hits as a simulated user looks around; a failing program goes back for repair once, then falls back.'],
  ['Session link', 'Tested', 'Host and client exchange prompts, replies, presence and settings over real TCP sockets on loopback.'],
  ['Planner service', 'Tested', '14 tests with a stand-in for the Claude client: request shape (image, cached instructions, schema), refusal and error handling, the HTTP surface. Every example program is run against the real runtime library.'],
  ['Planner service calling Claude for real', 'Not run', 'No Claude credentials were available here. The quality of generated programs in real places is the main thing still to learn, and the reason the episode replay exists.'],
  ['Browser lab', 'Tested', 'Headless browser: generated spaces, surface discovery while looking around, recorded and sensing-only programs, both themes, phone width.'],
  ['Shared Unity runtime: scene host, text, interaction, prompt bar, simulator', 'Compile-checked', 'Type-checks against Unity\'s reference assemblies with <code>dotnet build tools/UnityCompileCheck</code>, for both input backends. Never run in Unity.'],
  ['Shaders (surface, panel, text, flow)', 'Not compiled', 'Written by hand; there was no shader compiler to run them through.'],
  ['Quest adapter: live depth rays, surface tracking, passthrough camera frames, pointers, keyboard', 'Checked against SDK source', 'Every Meta and OpenXR API it calls was located in the SDK 209 package sources. Compiles against stubs copied from those sources, in editor and Android configurations.'],
  ['Quest setup, build and demo recorder scripts', 'Checked against SDK source', 'Compiled against an older <code>UnityEditor</code> reference plus stubs. A few newer editor and Recorder APIs could only be confirmed by finding them in the package sources.'],
  ['iOS adapter: ARKit ray casts, planes, LiDAR mesh, camera frames with intrinsics, dev tool UI', 'Checked against SDK source', 'AR Foundation 6.3.5 APIs located in the package sources. Compiles in four configurations against stubs copied from them. Image orientation is an assumption the Space tab lets you check.'],
  ['iOS native speech plugin and Xcode post-process step', 'Not compiled', 'Objective-C++ and editor code read carefully but never built.'],
  ['Recording in the Meta XR Simulator', 'Not run', 'The simulator is a Windows and macOS application; it cannot run here. The recorder command and the steps are in place for you.'],
  ['First Unity import, device builds, and everything on device', 'Not run', 'Depth and camera permissions, where things land relative to what was seen, legibility in the headset, performance. These need your hardware.']
];

/* The demo clip shown under Recording. Recorded from the lab by a headless browser. */
window.SPATAIL_MEDIA = {
  video: 'media/spatail-lab-demo.mp4',
  poster: 'media/spatail-lab-demo.jpg',
  caption: 'Recorded from the lab in a headless browser, just under two minutes. The six film requests run in the Workshop, then the same runtime starts in a generated plant room it has never seen and outlines each surface as it is found. Programs here are the recorded ones; no call to Claude was made.'
};
