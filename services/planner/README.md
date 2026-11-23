# Spatail planner service

Claude looks at what the device senses and writes the program that builds the experience.

There is no list of content types in this service and nothing about any place is stored in it. Each request
carries what the device sensed at that moment: a camera frame, a coarse grid of depth samples, whatever flat
surfaces it has noticed so far, and what is already running. The model reads that, writes a small JavaScript
program against the Spatail API, and the device runs it.

```
device ──POST /v1/build──▶ planner service ──Messages API──▶ Claude (sees the frame, writes code)
       ◀── { reply, title, id, code } ──
       ──POST /v1/locate─▶  "where is the red valve in this frame?"   (space.find, while a program runs)
```

Devices never hold a model API key. If the service is unreachable they fall back to a recorded demo.

## Run it

```bash
cd services/planner
npm install
npm run build
ANTHROPIC_API_KEY=... npm start
```

Then:

- **Browser lab** (no device needed): open `http://localhost:8787/lab/`, choose a space, pick the Planner
  builder and ask for something. The lab renders a camera frame of the simulated place, sends it here, and runs
  what comes back.
- **Apps**: set the planner URL to this machine's LAN address, `http://<address>:8787`, in the iOS dev tool's
  Settings (and push it to a headset from the Link tab), or bake it into a build with
  `Assets/Resources/SpatailConfig.json`.

## Endpoints

| | |
|---|---|
| `POST /v1/build` | `{ prompt, image?, view?, user?, floorY?, surfaces?, props?, running?, repair? }` → `{ reply, title, id, code, remove, clear, builder, latencyMs, error? }`. `image` is a base64 JPEG. `repair` carries a program that failed and its error, for one retry. |
| `POST /v1/locate` | `{ query, image }` → `{ points: [{ u, v, label }] }`, image positions from 0 to 1. |
| `POST /v1/episodes` | `{ request, result }` → saved under `episodes/` as a test case. |
| `GET /healthz` | Model, effort, loaded examples and knowledge files. |
| `GET /lab/` | The browser lab from `docs/lab`. |

Planner failures are HTTP 200 with `error` set, so a device can show the reply and fall back. Only a bug in
the service is a 500.

## Configuration

| Variable | Default | Meaning |
|---|---|---|
| `ANTHROPIC_API_KEY` | none | Credentials for Claude. Any credential the Anthropic SDK resolves works. |
| `PORT` | `8787` | Port to listen on. |
| `SPATAIL_TOKEN` | none | Shared secret. When set, API requests need `Authorization: Bearer <token>`. |
| `SPATAIL_MODEL` | `claude-opus-5-5` | Claude model id. |
| `SPATAIL_EFFORT` | `medium` | `low` to `max`. Lower is faster and simpler; higher is slower and more thorough. |
| `SPATAIL_KNOWLEDGE_DIR` | `./knowledge` | `.json` and `.md` files given to the model as what the organisation knows. |
| `SPATAIL_EPISODES_DIR` | `./episodes` | Where saved requests go. They contain camera frames; the folder is git-ignored. |

## What the model is given

- `src/prompt.ts`: the standing instructions. They explain the situation (someone moving through a real place,
  nothing prepared), what arrives with a request, how to anchor to what is seen, and how to change what is
  running.
- `src/api.ts`: the API reference, included in the instructions. A test fails if it mentions anything the
  runtime library does not define.
- `examples/` and two programs from the app package: worked examples, each checked by a test that runs it.
- `knowledge/`: demo data standing in for a maintenance system. Replace it, or point
  `SPATAIL_KNOWLEDGE_DIR` at an export from yours.

The instructions and the knowledge are two cached system blocks; the frame and the sensed context go in the
user message. The answer is constrained to a small schema with structured outputs, and server-side refusal
fallback is enabled.

## Testing against real places

```bash
ANTHROPIC_API_KEY=... npm run replay
```

Every request you save as an episode (from the lab's "Save episode", or from the dev tool) is a real moment in
a real place: the frame, the depth samples, what was said. `replay` runs all of them through the planner again,
checks that each returned program parses and starts against the real runtime library, and writes a report. Use
it whenever you change the instructions, the API or the model, to see the effect across everywhere you have
been instead of the one spot you are standing in. Each replay calls the model and costs tokens.

## Test

```bash
npm test
```

Runs without credentials: the Claude client is replaced by a stand-in. After changing the instructions or the
API reference, refresh what the developer guide shows:

```bash
npm run export-docs
```
