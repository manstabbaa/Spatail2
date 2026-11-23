import Anthropic from "@anthropic-ai/sdk";
import { existsSync, mkdirSync, readdirSync, readFileSync, writeFileSync } from "node:fs";
import path from "node:path";
import { loadContext, SERVICE_ROOT } from "./context.js";
import { buildWithClaude } from "./planner.js";
import { checkProgram } from "./check.js";
import { optionsFromEnv } from "./server.js";
import type { BuildRequest } from "./schema.js";

/**
 * Replays saved episodes through the planner:
 *
 *   npm run replay                 every episode in ./episodes
 *   npm run replay -- some-dir     episodes somewhere else
 *
 * An episode is a real request captured on a device or in the lab (camera frame, depth samples, surfaces, what
 * was said). Replaying them is how you find out whether a change to the instructions, the API or the model
 * helped across the places you have actually been, rather than the one you are standing in. Each program is
 * parsed and run against a stub host to catch code that cannot start; the result is written next to the episode
 * set as replay-<timestamp>.json. Every replay calls the model and costs tokens.
 */
async function main(): Promise<void> {
  const dir = path.resolve(process.argv[2] ?? process.env.SPATAIL_EPISODES_DIR ?? path.join(SERVICE_ROOT, "episodes"));
  if (!existsSync(dir)) {
    console.error(`No episodes at ${dir}. Save some from the lab ("Save episode") or a device first.`);
    process.exit(2);
  }
  const files = readdirSync(dir).filter((n) => n.endsWith(".json") && !n.startsWith("replay-")).sort();
  if (files.length === 0) {
    console.error(`No episodes in ${dir}.`);
    process.exit(2);
  }

  const options = optionsFromEnv();
  const context = loadContext();
  const client = new Anthropic();
  const rows: Array<Record<string, unknown>> = [];

  for (const file of files) {
    const episode = JSON.parse(readFileSync(path.join(dir, file), "utf8")) as { request: BuildRequest };
    const { result, usage } = await buildWithClaude(client, episode.request, context, options);
    const check = result.code ? await checkProgram(result.code, episode.request) : { ok: true, error: undefined as string | undefined, nodes: 0 };
    const status = result.error ? "PLANNER ERROR" : !result.code ? "no program" : check.ok ? "ok" : "PROGRAM ERROR";
    console.log(`${status.padEnd(14)} ${String(result.latencyMs ?? "?").padStart(6)} ms  ${file}\n               ${JSON.stringify(episode.request.prompt)} -> ${JSON.stringify(result.title)}: ${result.reply}${check.error ? `\n               ${check.error.split("\n")[0]}` : ""}`);
    rows.push({ file, prompt: episode.request.prompt, status, title: result.title, reply: result.reply, latencyMs: result.latencyMs, nodes: check.nodes, error: result.error ?? check.error, usage, code: result.code });
  }

  mkdirSync(dir, { recursive: true });
  const out = path.join(dir, `replay-${new Date().toISOString().replace(/[:.]/g, "-")}.json`);
  writeFileSync(out, JSON.stringify({ model: options.model, effort: options.effort, results: rows }, null, 2));
  const bad = rows.filter((r) => r.status !== "ok" && r.status !== "no program").length;
  console.log(`\n${rows.length} episode(s), ${bad} with errors. Details: ${out}`);
  process.exit(bad ? 1 : 0);
}

main();
