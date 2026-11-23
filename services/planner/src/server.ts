import Anthropic from "@anthropic-ai/sdk";
import { createReadStream, existsSync, mkdirSync, statSync, writeFileSync } from "node:fs";
import { createServer, type IncomingMessage, type Server, type ServerResponse } from "node:http";
import path from "node:path";
import { pathToFileURL } from "node:url";
import { loadContext, REPO_ROOT, SERVICE_ROOT } from "./context.js";
import { buildWithClaude, DEFAULT_OPTIONS, locateWithClaude, type BuildOutcome, type Effort, type LocateOutcome, type PlannerOptions } from "./planner.js";
import type { BuildRequest, LocateRequest } from "./schema.js";

/**
 * The Spatail planner service.
 *
 *   POST /v1/build     what the device senses + what the user said  ->  a program to run
 *   POST /v1/locate    a camera frame + a description               ->  where that thing is in the frame
 *   POST /v1/episodes  a request and its result                     ->  saved to disk as a test case
 *   GET  /healthz
 *   GET  /lab/         the browser lab, served from docs/lab, so it can call this service directly
 *
 * Devices never hold a model API key: they talk to this service, and it talks to Claude.
 *
 * Environment:
 *   ANTHROPIC_API_KEY       credentials for Claude (or any other credential the Anthropic SDK resolves)
 *   PORT                    default 8787
 *   SPATAIL_TOKEN           optional shared secret; when set, API requests must send "Authorization: Bearer <token>"
 *   SPATAIL_MODEL           default claude-opus-5-5
 *   SPATAIL_EFFORT          low | medium | high | xhigh | max, default medium
 *   SPATAIL_KNOWLEDGE_DIR   default ./knowledge
 *   SPATAIL_EPISODES_DIR    default ./episodes
 */

const MAX_BODY_BYTES = 12 * 1024 * 1024;   // camera frames travel as base64

export interface ServerConfig {
  token?: string;
  build: (request: BuildRequest) => Promise<BuildOutcome>;
  locate: (request: LocateRequest) => Promise<LocateOutcome>;
  describe?: () => Record<string, unknown>;
  log?: (line: string) => void;
  /** Directory served under /lab/. Omit to disable. */
  labDir?: string;
  /** Where POST /v1/episodes writes. Omit to disable. */
  episodesDir?: string;
}

const TYPES: Record<string, string> = {
  ".html": "text/html; charset=utf-8", ".js": "text/javascript; charset=utf-8", ".json": "application/json; charset=utf-8",
  ".css": "text/css; charset=utf-8", ".png": "image/png", ".jpg": "image/jpeg", ".svg": "image/svg+xml", ".mp4": "video/mp4",
};

