import Anthropic from "@anthropic-ai/sdk";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import type { AddressInfo } from "node:net";
import path from "node:path";
import { test } from "node:test";
import { fileURLToPath } from "node:url";
import { DEFAULT_OPTIONS, normalizePlan, planWithClaude, type PlannerClient } from "../src/planner.js";
import { knowledgeBlock, stableStringify, SYSTEM_PROMPT, userMessage } from "../src/prompt.js";
import { ANCHOR_MODES, ELEMENT_TYPES, PLAN_SCHEMA, type PlanRequest } from "../src/schema.js";
import { createPlannerServer, optionsFromEnv } from "../src/server.js";

const here = path.dirname(fileURLToPath(import.meta.url));
const repo = path.resolve(here, "../../../..");
const load = (relative: string) => JSON.parse(readFileSync(path.join(repo, relative), "utf8"));

function sampleRequest(prompt = "label the motor"): PlanRequest {
  const site = load("packages/com.spatail.core/Runtime/Resources/Spatail/pump_station.json");
  const room = load("docs/sim/data/room.json");
  return { prompt, site: { id: site.id, name: site.name, equipment: site.equipment.id }, room, knowledge: site.knowledge, builds: [] };
}

const goodPlan = {
  reply: "Here is the motor.",
  chip: "Labels",
  ops: [{
    op: "add",
    build: {
      id: "labels", kind: "labels", title: "Labels", stepCount: 0, currentStep: 0,
      elements: [{ id: "l1", type: "label", text: "Motor", step: 0, style: "neutral", scale: 1, anchor: { entity: "motor", at: "top", offset: { x: 0, y: 0, z: 0 } } }],
    },
  }],
};

/** A stand-in for the SDK client that records the request and returns a canned message. */
function fakeClient(respond: (params: any, options: any) => any): { client: PlannerClient; calls: Array<{ params: any; options: any }> } {
  const calls: Array<{ params: any; options: any }> = [];
  const client = {
    beta: {
      messages: {
        create: async (params: any, options: any) => {
          calls.push({ params, options });
          return respond(params, options);
        },
      },
    },
  } as unknown as PlannerClient;
  return { client, calls };
}

function message(overrides: Record<string, unknown> = {}) {
  return {
    id: "msg_1", type: "message", role: "assistant", model: "claude-opus-5-5",
    content: [{ type: "text", text: JSON.stringify(goodPlan) }],
    stop_reason: "end_turn", stop_details: null,
    usage: { input_tokens: 120, output_tokens: 80, cache_read_input_tokens: 3000, cache_creation_input_tokens: 0 },
    ...overrides,
  };
}

// ---- schema ---------------------------------------------------------------------------------

const UNSUPPORTED = ["minimum", "maximum", "multipleOf", "minLength", "maxLength", "minItems", "maxItems", "pattern", "exclusiveMinimum", "exclusiveMaximum"];

function walk(node: any, visit: (n: any, path: string) => void, at = "$"): void {
  if (!node || typeof node !== "object") return;
  visit(node, at);
  if (node.properties) for (const [k, v] of Object.entries(node.properties)) walk(v, visit, `${at}.${k}`);
  if (node.items) walk(node.items, visit, `${at}[]`);
  if (node.anyOf) node.anyOf.forEach((v: any, i: number) => walk(v, visit, `${at}|${i}`));
}

test("schema: every object is closed and requires only declared properties", () => {
  let objects = 0;
  walk(PLAN_SCHEMA, (node, at) => {
    for (const keyword of UNSUPPORTED) assert.ok(!(keyword in node), `${at} uses unsupported keyword ${keyword}`);
    if (node.type !== "object") return;
    objects++;
    assert.equal(node.additionalProperties, false, `${at} must set additionalProperties: false`);
    for (const name of node.required ?? []) assert.ok(name in node.properties, `${at} requires undeclared property ${name}`);
  });
  assert.ok(objects > 15, `expected the walk to visit the whole schema, saw ${objects} objects`);
});

test("schema: covers every element type and anchor mode the device understands", () => {
  const elements = (PLAN_SCHEMA as any).properties.ops.items.anyOf[0].properties.build.properties.elements.items.anyOf;
  const types = elements.map((e: any) => e.properties.type.const).sort();
  assert.deepEqual(types, [...ELEMENT_TYPES].sort());
  const anchorModes = elements[0].properties.anchor.properties.at.enum;
  assert.deepEqual([...anchorModes].sort(), [...ANCHOR_MODES].sort());

  // Keep in step with the C# side: these lists are the contract.
  const spec = readFileSync(path.join(repo, "packages/com.spatail.core/Runtime/Core/Spec/SpatialSpec.cs"), "utf8");
  for (const type of ELEMENT_TYPES) assert.ok(spec.includes(`"${type}"`), `C# ElementType is missing "${type}"`);
  for (const mode of ANCHOR_MODES) assert.ok(spec.includes(`"${mode}"`), `C# AnchorMode is missing "${mode}"`);
});

