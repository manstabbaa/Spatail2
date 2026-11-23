# Spatail Space Lab

A second host for Spatail programs, in the browser. The headset runs each generated program in Jint against
Unity; the lab runs the same program, with the same library file, against Three.js. You are the wearer: look and
walk around a space, ask for something, and the program for that request runs in front of you.

It exists for three things:

1. Showing the product in the docs: a request goes in, a program comes out, the result appears in the space.
2. Testing against spaces nobody prepared. Three of the four spaces are generated from a seed, have no labelled
   objects at all, and start with no known surfaces. Surfaces are found as you look at them, the way plane
   detection finds them on a device.
3. Recording demos without a headset.

## Files

| File | What it is |
| --- | --- |
| `spatail-lab.js` | The engine: spaces, sensing, the host contract, cameras, input. `window.SpatailLab`. |
| `lab-ui.js` | The page around it: pickers, prompt bar, running list, code editor, builders. `window.SpatailLabUI`. |
| `index.html` | Standalone page. `?space=Office&seed=7&layout=wide&planner=http://localhost:8787` are understood. |
| `examples/*.js` | Hand-written programs that use sensing only (no props). `build-bundle.js` packs them into `examples-bundle.js`. |
| `data/spatail.js` | The runtime library, exported from the package. Not edited here. |
| `data/spatail-data.js` | The film room, its recorded programs and the font metrics, exported from the package. |

Three.js r128 must be on the page as the global `THREE` before `spatail-lab.js`.

## Running it

Serve the `docs` folder over HTTP and open `lab/index.html`. It does not work from `file://`, because the page
fetches `data/spatail.js` as text (the library is evaluated once per experience, each with its own host, so it
cannot be a script tag). To skip the fetch, set `window.SPATAIL_LIBRARY` to the library source before mounting.

With the builder set to **Recorded**, the six film requests replay in the Workshop and the sensing examples
replay anywhere. With **Planner**, each request posts what the lab camera sees to `POST {planner}/v1/build` in
the same JSON shape the devices send, and runs what comes back. The planner address defaults to the page's own
origin, so the simplest setup is to let the planner service serve this folder.

## Engine API

```js
const lab = SpatailLab.create(container, {
  library,            // source text of spatail.js (or set window.SPATAIL_LIBRARY)
  data,               // defaults to window.SPATAIL_DATA
  theme,              // 'light' | 'dark'
  camera,             // 'first' (default) | 'orbit'
  space, seed         // starting space; default 'Workshop'
});

lab.run({ id, title, prompt, code, view? })   // -> { id, error?, settled: Promise<{ id, error? }> }
lab.stop(id)                                  // forgets its state
lab.stopAll()
lab.experiences()                             // [{ id, title, prompt, code, error, nodeCount, log[] }]
lab.say(text)                                 // -> true if a running program took it as a voice command
lab.setSpace(name, seed?)                     // -> { name, seed }; stops everything, forgets surfaces
lab.spaces()                                  // [{ name, seeded, about }]
lab.space()                                   // { name, seed, props }
lab.surfaces()                                // what has been discovered so far
lab.capture()                                 // { view, imageDataUrl (JPEG 960x720), depth: { columns, rows, metres[] } }
lab.lastCapture()
lab.buildRequest(prompt, repair?)             // body for POST /v1/build
lab.raycast(origin, direction, maxDistance?)  // -> { position, normal, distance, label? } | null
lab.viewPoint(view, u, v)                     // the same, through a captured view
lab.onEvent(cb)                               // -> unsubscribe; events below
lab.setFinder(async (query) => hits)          // what space.find() calls; null restores the default
lab.setTheme('light' | 'dark')
lab.setCamera('first' | 'orbit'); lab.camera()
lab.userPose(); lab.setUserPose({ position?, yaw?, pitch? })
lab.walk(forwardMetres, rightMetres); lab.look(yawDegrees, pitchDegrees)
lab.advance(seconds, step?)                   // step time by hand (tests)
lab.locate(predicate)                         // -> { x, y, id } on-screen position of the first matching node
lab.nodes(experienceId?)                      // [{ id, kind, parent, props, experience }]
lab.resize(); lab.dispose(); lab.canvas
```

Events: `{type:'surface', surface, count}`, `{type:'started'|'stopped', id, title}`, `{type:'failed', id, title, error}`,
`{type:'log', id, level, message}`, `{type:'space', name, seed}`.

Coordinates everywhere in this API are Spatail's: metres, Y up, +Z forward, left-handed, as on the devices.

`view` in `run` is the capture the request was made from; it is what `space.view.point(u, v)` casts through. Pass
`lab.capture().view` taken at the moment of the request.

## Page API

```js
SpatailLabUI.loadLibrary('data/spatail.js').then(library => {
  const ui = SpatailLabUI.mount(element, { library, compact: false, plannerUrl, space, seed });
  ui.lab            // the engine
  ui.submit(text)   // as if typed into the prompt bar
  ui.run({ id, title, prompt, code })   // run a given program in the current space
  ui.setSpace(name, seed?)              // change space as the picker does; no seed means a new layout
  ui.setBuilder('recorded' | 'planner')
  ui.dispose()
});
```

`mount` scopes its styles under `.sl` and reads the host page's colour variables (`--bg`, `--surface`, `--ink`,
`--muted`, `--line`, `--accent`, `--accent-ink`, `--accent-soft`, `--on-accent`, `--warn`, `--code-bg`), with
fallbacks. It follows `data-theme` on `<html>` and the system colour scheme. Several instances can be mounted on
one page.

## What is and is not simulated

- Surfaces: a plane becomes known after about a second with part of it in view within six metres and not hidden.
- Depth and raycasts: exact, against the generated geometry.
- The camera image: a render of the space without any program content, as a device camera would see it.
- Props: only the Workshop has them (the stand-in pump and its parts). Generated spaces have none; their objects
  have no names as far as a program can tell.
- `space.find(query)`: under Recorded it only matches stand-in props by name. Under Planner it posts the current
  frame to `/v1/locate` and casts the returned image points into the space.
- No statement budget: a program with a runaway loop freezes the tab here, where a device would stop it.
