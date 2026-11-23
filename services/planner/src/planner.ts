import Anthropic from "@anthropic-ai/sdk";
import { knowledgeBlock, SYSTEM_PROMPT, userMessage } from "./prompt.js";
import { PLAN_SCHEMA, type PlanOp, type PlanRequest, type PlanResult } from "./schema.js";

export type Effort = "low" | "medium" | "high" | "xhigh" | "max";

export interface PlannerOptions {
  /** Claude model id. */
  model: string;
  /**
   * How hard the model works on each plan. Planning from a known vocabulary is a small task and the user is
   * standing in a headset waiting, so the default is "low"; raise it if plans for your content come back shallow.
   */
  effort: Effort;
  maxTokens: number;
  /** Per-request timeout in milliseconds. The device gives up after 45 s. */
  timeoutMs: number;
}

export const DEFAULT_OPTIONS: PlannerOptions = {
  model: "claude-opus-5-5",
  effort: "low",
  maxTokens: 16000,
  timeoutMs: 40_000,
};

/** Just the part of the SDK client the planner uses, so tests can pass a stand-in. */
export interface PlannerClient {
  beta: { messages: { create: Anthropic["beta"]["messages"]["create"] } };
}

export interface PlanOutcome {
  plan: PlanResult;
  /** Token usage for logging; absent when the call failed before a response. */
  usage?: { input: number; cacheRead: number; cacheWrite: number; output: number };
}

/**
 * Plans one request with Claude. Always resolves: failures come back as a plan with `error` set and a reply the
 * device can show, so the device can fall back to its offline planner.
 */
export async function planWithClaude(client: PlannerClient, request: PlanRequest, options: PlannerOptions = DEFAULT_OPTIONS): Promise<PlanOutcome> {
  const started = Date.now();
  const fail = (reply: string, error: string): PlanOutcome => ({
    plan: { reply, chip: "", ops: [], planner: options.model, latencyMs: Date.now() - started, error },
  });

  let response: Anthropic.Beta.Messages.BetaMessage;
  try {
    response = await client.beta.messages.create(
      {
        model: options.model,
        max_tokens: options.maxTokens,
        // If the model declines for policy reasons, let the API retry on its default fallback model inside the
        // same call, so a false positive does not leave the user with nothing.
        betas: ["server-side-fallback-2026-07-01"],
        fallbacks: "default",
        output_config: {
          effort: options.effort,
          format: { type: "json_schema", schema: PLAN_SCHEMA },
        },
        // Two cached blocks: the instructions never change; the knowledge changes only when the site does.
        system: [
          { type: "text", text: SYSTEM_PROMPT, cache_control: { type: "ephemeral" } },
          { type: "text", text: knowledgeBlock(request), cache_control: { type: "ephemeral" } },
        ],
        messages: [{ role: "user", content: userMessage(request) }],
      },
      { timeout: options.timeoutMs, maxRetries: 1 },
    );
  } catch (error) {
    if (error instanceof Anthropic.AuthenticationError) {
      return fail("The planner service is not signed in to Claude.", "authentication failed: check ANTHROPIC_API_KEY on the planner service");
    }
    if (error instanceof Anthropic.PermissionDeniedError) {
      return fail("The planner service is not allowed to use that model.", `permission denied: ${error.message}`);
    }
    if (error instanceof Anthropic.RateLimitError) {
      return fail("The planner is busy right now. Try again in a moment.", "rate limited");
    }
    if (error instanceof Anthropic.BadRequestError) {
      return fail("The planner could not process that request.", `bad request: ${error.message}`);
    }
    if (error instanceof Anthropic.APIConnectionError) {
      return fail("The planner could not reach Claude.", `connection error: ${error.message}`);
    }
    if (error instanceof Anthropic.APIError) {
      return fail("The planner hit an error.", `API error ${error.status}: ${error.message}`);
    }
    throw error;
  }

  const usage = {
    input: response.usage.input_tokens,
    cacheRead: response.usage.cache_read_input_tokens ?? 0,
    cacheWrite: response.usage.cache_creation_input_tokens ?? 0,
    output: response.usage.output_tokens,
  };

  if (response.stop_reason === "refusal") {
    const category = response.stop_details?.category ?? "unspecified";
    return { ...fail("I can't help with that one.", `refused (${category})`), usage };
  }
  if (response.stop_reason === "max_tokens") {
    return { ...fail("That plan got too long. Try asking for less at once.", "output hit max_tokens"), usage };
  }

  const text = response.content
    .filter((block): block is Anthropic.Beta.Messages.BetaTextBlock => block.type === "text")
    .map((block) => block.text)
    .join("");

  let parsed: unknown;
  try {
    parsed = JSON.parse(text);
  } catch {
    return { ...fail("The planner returned something unreadable.", "model output was not valid JSON"), usage };
  }

  const plan = normalizePlan(parsed);
  if (!plan) return { ...fail("The planner returned something unreadable.", "model output did not match the plan shape"), usage };

  // `response.model` is the model that actually answered, which differs from the request when a fallback ran.
  plan.planner = response.model;
  plan.latencyMs = Date.now() - started;
  return { plan, usage };
}

const OPS = new Set(["add", "remove", "step", "clear"]);

/**
 * Light structural check and clean-up. The output is schema-constrained, so this mostly guards against a
 * truncated or refused response slipping through; the device does its own validation against the live room.
 */
export function normalizePlan(value: unknown): PlanResult | null {
  if (!value || typeof value !== "object" || Array.isArray(value)) return null;
  const raw = value as Record<string, unknown>;
  if (typeof raw.reply !== "string" || !Array.isArray(raw.ops)) return null;

  const ops: PlanOp[] = [];
  for (const item of raw.ops) {
    if (!item || typeof item !== "object") continue;
    const op = item as Record<string, unknown>;
    if (typeof op.op !== "string" || !OPS.has(op.op)) continue;
    if (op.op === "add") {
      const build = op.build as Record<string, unknown> | undefined;
      if (!build || typeof build !== "object" || !Array.isArray(build.elements)) continue;
    }
    if ((op.op === "remove" || op.op === "step") && typeof op.buildId !== "string") continue;
    ops.push(op as unknown as PlanOp);
  }
  return { reply: raw.reply, chip: typeof raw.chip === "string" ? raw.chip : "", ops };
}
