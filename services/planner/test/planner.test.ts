import Anthropic from "@anthropic-ai/sdk";
import assert from "node:assert/strict";
import { existsSync, mkdtempSync, readdirSync, readFileSync, writeFileSync } from "node:fs";
import type { AddressInfo } from "node:net";
import { tmpdir } from "node:os";
import path from "node:path";
import { test } from "node:test";
import { API_REFERENCE } from "../src/api.js";
import { checkProgram } from "../src/check.js";
import { LIBRARY_PATH, loadContext, REPO_ROOT, SERVICE_ROOT } from "../src/context.js";
import { buildWithClaude, DEFAULT_OPTIONS, locateWithClaude, normalizeBuild, type PlannerClient } from "../src/planner.js";
import { knowledgeBlock, sensedContext, stableStringify, systemPrompt } from "../src/prompt.js";
import { BUILD_SCHEMA, LOCATE_SCHEMA, type BuildRequest } from "../src/schema.js";
import { createPlannerServer, optionsFromEnv } from "../src/server.js";

const library = readFileSync(LIBRARY_PATH, "utf8");
const context = loadContext({});

/** The stand-in room the lab uses, as a request a device could have sent. */
function standInRequest(prompt = "what is this?"): BuildRequest {
  const text = readFileSync(path.join(REPO_ROOT, "docs/lab/data/spatail-data.js"), "utf8");
  const data = JSON.parse(text.slice(text.indexOf("{"), text.lastIndexOf("}") + 1));
  const entities = data.room.entities as Array<Record<string, any>>;
  return {
    prompt,
    image: "/9j/2Q==",
    view: { width: 1280, height: 960, depth: { columns: 8, rows: 6, metres: Array.from({ length: 48 }, (_, i) => (i % 9 === 0 ? -1 : 2 + (i % 5) * 0.31)) } },
    user: data.room.user,
    floorY: 0,
    surfaces: entities.filter((e) => !e.virtual).map((e) => ({ id: e.id, kind: e.kind, position: e.position, size: e.size, rotation: e.rotation })),
    props: entities.filter((e) => e.virtual).map((e) => ({ id: e.id, name: e.label, aliases: e.aliases, position: e.position, size: e.size, rotation: e.rotation, origin: e.origin, actionPoint: e.actionPoint })),
    running: [],
  };
}

const goodBuild = { reply: "That is the drive motor.", title: "Note", id: "note-motor", code: "kit.label('Drive motor', space.view.point(0.4, 0.6) || user.head.position);", remove: [], clear: false };

function fakeClient(respond: (params: any, options: any) => any): { client: PlannerClient; calls: Array<{ params: any; options: any }> } {
  const calls: Array<{ params: any; options: any }> = [];
  const client = { beta: { messages: { create: async (params: any, options: any) => { calls.push({ params, options }); return respond(params, options); } } } } as unknown as PlannerClient;
  return { client, calls };
}

function message(body: unknown, overrides: Record<string, unknown> = {}) {
  return {
    id: "msg_1", type: "message", role: "assistant", model: "claude-opus-5-5",
    content: [{ type: "text", text: typeof body === "string" ? body : JSON.stringify(body) }],
    stop_reason: "end_turn", stop_details: null,
    usage: { input_tokens: 900, output_tokens: 400, cache_read_input_tokens: 6000, cache_creation_input_tokens: 0 },
    ...overrides,
  };
}

// ---- the API reference and the examples must be true ------------------------------------------

