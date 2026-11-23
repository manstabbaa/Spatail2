import Anthropic from "@anthropic-ai/sdk";
import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { planWithClaude } from "./planner.js";
import { optionsFromEnv } from "./server.js";
import type { PlanRequest } from "./schema.js";

/**
 * Try the AI planner from a terminal, against the simulated pump room:
 *
 *   npm run build && npm run plan -- "label every part of the pump"
 *
 * Uses the room exported for the docs simulator and the pump station's knowledge, so no device is needed.
 */
const here = path.dirname(fileURLToPath(import.meta.url));
const repo = path.resolve(here, "../../../..");

function load(relative: string): any {
  return JSON.parse(readFileSync(path.join(repo, relative), "utf8"));
}

async function main(): Promise<void> {
  const prompt = process.argv.slice(2).join(" ").trim();
  if (!prompt) {
    console.error("usage: npm run plan -- \"<what you would say in the headset>\"");
    process.exit(2);
  }
  const room = load("docs/sim/data/room.json");
  // The device does not send hologram geometry to the planner; strip it here too.
  for (const entity of room.entities) {
    delete entity.shapes;
    delete entity.shapeOrigin;
  }
  const site = load("packages/com.spatail.core/Runtime/Resources/Spatail/pump_station.json");
  const request: PlanRequest = {
    prompt,
    site: { id: site.id, name: site.name, equipment: site.equipment.id },
    room,
    knowledge: site.knowledge,
    builds: [],
  };
  const { plan, usage } = await planWithClaude(new Anthropic(), request, optionsFromEnv());
  console.log(JSON.stringify(plan, null, 2));
  if (usage) console.error(`tokens: in=${usage.input} cache_read=${usage.cacheRead} cache_write=${usage.cacheWrite} out=${usage.output}`);
  if (plan.error) process.exit(1);
}

main();
