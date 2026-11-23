# Spatail planner service

Turns "what the user said" plus "what the device knows about the room" into spatial content, using Claude.

The headset and the iOS dev tool never hold a model API key. They send each request here; this service owns the
model, the instructions and the credentials, and answers with a plan in the same wire format the on-device
offline planner produces. If the service is unreachable or fails, the device falls back to the offline planner.

```
device  ──POST /v1/plan──▶  planner service  ──Messages API──▶  Claude
        ◀── PlanResult ───                   ◀─ structured JSON ─
```

## Run it

```bash
cd services/planner
npm install
npm run build
ANTHROPIC_API_KEY=... npm start
```

Then put this machine's LAN address in the app: **Planner URL** `http://<address>:8787` (the prompt bar's settings
on Quest come from the dev tool or `adb`; on iOS it is under Settings in the drawer).

Check it without a device:

```bash
curl http://localhost:8787/healthz
```

```bash
ANTHROPIC_API_KEY=... npm run plan -- "label every part of the pump and tell me what the gate valve does"
```

## Configuration

| Variable | Default | Meaning |
|---|---|---|
| `ANTHROPIC_API_KEY` | none | Credentials for Claude. Any credential the Anthropic SDK resolves works. |
| `PORT` | `8787` | Port to listen on. |
| `SPATAIL_TOKEN` | none | Shared secret. When set, requests need `Authorization: Bearer <token>`. |
| `SPATAIL_MODEL` | `claude-opus-5-5` | Claude model id. |
| `SPATAIL_EFFORT` | `low` | `low`, `medium`, `high`, `xhigh` or `max`. Higher is more thorough and slower. |

## How a request is planned

- `src/prompt.ts` holds the standing instructions and builds the request. The instructions and the site's
  knowledge are sent as two cached system blocks; the room, current builds and the user's words go in the user
  message, so repeat requests for the same site read most of their input from the prompt cache.
- `src/schema.ts` is the plan's JSON Schema. The response is constrained to it with structured outputs, so the
  model cannot return anything the device cannot parse.
- `src/planner.ts` makes the call (with server-side refusal fallback enabled), handles refusals, truncation and
  API errors, and reports which model actually answered.
- `src/server.ts` is the HTTP surface. Planner failures are returned as HTTP 200 with `error` set, so the device
  can show the reply and fall back; only a bug in the service is a 500.

The contract with the Unity side is `SpecJson.cs` and `RemotePlanner.cs` in `packages/com.spatail.core`. A test
checks that the element types and anchor modes listed here match the C# constants.

## Test

```bash
npm test
```

The tests run without credentials: the Claude client is replaced by a stand-in.