test("api reference: every name it mentions exists in the runtime library", () => {
  const names = new Set<string>();
  for (const match of API_REFERENCE.matchAll(/\b(scene|space|kit|vec|quat|voice|user)\.([a-zA-Z]+)/g)) names.add(`${match[1]}.${match[2]}`);
  assert.ok(names.size > 60, `expected the reference to cover the API, found ${names.size} names`);
  const missing = [...names].filter((name) => {
    const member = name.split(".")[1];
    if (name === "space.view") return !library.includes("view: null");
    if (name === "user.head") return !library.includes("get head()");
    if (name === "scene.time") return !library.includes("get time()");
    return !new RegExp(`\\b${member}\\s*:|get ${member}\\(`).test(library);
  });
  assert.deepEqual(missing, [], "names in the reference that the library does not define");
  for (const method of ["set", "add", "remove", "worldPosition", "lookAt", "onClick", "onHover", "spin", "animate"]) {
    assert.ok(library.includes(`Node.prototype.${method} =`), `node.${method} is documented but not defined`);
  }
  for (const token of ["accent", "accentDeep", "accentSoft", "warning", "success", "ink", "muted", "surface", "layer"]) {
    assert.ok(new RegExp(`\\b${token}:`).test(library), `theme.${token} is documented but not defined`);
  }
});

test("examples: every program the model is shown, and every film program, runs", async () => {
  assert.ok(context.examples.length >= 4, `expected at least four examples, got ${context.examples.length}`);
  for (const example of context.examples) {
    const result = await checkProgram(example.code, standInRequest(example.request));
    assert.ok(result.ok, `example for ${JSON.stringify(example.request)} failed: ${result.error}`);
    assert.ok(result.nodes > 0, `example for ${JSON.stringify(example.request)} built nothing`);
  }
  const dir = path.join(REPO_ROOT, "packages/com.spatail.core/Runtime/Resources/Spatail/Script/Examples");
  for (const name of readdirSync(dir).filter((n) => n.endsWith(".js.txt"))) {
    const result = await checkProgram(readFileSync(path.join(dir, name), "utf8"), standInRequest());
    assert.ok(result.ok, `${name} failed: ${result.error}`);
  }
});

test("check: catches programs that cannot start", async () => {
  assert.match((await checkProgram("const x = ;")).error ?? "", /SyntaxError/);
  assert.match((await checkProgram("scene.hologram({});")).error ?? "", /not a function/);
  assert.match((await checkProgram("await scene.wait(0.2); space.prop('nothing').position;")).error ?? "", /null|undefined/);
  assert.match((await checkProgram("scene.onFrame(() => { missing(); });")).error ?? "", /missing/);
  const fine = await checkProgram("const hits = await space.find('a door'); scene.box({ size: [1, 1, 1] }); log(hits.length);");
  assert.ok(fine.ok && fine.nodes === 1 && fine.logs.includes("info: 0"));
});

// ---- prompt -----------------------------------------------------------------------------------

test("prompt: instructions are stable and carry the API, examples and knowledge", () => {
  const a = systemPrompt(context.examples), b = systemPrompt(context.examples);
  assert.equal(a, b);
  assert.ok(a.includes(API_REFERENCE));
  for (const example of context.examples) assert.ok(a.includes(JSON.stringify(example.request)));
  assert.ok(a.includes("no menu of content types"));
  assert.ok(!/\b20\d\d-\d\d-\d\d\b/.test(a), "no dates in the cached prefix");
  const knowledge = knowledgeBlock(context.knowledge);
  assert.ok(knowledge.includes("pump-p101.json") && knowledge.includes("Close this valve"));
  assert.ok(knowledgeBlock([]).includes("no connector"));
  assert.equal(stableStringify({ b: 1, a: [{ d: 1, c: 2 }] }), '{"a":[{"c":2,"d":1}],"b":1}');
});

test("prompt: the sensed context describes the frame, what is running and a repair", () => {
  const request = standInRequest("make it bigger");
  request.running = [{ id: "chart", title: "Dashboard", prompt: "show output", code: "scene.box({ size: [1, 1, 1] });" }];
  request.repair = { code: "nope(", error: "SyntaxError: Unexpected end of input" };
  const text = sensedContext(request);
  assert.ok(text.includes("Depth grid (8 columns by 6 rows"));
  assert.equal(text.split("\n").filter((l) => /^(\s*(-|\d+\.\d\d)\s*){8}$/.test(l)).length, 6, "six rows of eight distances");
  assert.ok(text.includes('id "chart", "Dashboard"') && text.includes("scene.box"));
  assert.ok(text.includes("Your last program for this request failed") && text.includes("Unexpected end of input"));
  assert.ok(text.trimEnd().endsWith('The user said: "make it bigger"'), "the request comes last");
  assert.ok(sensedContext({ prompt: "hi" }).includes("no camera frame"));
});