// ---- prompt ---------------------------------------------------------------------------------

test("prompt: stable serialization and no per-request content in the cached prefix", () => {
  assert.equal(stableStringify({ b: 1, a: { d: [3, { z: 1, y: 2 }], c: 2 } }), '{"a":{"c":2,"d":[3,{"y":2,"z":1}]},"b":1}');
  const a = sampleRequest("first question");
  const b = sampleRequest("a different question");
  assert.equal(knowledgeBlock(a), knowledgeBlock(b), "knowledge block must not depend on the prompt");
  assert.ok(!SYSTEM_PROMPT.includes("first question"));
  assert.ok(userMessage(a).includes('"first question"'));
  assert.ok(userMessage({ ...a, focus: "gate-valve" }).includes('pointing at entity "gate-valve"'));
  for (const type of ELEMENT_TYPES) assert.ok(SYSTEM_PROMPT.includes(`- ${type}:`), `system prompt does not describe ${type}`);
});

// ---- planner --------------------------------------------------------------------------------

test("planner: sends the expected request and returns the plan", async () => {
  const { client, calls } = fakeClient(() => message());
  const { plan, usage } = await planWithClaude(client, sampleRequest());

  const { params, options } = calls[0];
  assert.equal(params.model, "claude-opus-5-5");
  assert.equal(params.max_tokens, 16000);
  assert.deepEqual(params.betas, ["server-side-fallback-2026-07-01"]);
  assert.equal(params.fallbacks, "default");
  assert.equal(params.output_config.effort, "low");
  assert.equal(params.output_config.format.type, "json_schema");
  assert.equal(params.output_config.format.schema, PLAN_SCHEMA);
  assert.equal(params.system.length, 2);
  assert.deepEqual(params.system[0].cache_control, { type: "ephemeral" });
  assert.deepEqual(params.system[1].cache_control, { type: "ephemeral" });
  assert.equal(params.messages.length, 1);
  assert.equal(params.messages[0].role, "user");
  // Parameters the current models reject must not be sent.
  for (const banned of ["temperature", "top_p", "top_k", "thinking", "tool_choice"]) assert.ok(!(banned in params), `${banned} must not be sent`);
  assert.equal(options.timeout, DEFAULT_OPTIONS.timeoutMs);

  assert.equal(plan.error, undefined);
  assert.equal(plan.chip, "Labels");
  assert.equal(plan.planner, "claude-opus-5-5");
  assert.equal(plan.ops.length, 1);
  assert.deepEqual(usage, { input: 120, cacheRead: 3000, cacheWrite: 0, output: 80 });
});

test("planner: reports the model that actually answered", async () => {
  const { client } = fakeClient(() => message({ model: "claude-opus-4-8" }));
  const { plan } = await planWithClaude(client, sampleRequest());
  assert.equal(plan.planner, "claude-opus-4-8");
});

test("planner: refusal, truncation and bad output become errors the device can show", async () => {
  const refusal = fakeClient(() => message({ stop_reason: "refusal", stop_details: { type: "refusal", category: "cyber", explanation: "" }, content: [] }));
  let { plan } = await planWithClaude(refusal.client, sampleRequest());
  assert.match(plan.error ?? "", /refused \(cyber\)/);
  assert.equal(plan.ops.length, 0);
  assert.ok(plan.reply.length > 0);

  const truncated = fakeClient(() => message({ stop_reason: "max_tokens", content: [{ type: "text", text: '{"reply":"x","ops":[' }] }));
  ({ plan } = await planWithClaude(truncated.client, sampleRequest()));
  assert.match(plan.error ?? "", /max_tokens/);

  const garbage = fakeClient(() => message({ content: [{ type: "text", text: "Sure! Here you go." }] }));
  ({ plan } = await planWithClaude(garbage.client, sampleRequest()));
  assert.match(plan.error ?? "", /not valid JSON/);

  const wrongShape = fakeClient(() => message({ content: [{ type: "text", text: '{"answer":42}' }] }));
  ({ plan } = await planWithClaude(wrongShape.client, sampleRequest()));
  assert.match(plan.error ?? "", /plan shape/);
});

test("planner: API errors map to distinct, non-throwing outcomes", async () => {
  const headers = new Headers();
  const cases: Array<[Error, RegExp]> = [
    [new Anthropic.AuthenticationError(401, undefined, "bad key", headers), /authentication failed/],
    [new Anthropic.RateLimitError(429, undefined, "slow down", headers), /rate limited/],
    [new Anthropic.BadRequestError(400, undefined, "nope", headers), /bad request/],
    [new Anthropic.InternalServerError(500, undefined, "boom", headers), /API error 500/],
    [new Anthropic.APIConnectionError({ message: "offline" }), /connection error/],
  ];
  for (const [error, expected] of cases) {
    const { client } = fakeClient(() => { throw error; });
    const { plan, usage } = await planWithClaude(client, sampleRequest());
    assert.match(plan.error ?? "", expected);
    assert.equal(usage, undefined);
    assert.ok(plan.reply.length > 0);
  }
  // Anything that is not an API error is a bug and must surface.
  const { client } = fakeClient(() => { throw new TypeError("bug"); });
  await assert.rejects(planWithClaude(client, sampleRequest()), TypeError);
});