export function createPlannerServer(config: ServerConfig): Server {
  const log = config.log ?? ((line: string) => console.log(line));
  const stamp = () => new Date().toISOString();

  return createServer(async (req, res) => {
    try {
      const url = new URL(req.url ?? "/", "http://localhost");

      if (req.method === "GET" && url.pathname === "/healthz") return send(res, 200, { ok: true, ...(config.describe?.() ?? {}) });
      if (req.method === "GET" && url.pathname === "/") return redirect(res, config.labDir ? "/lab/" : "/healthz");
      if (req.method === "GET" && url.pathname.startsWith("/lab")) return serveLab(config.labDir, url.pathname, res);

      const route = url.pathname;
      if (route !== "/v1/build" && route !== "/v1/locate" && route !== "/v1/episodes") return send(res, 404, { error: "not found" });
      if (req.method !== "POST") return send(res, 405, { error: "use POST" });
      if (config.token && (req.headers.authorization ?? "") !== `Bearer ${config.token}`) return send(res, 401, { error: "missing or wrong bearer token" });

      let body: unknown;
      try {
        body = JSON.parse(await readBody(req));
      } catch (error) {
        const tooLarge = error instanceof Error && error.message === "too large";
        return send(res, tooLarge ? 413 : 400, { error: tooLarge ? "request body too large" : "request body is not valid JSON" });
      }
      if (!body || typeof body !== "object" || Array.isArray(body)) return send(res, 400, { error: "request body must be a JSON object" });
      const b = body as Record<string, unknown>;

      if (route === "/v1/build") {
        if (typeof b.prompt !== "string" || b.prompt.trim().length === 0) return send(res, 400, { error: "\"prompt\" must be a non-empty string" });
        if (b.prompt.length > 2000) return send(res, 400, { error: "\"prompt\" is too long" });
        if (b.image !== undefined && typeof b.image !== "string") return send(res, 400, { error: "\"image\" must be a base64 string" });
        const request = body as BuildRequest;
        const { result, usage } = await config.build(request);
        const tokens = usage ? ` tokens in=${usage.input} cache_read=${usage.cacheRead} cache_write=${usage.cacheWrite} out=${usage.output}` : "";
        log(`${stamp()} build ${JSON.stringify(request.prompt)}${request.repair ? " (repair)" : ""}${request.image ? "" : " (no frame)"} -> ${result.error ? `ERROR ${result.error}` : `${JSON.stringify(result.title)} ${result.code.length} chars`} ${result.latencyMs ?? "?"} ms${tokens}`);
        // Failures are still a 200 with `error` set: the device shows the reply and falls back.
        return send(res, 200, result);
      }

      if (route === "/v1/locate") {
        if (typeof b.query !== "string" || !b.query.trim() || typeof b.image !== "string" || !b.image) return send(res, 400, { error: "\"query\" and \"image\" are required" });
        const { result } = await config.locate(body as LocateRequest);
        log(`${stamp()} locate ${JSON.stringify(b.query)} -> ${result.error ? `ERROR ${result.error}` : `${result.points.length} point(s)`}`);
        return send(res, 200, result);
      }

      // /v1/episodes: keep a real request (with its frame) and what was built, to replay later as a test case.
      if (!config.episodesDir) return send(res, 404, { error: "episodes are not enabled" });
      if (!b.request || typeof b.request !== "object") return send(res, 400, { error: "\"request\" is required" });
      mkdirSync(config.episodesDir, { recursive: true });
      const name = `${stamp().replace(/[:.]/g, "-")}-${slug(String((b.request as Record<string, unknown>).prompt ?? "episode"))}.json`;
      writeFileSync(path.join(config.episodesDir, name), JSON.stringify(body, null, 2));
      log(`${stamp()} episode saved ${name}`);
      return send(res, 200, { saved: name });
    } catch (error) {
      log(`${stamp()} unhandled ${error instanceof Error ? error.stack ?? error.message : String(error)}`);
      return send(res, 500, { error: "internal error" });
    }
  });
}

function slug(text: string): string {
  return text.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "").slice(0, 40) || "episode";
}

function serveLab(labDir: string | undefined, pathname: string, res: ServerResponse): void {
  if (!labDir) return send(res, 404, { error: "the lab is not being served" });
  if (pathname === "/lab") return redirect(res, "/lab/");
  const relative = decodeURIComponent(pathname.slice("/lab/".length)) || "index.html";
  const file = path.resolve(labDir, relative);
  // Stay inside the lab directory.
  if (file !== labDir && !file.startsWith(labDir + path.sep)) return send(res, 403, { error: "forbidden" });
  if (!existsSync(file) || !statSync(file).isFile()) return send(res, 404, { error: "not found" });
  res.writeHead(200, { "Content-Type": TYPES[path.extname(file)] ?? "application/octet-stream", "Cache-Control": "no-cache" });
  createReadStream(file).pipe(res);
}

function redirect(res: ServerResponse, location: string): void {
  res.writeHead(302, { Location: location });
  res.end();
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
  const context = loadContext();
  // Resolves credentials from the environment (ANTHROPIC_API_KEY, ANTHROPIC_AUTH_TOKEN, or an `ant auth login` profile).
  const client = new Anthropic();
  const port = Number(process.env.PORT ?? 8787);
  const token = process.env.SPATAIL_TOKEN || undefined;
  const labDir = path.join(REPO_ROOT, "docs", "lab");

  const server = createPlannerServer({
    token,
    build: (request) => buildWithClaude(client, request, context, options),
    locate: (request) => locateWithClaude(client, request, options),
    describe: () => ({ model: options.model, effort: options.effort, examples: context.examples.length, knowledge: context.knowledge.map((k) => k.name) }),
    labDir: existsSync(labDir) ? labDir : undefined,
    episodesDir: process.env.SPATAIL_EPISODES_DIR || path.join(SERVICE_ROOT, "episodes"),
  });
  server.listen(port, "0.0.0.0", () => {
    console.log(`Spatail planner listening on http://0.0.0.0:${port}  (model ${options.model}, effort ${options.effort}${token ? ", token required" : ", no token"})`);
    console.log(`  ${context.examples.length} examples, knowledge: ${context.knowledge.map((k) => k.name).join(", ") || "none"}`);
    if (existsSync(labDir)) console.log(`  Browser lab: http://localhost:${port}/lab/`);
    console.log("  Point the apps' planner URL at this machine's LAN address, e.g. http://192.168.1.20:" + port);
  });
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) main();