test("schema: closed objects, required fields declared", () => {
  for (const schema of [BUILD_SCHEMA, LOCATE_SCHEMA]) {
    const walk = (node: any, at: string): void => {
      if (!node || typeof node !== "object") return;
      for (const keyword of ["minimum", "maximum", "minLength", "maxLength", "minItems", "maxItems", "pattern"]) assert.ok(!(keyword in node), `${at} uses ${keyword}`);
      if (node.type === "object") {
        assert.equal(node.additionalProperties, false, `${at} must be closed`);
        for (const name of node.required ?? []) assert.ok(name in node.properties, `${at} requires undeclared ${name}`);
        for (const [k, v] of Object.entries(node.properties)) walk(v, `${at}.${k}`);
      }
      if (node.items) walk(node.items, `${at}[]`);
    };
    walk(schema, "$");
  }
});

// ---- planner ----------------------------------------------------------------------------------

test("build: sends the frame, the context and the right parameters; returns the program", async () => {
  const { client, calls } = fakeClient(() => message(goodBuild));
  const { result, usage } = await buildWithClaude(client, standInRequest(), context);
  const { params, options } = calls[0];
  assert.equal(params.model, "claude-opus-5-5");
  assert.deepEqual(params.betas, ["server-side-fallback-2026-07-01"]);
  assert.equal(params.fallbacks, "default");
  assert.equal(params.output_config.effort, "medium");
  assert.equal(params.output_config.format.schema, BUILD_SCHEMA);
  assert.equal(params.system.length, 2);
  for (const block of params.system) assert.deepEqual(block.cache_control, { type: "ephemeral" });
  const content = params.messages[0].content;
  assert.equal(content[0].type, "image");
  assert.deepEqual(content[0].source, { type: "base64", media_type: "image/jpeg", data: "/9j/2Q==" });
  assert.ok(content[1].text.includes("The user said"));
  for (const banned of ["temperature", "top_p", "top_k", "thinking", "tool_choice"]) assert.ok(!(banned in params), `${banned} must not be sent`);
  assert.equal(options.timeout, DEFAULT_OPTIONS.timeoutMs);
  assert.equal(result.error, undefined);
  assert.equal(result.builder, "claude-opus-5-5");
  assert.equal(result.code, goodBuild.code);
  assert.deepEqual(usage, { input: 900, cacheRead: 6000, cacheWrite: 0, output: 400 });

  const noFrame = fakeClient(() => message(goodBuild));
  await buildWithClaude(noFrame.client, { prompt: "hello" }, context);
  assert.equal(noFrame.calls[0].params.messages[0].content.length, 1, "no image block without a frame");
});

test("build: fenced code is unwrapped; refusals, truncation and bad output become errors", async () => {
  let { result } = await buildWithClaude(fakeClient(() => message({ ...goodBuild, code: "```js\nscene.box({ size: [1, 1, 1] });\n```" })).client, standInRequest(), context);
  assert.equal(result.code, "scene.box({ size: [1, 1, 1] });");
  ({ result } = await buildWithClaude(fakeClient(() => message("", { stop_reason: "refusal", stop_details: { type: "refusal", category: "cyber" }, content: [] })).client, standInRequest(), context));
  assert.match(result.error ?? "", /refused \(cyber\)/);
  assert.equal(result.code, "");
  ({ result } = await buildWithClaude(fakeClient(() => message('{"reply":"x","code":"scene.', { stop_reason: "max_tokens" })).client, standInRequest(), context));
  assert.match(result.error ?? "", /max_tokens/);
  ({ result } = await buildWithClaude(fakeClient(() => message("Sure! Here you go.")).client, standInRequest(), context));
  assert.match(result.error ?? "", /not valid JSON/);
  assert.equal(normalizeBuild({ answer: 42 }), null);
  assert.deepEqual(normalizeBuild({ reply: "Removed.", remove: ["a", 3], clear: "yes" }), { reply: "Removed.", title: "", id: "", code: "", remove: ["a"], clear: false });
});

