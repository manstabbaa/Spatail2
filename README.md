# Spatail

**What AI did to software, Spatail does to mixed reality.**

Stand next to a machine and say what you need. Spatail reads the room, plans the content with AI and builds it
in place: a walkthrough on the valve, the flow through the pipes, last week's faults on the motor, this month's
output by the bench, a parts checklist above it, a colleague brought into the room.

This repository contains:

| | |
|---|---|
| `apps/spatail-quest` | The headset app: Unity project for Meta Quest 3 / 3S (passthrough mixed reality, Meta XR SDK). |
| `apps/spatail-devtool-ios` | The dev tool: Unity project for iPhone / iPad (ARKit). The same runtime in AR, wrapped in developer tooling, and a remote console for a headset. |
| `packages/com.spatail.core` | The shared Unity package both apps are built on. |
| `services/planner` | The AI planner service: a small Node service that plans requests with Claude. |
| `docs` | The interactive developer guide, with a browser simulator that replays real planner output. |
| `tests`, `tools` | Core test suite, a Unity compile check that needs no Unity install, and the font atlas baker. |

## Try it in five minutes, without a device

1. Install **Unity 6.3 LTS** (6000.3.x) with Android Build Support (for the Quest project) or iOS Build Support
   (for the dev tool).
2. Open `apps/spatail-quest` (or `apps/spatail-devtool-ios`) in Unity. On first open the project sets itself up:
   it creates its scene and applies the platform settings. You can re-run this from **Spatail > Setup Project**.
3. Press **Play**. With no headset or phone attached, the app runs in a simulated room.
   - Hold the right mouse button to look; `W A S D` `Q E` to move while looking.
   - Click a suggestion above the prompt bar, or just start typing and press Enter.
   - `F1` to `F6` send the six requests from the film.
   - Click **Move pump**, point at the floor, and click to drop it. `Z` / `X` rotate it.

No network or API key is needed for this: the on-device planner answers the core requests.

## Put it on a headset

Open `apps/spatail-quest`, connect a Quest 3 or 3S in developer mode, and choose **Spatail > Build Quest APK**
(output: `apps/spatail-quest/Builds/Spatail-Quest.apk`), or use Unity's Build And Run. Run Space Setup on the
headset first so the app can see your floor, walls and tables. Details and controls: `apps/spatail-quest/README.md`.

## Put the dev tool on an iPhone

Open `apps/spatail-devtool-ios` on a Mac, choose **Spatail > Build iOS (Xcode project)** (output:
`apps/spatail-devtool-ios/Builds/iOS`), open the generated project in Xcode, pick your team and run on a device.
Details: `apps/spatail-devtool-ios/README.md`.

## Turn on the AI planner

```bash
cd services/planner
npm install
npm run build
ANTHROPIC_API_KEY=... npm start
```

Then point the apps at it: set **Planner URL** to `http://<that machine's LAN address>:8787` in the dev tool's
Settings, and push it to a headset from the dev tool's Link tab; or bake it into a build with
`Assets/Resources/SpatailConfig.json` (`{ "plannerUrl": "http://192.168.1.20:8787" }`). See `services/planner/README.md`.

Devices never hold a model API key. If the service is unreachable the apps fall back to the on-device planner.

## How it works

```
 say it ──▶ room model ──▶ planner ──▶ plan ──▶ layout solver ──▶ views in the room
            (device +       (Claude     (builds of      (anchors become       (and to anyone in
             registered      or on-      anchored        poses, clear of        the session)
             equipment)      device)     elements)       obstacles)
```

- **Room model.** The device contributes surfaces and furniture. The registered site
  (`packages/com.spatail.core/Runtime/Resources/Spatail/pump_station.json`) contributes the equipment, its parts
  and what is known about it. Fixtures bind to real furniture when the room has it and to stand-in holograms
  when it does not, so the demo works in any room.
- **Plan.** Planners return operations on *builds*: named groups of *elements* (label, pin, card, gizmo, flow,
  highlight, chart, checklist, avatar, panel) anchored to entities in the room. One wire format is shared by the
  on-device planner, the AI service, the session link and the docs simulator.
- **Layout.** Anchors such as `floor-near` or `callout` are resolved against the live room, so content lands
  somewhere sensible instead of being dropped in with no sense of the room.

The developer guide in `docs/` walks through all of it interactively.

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

The first runs the core suite (planner, layout, binder, wire format, session link over real sockets). The second
type-checks the Unity-facing runtime against Unity's reference assemblies, with no Unity install or licence. The
third tests the planner service with a stand-in for the Claude client. CI runs all three on every push.

After changing the planner, the layout solver or the site definition, regenerate the data the docs simulator
replays:

```bash
dotnet run --project tests/Spatail.Core.Tests -- export docs/sim/data
```

## What has and has not been run

Everything in "Tests and checks" passes. The Unity projects, however, were written without access to a Unity
editor, Xcode, a headset or Claude credentials. The platform code was checked against the actual SDK sources
(Meta XR SDK 209, AR Foundation 6.3.5) and compile-checked as far as that is possible outside Unity, but the
first real build and the first on-device run will happen on your machines. `docs/` has a table of exactly what
was verified and how.

## Licences

The Inter typeface (baked into the font atlases) is used under the SIL Open Font License; see
`packages/com.spatail.core/Runtime/Resources/Spatail/Fonts/INTER_LICENSE.txt`.
