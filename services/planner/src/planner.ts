import Anthropic from "@anthropic-ai/sdk";
import { knowledgeBlock, sensedContext, systemPrompt, type Example } from "./prompt.js";
import { BUILD_SCHEMA, LOCATE_SCHEMA, type BuildRequest, type BuildResult, type LocateRequest, type LocateResult } from "./schema.js";

export type Effort = "low" | "medium" | "high" | "xhigh" | "max";

export interface PlannerOptions {
  /** Claude model id. */
  model: string;
  /**
   * How hard the model works on each program. "medium" balances the quality of what gets built against how
   * long the user waits; lower it for snappier, simpler builds, raise it for intricate ones.
   */
  effort: Effort;
  maxTokens: number;
  /** Per-request timeout in milliseconds. Devices give up after 90 s. */
  timeoutMs: number;
}

export const DEFAULT_OPTIONS: PlannerOptions = {
  model: "claude-opus-5-5",
  effort: "medium",
  maxTokens: 16000,
  timeoutMs: 80_000,
};

/** What the planner knows beyond the request: worked examples and the organisation's knowledge. */
export interface PlannerContext {
  examples: Example[];
  knowledge: Array<{ name: string; text: string }>;
}

/** Just the part of the SDK client the planner uses, so tests can pass a stand-in. */
export interface PlannerClient {
  beta: { messages: { create: Anthropic["beta"]["messages"]["create"] } };
}

export interface Usage { input: number; cacheRead: number; cacheWrite: number; output: number }
export interface BuildOutcome { result: BuildResult; usage?: Usage }
export interface LocateOutcome { result: LocateResult; usage?: Usage }

type Failure = { reply: string; error: string };

/** Maps SDK errors to something a device can show. Anything that is not an API error is a bug and is rethrown. */
function explain(error: unknown): Failure {
  if (error instanceof Anthropic.AuthenticationError) return { reply: "The planner service is not signed in to Claude.", error: "authentication failed: check ANTHROPIC_API_KEY on the planner service" };
  if (error instanceof Anthropic.PermissionDeniedError) return { reply: "The planner service is not allowed to use that model.", error: `permission denied: ${error.message}` };
  if (error instanceof Anthropic.RateLimitError) return { reply: "The planner is busy right now. Try again in a moment.", error: "rate limited" };
  if (error instanceof Anthropic.BadRequestError) return { reply: "The planner could not process that request.", error: `bad request: ${error.message}` };
  if (error instanceof Anthropic.APIConnectionError) return { reply: "The planner could not reach Claude.", error: `connection error: ${error.message}` };
  if (error instanceof Anthropic.APIError) return { reply: "The planner hit an error.", error: `API error ${error.status}: ${error.message}` };
  throw error;
}

function usageOf(message: Anthropic.Beta.Messages.BetaMessage): Usage {
  return {
    input: message.usage.input_tokens,
    cacheRead: message.usage.cache_read_input_tokens ?? 0,
    cacheWrite: message.usage.cache_creation_input_tokens ?? 0,
    output: message.usage.output_tokens,
  };
}

function textOf(message: Anthropic.Beta.Messages.BetaMessage): string {
  return message.content
    .filter((block): block is Anthropic.Beta.Messages.BetaTextBlock => block.type === "text")
    .map((block) => block.text)
    .join("");
}

function imageBlock(base64: string): Anthropic.Beta.Messages.BetaImageBlockParam {
  return { type: "image", source: { type: "base64", media_type: "image/jpeg", data: base64 } };
}

/**
 * Has Claude look at what the device senses and write the program for one request. Always resolves: failures
 * come back as a result with `error` set and a reply the device can show.
 */