test("build: API errors map to distinct outcomes; bugs surface", async () => {
  const headers = new Headers();
  const cases: Array<[Error, RegExp]> = [
    [new Anthropic.AuthenticationError(401, undefined, "bad key", headers), /authentication failed/],
    [new Anthropic.RateLimitError(429, undefined, "slow down", headers), /rate limited/],
    [new Anthropic.BadRequestError(400, undefined, "nope", headers), /bad request/],
    [new Anthropic.InternalServerError(500, undefined, "boom", headers), /API error 500/],
    [new Anthropic.APIConnectionError({ message: "offline" }), /connection error/],
  ];
  for (const [error, expected] of cases) {
    const { result } = await buildWithClaude(fakeClient(() => { throw error; }).client, standInRequest(), context);
    assert.match(result.error ?? "", expected);
    assert.ok(result.reply.length > 0);
  }
  await assert.rejects(buildWithClaude(fakeClient(() => { throw new TypeError("bug"); }).client, standInRequest(), context), TypeError);
});

test("locate: asks about one frame, clamps what comes back", async () => {
  const { client, calls } = fakeClient(() => message({ points: [{ u: 0.4, v: 1.3, label: "valve" }, { u: "x" }, { u: 0.1, v: 0.2 }] }));
  const { result } = await locateWithClaude(client, { query: "the valve", image: "/9j/2Q==" });
  assert.deepEqual(result.points, [{ u: 0.4, v: 1, label: "valve" }, { u: 0.1, v: 0.2, label: "the valve" }]);
  const params = calls[0].params;
  assert.equal(params.output_config.format.schema, LOCATE_SCHEMA);
  assert.equal(params.messages[0].content[0].type, "image");
  assert.ok(params.messages[0].content[1].text.includes('"the valve"'));
  const failed = await locateWithClaude(fakeClient(() => { throw new Anthropic.RateLimitError(429, undefined, "x", new Headers()); }).client, { query: "q", image: "x" });
  assert.deepEqual(failed.result, { points: [], error: "rate limited" });
});

// ---- server -----------------------------------------------------------------------------------

async function withServer(config: Partial<Parameters<typeof createPlannerServer>[0]>, run: (base: string) => Promise<void>): Promise<void> {
  const server = createPlannerServer({
    log: () => {},
    build: async () => ({ result: { ...goodBuild, builder: "claude-opus-5-5", latencyMs: 5 } }),
    locate: async () => ({ result: { points: [{ u: 0.5, v: 0.5, label: "door" }] } }),
    ...config,
  });
  await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
  const { port } = server.address() as AddressInfo;
  try { await run(`http://127.0.0.1:${port}`); } finally { await new Promise<void>((resolve) => server.close(() => resolve())); }
}

const post = (url: string, body: unknown, headers: Record<string, string> = {}) =>
  fetch(url, { method: "POST", headers: { "Content-Type": "application/json", ...headers }, body: typeof body === "string" ? body : JSON.stringify(body) });

test("server: build and locate end to end, with validation", async () => {
  const seen: BuildRequest[] = [];
  await withServer({ build: async (request) => { seen.push(request); return { result: { ...goodBuild } }; }, describe: () => ({ model: "claude-opus-5-5" }) }, async (base) => {
    assert.deepEqual(await (await fetch(`${base}/healthz`)).json(), { ok: true, model: "claude-opus-5-5" });
    const ok = await post(`${base}/v1/build`, standInRequest("what is this?"));
    assert.equal(ok.status, 200);
    assert.equal(((await ok.json()) as any).title, "Note");
    assert.equal(seen[0].prompt, "what is this?");
    assert.equal(seen[0].view?.depth?.metres.length, 48);
    assert.equal((await post(`${base}/v1/build`, "{nope")).status, 400);
    assert.equal((await post(`${base}/v1/build`, { image: "x" })).status, 400);
    assert.equal((await post(`${base}/v1/build`, { prompt: "x", image: 5 })).status, 400);
    assert.equal((await fetch(`${base}/v1/build`)).status, 405);
    assert.equal((await fetch(`${base}/v1/nope`)).status, 404);

    const located = await post(`${base}/v1/locate`, { query: "the door", image: "/9j/2Q==" });
    assert.deepEqual(await located.json(), { points: [{ u: 0.5, v: 0.5, label: "door" }] });
    assert.equal((await post(`${base}/v1/locate`, { query: "the door" })).status, 400);
  });
});

