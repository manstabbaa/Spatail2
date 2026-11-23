import { existsSync, readdirSync, readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import type { PlannerContext } from "./planner.js";
import type { Example } from "./prompt.js";

const here = path.dirname(fileURLToPath(import.meta.url));
/** services/planner, whether running from src (tests) or dist/src (built). */
export const SERVICE_ROOT = existsSync(path.join(here, "../package.json")) ? path.resolve(here, "..") : path.resolve(here, "../..");
export const REPO_ROOT = path.resolve(SERVICE_ROOT, "../..");
export const LIBRARY_PATH = path.join(REPO_ROOT, "packages/com.spatail.core/Runtime/Resources/Spatail/Script/spatail.js.txt");
const PACKAGE_EXAMPLES = path.join(REPO_ROOT, "packages/com.spatail.core/Runtime/Resources/Spatail/Script/Examples");

/** The request each example answers is its first comment line: // "Build me a walkthrough for this pump" */
function readExample(file: string): Example | null {
  const code = readFileSync(file, "utf8");
  const first = code.split("\n", 1)[0];
  const match = first.match(/^\/\/\s*"(.+)"\s*$/);
  if (!match) return null;
  return { request: match[1], code: code.split("\n").slice(1).join("\n") };
}

/**
 * Loads worked examples and knowledge once at start-up, in a fixed order so the prompt (and its cache) is stable.
 * Examples: two of the stand-in pump programs shipped with the apps, then the sensing-only ones in ./examples.
 * Knowledge: every .json or .md file in ./knowledge (or SPATAIL_KNOWLEDGE_DIR). This directory is where a real
 * connector to a maintenance system or historian would plug in.
 */
export function loadContext(env: NodeJS.ProcessEnv = process.env): PlannerContext {
  const examples: Example[] = [];
  const add = (file: string) => {
    if (!existsSync(file)) return;
    const example = readExample(file);
    if (example) examples.push(example);
  };
  const local = path.join(SERVICE_ROOT, "examples");
  if (existsSync(local)) for (const name of readdirSync(local).filter((n) => n.endsWith(".js")).sort()) add(path.join(local, name));
  for (const name of ["walkthrough.js.txt", "dashboard.js.txt"]) add(path.join(PACKAGE_EXAMPLES, name));

  const knowledge: Array<{ name: string; text: string }> = [];
  const dir = env.SPATAIL_KNOWLEDGE_DIR || path.join(SERVICE_ROOT, "knowledge");
  if (existsSync(dir)) {
    for (const name of readdirSync(dir).filter((n) => n.endsWith(".json") || n.endsWith(".md")).sort()) {
      knowledge.push({ name, text: readFileSync(path.join(dir, name), "utf8").trim() });
    }
  }
  return { examples, knowledge };
}