test("normalizePlan: keeps valid ops, drops malformed ones", () => {
  const plan = normalizePlan({
    reply: "ok", chip: 7,
    ops: [goodPlan.ops[0], { op: "teleport" }, { op: "remove" }, { op: "remove", buildId: "x" }, { op: "add" }, { op: "clear" }, null, { op: "step", buildId: "w", step: 2 }],
  });
  assert.ok(plan);
  assert.equal(plan.chip, "");
  assert.deepEqual(plan.ops.map((o) => o.op), ["add", "remove", "clear", "step"]);
  assert.equal(normalizePlan([]), null);
  assert.equal(normalizePlan({ reply: "x" }), null);
  assert.equal(normalizePlan("nope"), null);
});

// ---- server ---------------------------------------------------------------------------------

async function withServer(config: Parameters<typeof createPlannerServer>[0], run: (base: string) => Promise<void>): Promise<void> {
  const server = createPlannerServer({ log: () => {}, ...config });
  await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
  const { port } = server.address() as AddressInfo;
  try {
    await run(`http://127.0.0.1:${port}`);
  } finally {
    await new Promise<void>((resolve) => server.close(() => resolve()));
  }
}

const post = (base: string, body: unknown, headers: Record<string, string> = {}) =>
  fetch(`${base}/v1/plan`, { method: "POST", headers: { "Content-Type": "application/json", ...headers }, body: typeof body === "string" ? body : JSON.stringify(body) });

test("server: plans a request end to end and validates input", async () => {
  const seen: PlanRequest[] = [];
  await withServer({
    plan: async (request) => { seen.push(request); return { plan: { ...goodPlan, planner: "claude-opus-5-5", latencyMs: 5 } as any }; },
    describe: () => ({ model: "claude-opus-5-5" }),
  }, async (base) => {
    const health = await fetch(`${base}/healthz`);
    assert.deepEqual(await health.json(), { ok: true, model: "claude-opus-5-5" });

    const ok = await post(base, sampleRequest("label the motor"));
    assert.equal(ok.status, 200);
    const plan = await ok.json() as any;
    assert.equal(plan.chip, "Labels");
    assert.equal(seen[0].prompt, "label the motor");
    assert.equal(seen[0].room.entities.length, 14);

    assert.equal((await post(base, "{not json")).status, 400);
    assert.equal((await post(base, { room: { entities: [] } })).status, 400);
    assert.equal((await post(base, { prompt: "hi" })).status, 400);
    assert.equal((await post(base, { prompt: "hi", room: { entities: [] }, builds: "no" })).status, 400);
    assert.equal((await fetch(`${base}/v1/plan`)).status, 405);
    assert.equal((await fetch(`${base}/nope`)).status, 404);
  });
});

test("server: enforces the bearer token when one is configured", async () => {
  await withServer({ token: "s3cret", plan: async () => ({ plan: goodPlan as any }) }, async (base) => {
    assert.equal((await post(base, sampleRequest())).status, 401);
    assert.equal((await post(base, sampleRequest(), { Authorization: "Bearer wrong" })).status, 401);
    assert.equal((await post(base, sampleRequest(), { Authorization: "Bearer s3cret" })).status, 200);
    assert.equal((await fetch(`${base}/healthz`)).status, 200);
  });
});

test("server: a planner failure is a 200 with the error for the device, a crash is a 500", async () => {
  await withServer({ plan: async () => ({ plan: { reply: "The planner is busy right now.", chip: "", ops: [], error: "rate limited" } }) }, async (base) => {
    const response = await post(base, sampleRequest());
    assert.equal(response.status, 200);
    assert.equal((await response.json() as any).error, "rate limited");
  });
  await withServer({ plan: async () => { throw new Error("bug"); } }, async (base) => {
    assert.equal((await post(base, sampleRequest())).status, 500);
  });
});

test("server: options come from the environment", () => {
  assert.deepEqual(optionsFromEnv({}), DEFAULT_OPTIONS);
  const custom = optionsFromEnv({ SPATAIL_MODEL: "claude-sonnet-5-5", SPATAIL_EFFORT: "medium" });
  assert.equal(custom.model, "claude-sonnet-5-5");
  assert.equal(custom.effort, "medium");
  assert.throws(() => optionsFromEnv({ SPATAIL_EFFORT: "extreme" }));
});