test("server: token, planner failure and crashes", async () => {
  await withServer({ token: "s3cret" }, async (base) => {
    assert.equal((await post(`${base}/v1/build`, standInRequest())).status, 401);
    assert.equal((await post(`${base}/v1/locate`, { query: "q", image: "i" }, { Authorization: "Bearer wrong" })).status, 401);
    assert.equal((await post(`${base}/v1/build`, standInRequest(), { Authorization: "Bearer s3cret" })).status, 200);
    assert.equal((await fetch(`${base}/healthz`)).status, 200);
  });
  await withServer({ build: async () => ({ result: { reply: "The planner is busy right now.", title: "", id: "", code: "", remove: [], clear: false, error: "rate limited" } }) }, async (base) => {
    const response = await post(`${base}/v1/build`, standInRequest());
    assert.equal(response.status, 200);
    assert.equal(((await response.json()) as any).error, "rate limited");
  });
  await withServer({ build: async () => { throw new Error("bug"); } }, async (base) => {
    assert.equal((await post(`${base}/v1/build`, standInRequest())).status, 500);
  });
});

test("server: saves episodes and serves the lab from one directory only", async () => {
  const episodes = mkdtempSync(path.join(tmpdir(), "spatail-episodes-"));
  const lab = mkdtempSync(path.join(tmpdir(), "spatail-lab-"));
  writeFileSync(path.join(lab, "index.html"), "<p>lab</p>");
  writeFileSync(path.join(path.dirname(lab), "secret.txt"), "no");
  await withServer({ episodesDir: episodes, labDir: lab }, async (base) => {
    const saved = await post(`${base}/v1/episodes`, { request: standInRequest("How far is the door?"), result: goodBuild });
    assert.equal(saved.status, 200);
    const name = ((await saved.json()) as any).saved as string;
    assert.match(name, /how-far-is-the-door\.json$/);
    assert.equal(JSON.parse(readFileSync(path.join(episodes, name), "utf8")).request.prompt, "How far is the door?");
    assert.equal((await post(`${base}/v1/episodes`, { result: goodBuild })).status, 400);

    const page = await fetch(`${base}/lab/`);
    assert.equal(page.status, 200);
    assert.equal(await page.text(), "<p>lab</p>");
    assert.equal((await fetch(`${base}/lab/missing.js`)).status, 404);
    assert.notEqual((await fetch(`${base}/lab/..%2Fsecret.txt`)).status, 200);
    assert.equal((await fetch(base, { redirect: "manual" })).status, 302);
  });
  await withServer({}, async (base) => {
    assert.equal((await post(`${base}/v1/episodes`, { request: standInRequest() })).status, 404);
    assert.equal((await fetch(`${base}/lab/`)).status, 404);
  });
});

test("server: options and context come from the environment and the repo", () => {
  assert.deepEqual(optionsFromEnv({}), DEFAULT_OPTIONS);
  assert.equal(optionsFromEnv({ SPATAIL_MODEL: "claude-sonnet-5-5", SPATAIL_EFFORT: "low" }).model, "claude-sonnet-5-5");
  assert.throws(() => optionsFromEnv({ SPATAIL_EFFORT: "extreme" }));
  assert.ok(existsSync(path.join(SERVICE_ROOT, "package.json")));
  assert.ok(context.examples.some((e) => e.request.includes("walkthrough")) && context.examples.some((e) => e.request.includes("exclusion zone")));
  assert.equal(loadContext({ SPATAIL_KNOWLEDGE_DIR: path.join(tmpdir(), "nothing-here") }).knowledge.length, 0);
});
