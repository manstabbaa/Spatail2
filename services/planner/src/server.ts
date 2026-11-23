import Anthropic from "@anthropic-ai/sdk";
import { createServer, type IncomingMessage, type Server, type ServerResponse } from "node:http";
import { pathToFileURL } from "node:url";
import { DEFAULT_OPTIONS, planWithClaude, type Effort, type PlannerOptions, type PlanOutcome } from "./planner.js";
import type { PlanRequest } from "./schema.js";

/**
 * The Spatail planner service.
 *
 *   POST /v1/plan   { prompt, room, knowledge, builds, focus? }  ->  PlanResult
 *   GET  /healthz                                                   ->  { ok, model, effort }
 *
 * Devices never hold a model API key: they talk to this service, and it talks to Claude. Run it on a machine the
 * headset can reach and put its address in the app's planner URL.
 *
 * Environment:
 *   ANTHROPIC_API_KEY   credentials for Claude (or any other credential the Anthropic SDK resolves)
 *   PORT                default 8787
 *   SPATAIL_TOKEN       optional shared secret; when set, requests must send "Authorization: Bearer <token>"
 *   SPATAIL_MODEL       default claude-opus-5-5
 *   SPATAIL_EFFORT      low | medium | high | xhigh | max, default low
 */

const MAX_BODY_BYTES = 2 * 1024 * 1024;

export type PlanFunction = (request: PlanRequest) => Promise<PlanOutcome>;

export interface ServerConfig {
  token?: string;
  plan: PlanFunction;
  describe?: () => Record<string, unknown>;
  log?: (line: string) => void;
}

export function createPlannerServer(config: ServerConfig): Server {
  const log = config.log ?? ((line: string) => console.log(line));

  return createServer(async (req, res) => {
    try {
      const url = new URL(req.url ?? "/", "http://localhost");

      if (req.method === "GET" && url.pathname === "/healthz") {
        return send(res, 200, { ok: true, ...(config.describe?.() ?? {}) });
      }
      if (url.pathname !== "/v1/plan") return send(res, 404, { error: "not found" });
      if (req.method !== "POST") return send(res, 405, { error: "use POST" });

      if (config.token) {
        const header = req.headers.authorization ?? "";
        if (header !== `Bearer ${config.token}`) return send(res, 401, { error: "missing or wrong bearer token" });
      }

      let body: unknown;
      try {
        body = JSON.parse(await readBody(req));
      } catch (error) {
        const tooLarge = error instanceof Error && error.message === "too large";
        return send(res, tooLarge ? 413 : 400, { error: tooLarge ? "request body too large" : "request body is not valid JSON" });
      }

      const problem = validate(body);
      if (problem) return send(res, 400, { error: problem });
      const request = body as PlanRequest;

      const outcome = await config.plan(request);
      const { plan, usage } = outcome;
      const tokens = usage ? ` tokens in=${usage.input} cache_read=${usage.cacheRead} cache_write=${usage.cacheWrite} out=${usage.output}` : "";
      log(`${new Date().toISOString()} plan ${JSON.stringify(request.prompt)} -> ${plan.error ? `ERROR ${plan.error}` : `${plan.ops.length} op(s), chip=${JSON.stringify(plan.chip)}`} ${plan.latencyMs ?? "?"} ms${tokens}`);

      // Failures are still a 200 with `error` set: the device shows the reply and falls back to its offline planner.
      return send(res, 200, plan);
    } catch (error) {
      log(`${new Date().toISOString()} unhandled ${error instanceof Error ? error.stack ?? error.message : String(error)}`);
      return send(res, 500, { error: "internal error" });
    }
  });
}

function validate(body: unknown): string | null {
  if (!body || typeof body !== "object" || Array.isArray(body)) return "request body must be a JSON object";
  const b = body as Record<string, unknown>;
  if (typeof b.prompt !== "string" || b.prompt.trim().length === 0) return "\"prompt\" must be a non-empty string";
  if (b.prompt.length > 2000) return "\"prompt\" is too long";
  const room = b.room as Record<string, unknown> | undefined;
  if (!room || typeof room !== "object" || !Array.isArray(room.entities)) return "\"room.entities\" must be an array";
  if (b.builds !== undefined && !Array.isArray(b.builds)) return "\"builds\" must be an array";
  return null;
}

function readBody(req: IncomingMessage): Promise<string> {
  return new Promise((resolve, reject) => {
    const chunks: Buffer[] = [];
    let size = 0;
    req.on("data", (chunk: Buffer) => {
      size += chunk.length;
      if (size > MAX_BODY_BYTES) {
        reject(new Error("too large"));
        req.destroy();
        return;
      }
      chunks.push(chunk);
    });
    req.on("end", () => resolve(Buffer.concat(chunks).toString("utf8")));
    req.on("error", reject);
  });
}

function send(res: ServerResponse, status: number, body: unknown): void {
  const text = JSON.stringify(body);
  res.writeHead(status, { "Content-Type": "application/json; charset=utf-8", "Content-Length": Buffer.byteLength(text) });
  res.end(text);
}

const EFFORTS: Effort[] = ["low", "medium", "high", "xhigh", "max"];

export function optionsFromEnv(env: NodeJS.ProcessEnv = process.env): PlannerOptions {
  const effort = (env.SPATAIL_EFFORT ?? DEFAULT_OPTIONS.effort) as Effort;
  if (!EFFORTS.includes(effort)) throw new Error(`SPATAIL_EFFORT must be one of ${EFFORTS.join(", ")}`);
  return { ...DEFAULT_OPTIONS, model: env.SPATAIL_MODEL || DEFAULT_OPTIONS.model, effort };
}

function main(): void {
  const options = optionsFromEnv();
  // Resolves credentials from the environment (ANTHROPIC_API_KEY, ANTHROPIC_AUTH_TOKEN, or an `ant auth login` profile).
  const client = new Anthropic();
  const port = Number(process.env.PORT ?? 8787);
  const token = process.env.SPATAIL_TOKEN || undefined;

  const server = createPlannerServer({
    token,
    plan: (request) => planWithClaude(client, request, options),
    describe: () => ({ model: options.model, effort: options.effort }),
  });
  server.listen(port, "0.0.0.0", () => {
    console.log(`Spatail planner listening on http://0.0.0.0:${port}  (model ${options.model}, effort ${options.effort}${token ? ", token required" : ", no token"})`);
    console.log("Point the app's planner URL at this machine's LAN address, e.g. http://192.168.1.20:" + port);
  });
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) main();