export async function buildWithClaude(client: PlannerClient, request: BuildRequest, context: PlannerContext, options: PlannerOptions = DEFAULT_OPTIONS): Promise<BuildOutcome> {
  const started = Date.now();
  const fail = (f: Failure): BuildResult => ({ reply: f.reply, title: "", id: "", code: "", remove: [], clear: false, builder: options.model, latencyMs: Date.now() - started, error: f.error });

  const content: Anthropic.Beta.Messages.BetaContentBlockParam[] = [];
  if (request.image) content.push(imageBlock(request.image));
  content.push({ type: "text", text: sensedContext(request) });

  let message: Anthropic.Beta.Messages.BetaMessage;
  try {
    message = await client.beta.messages.create(
      {
        model: options.model,
        max_tokens: options.maxTokens,
        // If the model declines for policy reasons, let the API retry on its default fallback model inside the
        // same call, so a false positive does not leave the user with nothing.
        betas: ["server-side-fallback-2026-07-01"],
        fallbacks: "default",
        output_config: { effort: options.effort, format: { type: "json_schema", schema: BUILD_SCHEMA } },
        // Two cached blocks: the instructions with the API and examples never change; knowledge changes rarely.
        system: [
          { type: "text", text: systemPrompt(context.examples), cache_control: { type: "ephemeral" } },
          { type: "text", text: knowledgeBlock(context.knowledge), cache_control: { type: "ephemeral" } },
        ],
        messages: [{ role: "user", content }],
      },
      { timeout: options.timeoutMs, maxRetries: 1 },
    );
  } catch (error) {
    return { result: fail(explain(error)) };
  }

  const usage = usageOf(message);
  if (message.stop_reason === "refusal") {
    return { result: fail({ reply: "I can't help with that one.", error: `refused (${message.stop_details?.category ?? "unspecified"})` }), usage };
  }
  if (message.stop_reason === "max_tokens") {
    return { result: fail({ reply: "That turned out too big to build in one go. Try asking for less at once.", error: "output hit max_tokens" }), usage };
  }

  let parsed: unknown;
  try {
    parsed = JSON.parse(textOf(message));
  } catch {
    return { result: fail({ reply: "The planner returned something unreadable.", error: "model output was not valid JSON" }), usage };
  }
  const result = normalizeBuild(parsed);
  if (!result) return { result: fail({ reply: "The planner returned something unreadable.", error: "model output did not match the expected shape" }), usage };

  // `message.model` is the model that actually answered, which differs from the request when a fallback ran.
  result.builder = message.model;
  result.latencyMs = Date.now() - started;
  return { result, usage };
}

/** Structural check on the model's answer. The output is schema-constrained; this guards the edges. */
export function normalizeBuild(value: unknown): BuildResult | null {
  if (!value || typeof value !== "object" || Array.isArray(value)) return null;
  const raw = value as Record<string, unknown>;
  if (typeof raw.reply !== "string") return null;
  const code = typeof raw.code === "string" ? stripFence(raw.code) : "";
  return {
    reply: raw.reply,
    title: typeof raw.title === "string" ? raw.title : "",
    id: typeof raw.id === "string" ? raw.id : "",
    code,
    remove: Array.isArray(raw.remove) ? raw.remove.filter((x): x is string => typeof x === "string") : [],
    clear: raw.clear === true,
  };
}

/** Programs occasionally arrive wrapped in a Markdown fence; the runtime wants bare code. */
function stripFence(code: string): string {
  const match = code.trim().match(/^```[a-zA-Z]*\n([\s\S]*?)\n?```$/);
  return match ? match[1] : code;
}

/**
 * Finds something in a camera frame by description. Programs call this through space.find when they need to
 * locate a thing that was not in the request's view, or to find it again after the user has moved.
 */
export async function locateWithClaude(client: PlannerClient, request: LocateRequest, options: PlannerOptions = DEFAULT_OPTIONS): Promise<LocateOutcome> {
  let message: Anthropic.Beta.Messages.BetaMessage;
  try {
    message = await client.beta.messages.create(
      {
        model: options.model,
        max_tokens: 2000,
        betas: ["server-side-fallback-2026-07-01"],
        fallbacks: "default",
        // Pointing at one thing in one image is a small task; keep it quick.
        output_config: { effort: "low", format: { type: "json_schema", schema: LOCATE_SCHEMA } },
        system: "You locate things in a camera frame from a mixed reality device. Given a description, return where it is in the image as normalised coordinates: u from 0 at the left edge to 1 at the right, v from 0 at the top to 1 at the bottom. Point at the middle of the visible part of the thing. Return several points only when there are several matching things, best match first. If it is not visible, return no points.",
        messages: [{ role: "user", content: [imageBlock(request.image), { type: "text", text: `Find: ${JSON.stringify(request.query)}` }] }],
      },
      { timeout: 30_000, maxRetries: 1 },
    );
  } catch (error) {
    return { result: { points: [], error: explain(error).error } };
  }
  const usage = usageOf(message);
  if (message.stop_reason === "refusal") return { result: { points: [], error: "refused" }, usage };
  try {
    const parsed = JSON.parse(textOf(message)) as { points?: unknown };
    const points = Array.isArray(parsed.points) ? parsed.points : [];
    const clean = points
      .filter((p): p is { u: number; v: number; label?: string } => !!p && typeof (p as any).u === "number" && typeof (p as any).v === "number")
      .map((p) => ({ u: Math.min(1, Math.max(0, p.u)), v: Math.min(1, Math.max(0, p.v)), label: typeof p.label === "string" ? p.label : request.query }));
    return { result: { points: clean }, usage };
  } catch {
    return { result: { points: [], error: "model output was not valid JSON" }, usage };
  }
}
