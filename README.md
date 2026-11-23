# Spatail

**What AI did to software, Spatail does to mixed reality.**

You are wearing a headset, or holding a phone, and going about your day in a real place: a plant floor, a
workshop, a street. You say what you need. Spatail looks at what the device senses at that moment, has an AI
write a small program for exactly that request in exactly that place, and runs it in the space around you.

Two things follow from that, and the whole project is built around them:

- **No menu of content types.** There is no fixed list of "walkthrough", "chart", "checklist" for the AI to pick
  from. It writes software against a small spatial API (`scene`, `space`, `user`, `voice`), so a request can
  become a marker on one bolt, a measurement between two things, a route on the floor, a procedure wrapped around
  a machine, or something nobody has asked for before.
- **No pre-understood environment.** Nothing about the place is scanned, registered or loaded ahead of time.
  Each request carries the camera frame, depth samples and whatever surfaces have been noticed so far, and the
  program keeps sensing while it runs (`space.view.point`, `space.raycast`, `space.find`).

## What is in this repository

| | |
|---|---|
| `packages/com.spatail.core` | The shared Unity package: the script runtime that executes generated programs in a sandbox, the Spatail API library (`spatail.js`), live surface tracking, rendering, and the app shell. |
| `apps/spatail-quest` | The headset app: Unity project for Meta Quest 3 / 3S (passthrough, live depth, passthrough camera). |
| `apps/spatail-devtool-ios` | The dev tool: Unity project for iPhone / iPad (ARKit). The same runtime in AR with developer tooling: sensed surfaces, running programs, a code editor, episode capture, and a remote console for a headset. |
| `services/planner` | The planner service: Claude sees the frame and writes the program. Also serves the browser lab and replays saved requests as a regression set. |
| `docs/lab` | The browser lab: the same runtime library and programs in generated spaces, with a virtual camera. The test bench for places that are not predetermined when you have no headset on. |
| `docs` | The interactive developer guide. |
| `tests`, `tools` | Core test suite, a Unity compile check that needs no Unity install, the font atlas baker. |

## Try it without a device

**In a browser, with the real planner:**

```bash
cd services/planner
npm install
npm run build
ANTHROPIC_API_KEY=... npm start
```

Open `http://localhost:8787/lab/`, pick a generated space (plant room, office, street), choose the Planner
builder and ask for something. The lab renders what the simulated headset camera sees, sends that frame to the
planner, and runs the program Claude writes. Walk around with the arrow keys and ask again from somewhere else.

**In a browser, with no API key:** open the developer guide and use the Workshop space. It replays six
programs that were written by hand for a stand-in pump (the six requests from the film), and you can edit any
program and re-run it.

**In Unity:** open `apps/spatail-quest` or `apps/spatail-devtool-ios` in Unity 6.3 LTS and press Play. With no
device attached the app runs in a simulated room. Type a request and press Enter; `F1` to `F6` send the six
film requests.

## How a request becomes an experience

```
 say it ─▶ sense ─────────────▶ write ───────────────▶ run ──────────────▶ live
           camera frame,         Claude reads the       sandboxed           the program keeps
           depth samples,        frame and writes a     JavaScript engine,  sensing, animating
           surfaces so far       program for the API    budgets, repair     and listening
```

1. **Sense.** The device captures a camera frame and its pose, samples depth along a grid of rays, and lists
   the flat surfaces it has noticed so far (often few, sometimes none).
2. **Write.** The planner service gives Claude the frame, the samples and the API reference. Claude writes a
   program. For things it can see it anchors with `space.view.point(u, v)`, which casts through that pixel into
   the sensed geometry.
3. **Run.** The device runs the program in its own JavaScript engine with nothing in scope but the Spatail API.
   Statement and node budgets stop runaway code. If a program fails to start, its error goes back to the planner
   once for repair.
4. **Live.** Programs keep running. They animate, respond to taps and short voice commands on the device, can
   look again with `space.find`, and keep their state when a follow-up request replaces them.

## Testing on places that are not predetermined

- **Generated spaces in the lab.** Seeded layouts of plant rooms, offices and streets. Surfaces start unknown
  and are discovered as the virtual user looks around, the way a headset would.
- **Episodes.** Any request, from the lab or from a device, can be saved with its frame and sensed context.
  `npm run replay` in `services/planner` re-runs every saved episode against the planner and checks that each
  program starts. That is the regression set: real requests in the real places you have walked through.
- **On device.** The dev tool shows what the sensor has found, the frame that was sent, the program that came
  back and its log, and lets you edit and re-run the program on the spot.

The developer guide has the full method.

## Tests and checks

```bash
dotnet run --project tests/Spatail.Core.Tests
```

```bash
dotnet build tools/UnityCompileCheck
```

```bash
cd services/planner && npm test
```

The first runs the core suite: the script runtime and its sandbox, the six film programs in two room layouts,
surface tracking, the build pipeline with repair, and the session link. The second type-checks the Unity
runtime against Unity's reference assemblies with no Unity install. The third tests the planner service with a
stand-in for the Claude client and runs every example program against the real runtime library.

## What has and has not been run

Everything in "Tests and checks" passes, and the browser lab has been exercised in a headless browser. The
Unity projects were written without a Unity editor, Xcode, a headset or Claude credentials: platform code was
checked against the actual SDK sources and compile-checked outside Unity, but the first editor import, the first
device build and the first live call to Claude will happen on your machines. The developer guide has a table of
exactly what was verified and how.

## Licences

Inter (baked into the font atlases) is under the SIL Open Font License. Jint (BSD-2-Clause), Acornima
(BSD-3-Clause) and System.Runtime.CompilerServices.Unsafe (MIT) are redistributed unmodified; see
`packages/com.spatail.core/Runtime/Plugins/THIRD_PARTY_NOTICES.md`.
