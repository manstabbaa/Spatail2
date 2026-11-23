# Spatail docs simulator

`spatail-sim.js` replays the output of the shipping C# planner and layout solver in the browser. It renders the
same wire format the Unity apps render (`PlanResult` + solver placements); it does not re-implement either.

```html
<script src="https://cdnjs.cloudflare.com/ajax/libs/three.js/r128/three.min.js"></script>
<script src="spatail-sim.js"></script>
<script>
  const sim = SpatailSim.create(container, { room, plans, theme: 'light' });
  sim.applyPlanAt(1);                       // plans[1].plan with plans[1].placements
</script>
```

Regenerate the data after changing the planner, the solver or the site definition:

```bash
cd tests/Spatail.Core.Tests && dotnet run -- export ../../docs/sim/data
```

## API

`SpatailSim.create(container, options)` where `options` is
`{ room, plans?, theme?: 'light'|'dark', uiScale?: number = 1.5, faceCamera?: boolean = true, touchAction?: string = 'pan-y', background?: boolean = true }`.

| Method | Notes |
| --- | --- |
| `applyPlan(plan, placements?)` | Same semantics as `BuildStore.Apply`: `add` replaces a build with the same id, `remove`, `step`, `clear`. `placements` maps `"<buildId>/<elementId>"` to `{ position, rotation, target, pivotBottom, path? }`. |
| `applyPlanAt(index)` | Applies `plans[index]`; returns that entry or `null`. |
| `reset()` | Removes every build and sends the camera home. |
| `builds()` | `[{ id, kind, title, stepCount, currentStep, elements }]` |
| `setStep(buildId, n)` | Clamped; returns `true` if the step changed. |
| `toggleChecklistItem(buildId, elementId, index)` | Returns `true` if toggled. |
| `setTwinMode('solid' \| 'ghost' \| 'hidden')` | |
| `setTheme('light' \| 'dark')` | |
| `focus(entityId \| null)` | Eases the camera to an entity, or back to the whole station. |
| `locate(buildId, elementId)` | `{ x, y }` in CSS px relative to the container, or `null` if not shown. |
| `onEvent(cb)` | `{ type: 'step', buildId, step, stepCount }`, `{ type: 'checklist', buildId, elementId, index, done }`, `{ type: 'click', buildId, elementId, elementType, action? }`. Returns an unsubscribe function. |
| `resize()` / `dispose()` | Resizing is automatic via `ResizeObserver`; `dispose` is idempotent. |
| `canvas` | The `<canvas>` element. |

Controls: drag to orbit, right-drag / shift-drag / two-finger drag to pan, pinch or wheel to zoom. The wheel only
zooms after the view has been grabbed, so the page still scrolls past the simulator.

Two deliberate differences from the headset, both switchable:

- `uiScale` draws labels, pins and panels 1.5x life size so they stay legible with the whole room on screen.
- `faceCamera` turns cards, panels, checklists and (once, when built) charts to face the camera. In the headset
  they face the user; here the camera is the viewer. Positions always come from the solver.

`dev.html` is a local harness: serve this folder and open it.
