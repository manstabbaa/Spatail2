import { readFileSync } from "node:fs";
import vm from "node:vm";
import { LIBRARY_PATH } from "./context.js";
import type { BuildRequest } from "./schema.js";

export interface CheckResult {
  ok: boolean;
  /** The first error the program raised while starting or during its first seconds. */
  error?: string;
  /** How many nodes it had created by the end. */
  nodes: number;
  logs: string[];
}

let library: string | null = null;

/**
 * Runs a generated program against the real runtime library and a stand-in host, for a couple of simulated
 * seconds. This cannot tell whether what was built is any good, but it does catch programs that do not parse,
 * call things that do not exist, or throw on start-up, before they reach a device.
 *
 * The stand-in world is crude on purpose: a floor, a wall three metres ahead, and whatever surfaces and props the
 * request listed.
 */
export async function checkProgram(code: string, request: Partial<BuildRequest> = {}, seconds = 2): Promise<CheckResult> {
  library ??= readFileSync(LIBRARY_PATH, "utf8");
  const logs: string[] = [];
  let failure: string | undefined;
  let nextId = 1;
  const alive = new Set<number>();
  const floorY = request.floorY ?? 0;
  const head = request.user?.position ?? [0, 1.6, 0];
  const forward = request.user?.forward ?? [0, 0, 1];
  const pendingFinds: number[] = [];

  const withPose = (list: Array<Record<string, unknown>> | undefined) =>
    JSON.stringify((list ?? []).map((e) => ({ rotation: [0, 0, 0, 1], size: [0.3, 0.3, 0.3], origin: e.position ?? [0, 0, 0], ...e })));

  const host = {
    create: (_kind: string, _json: string) => { const id = nextId++; alive.add(id); return id; },
    update: () => undefined,
    xf: () => undefined,
    destroy: (id: number) => { alive.delete(id); },
    measure: (text: string, size: number, _weight: string, maxWidth: number) => {
      const width = String(text).length * size * 0.55;
      const lines = maxWidth > 0 ? Math.max(1, Math.ceil(width / maxWidth)) : 1;
      return [maxWidth > 0 ? Math.min(width, maxWidth) : width, (lines - 1) * size * 1.3 + size * 0.73];
    },
    raycast: (ox: number, oy: number, oz: number, dx: number, dy: number, dz: number, max: number) => {
      // Floor below, or a wall 3 m along the user's forward direction.
      if (dy < -0.2) {
        const t = (oy - floorY) / -dy;
        if (t > 0 && t <= max) return JSON.stringify({ position: [ox + dx * t, floorY, oz + dz * t], normal: [0, 1, 0], distance: t });
      }
      const along = dx * forward[0] + dz * forward[2];
      if (along > 0.2) {
        const gap = 3 - ((ox - head[0]) * forward[0] + (oz - head[2]) * forward[2]);
        const t = gap / along;
        if (t > 0 && t <= max) return JSON.stringify({ position: [ox + dx * t, oy + dy * t, oz + dz * t], normal: [-forward[0], 0, -forward[2]], distance: t });
      }
      return null;
    },
    hasView: () => true,
    viewRay: (u: number, v: number) => JSON.stringify({ origin: head, direction: normalize([forward[0] + (u - 0.5) * forward[2], -(v - 0.5), forward[2] - (u - 0.5) * forward[0]]) }),
    floorY: () => floorY,
    surfaces: () => withPose(request.surfaces),
    props: () => withPose(request.props),
    user: () => [...head, ...forward, 0, 0, 0, 1],
    find: (_query: string, id: number) => { pendingFinds.push(id); },
    log: (level: string, message: string) => {
      logs.push(`${level}: ${message}`);
      if (level === "error" && !failure) failure = message;
    },
    fail: (message: string) => { failure ??= message; },
    finish: () => undefined,
  };

  const context = vm.createContext({ __host: host });
  const settle = () => new Promise<void>((resolve) => setImmediate(resolve));
  try {
    vm.runInContext(library, context, { timeout: 2000 });
    vm.runInContext(`(async function () {\n${code}\n})().catch(function (e) { __host.fail(__describe(e)); });`, context, { timeout: 3000, filename: "program.js" });
    await settle();
    const step = 1 / 30;
    for (let t = 0; t < seconds && !failure; t += step) {
      while (pendingFinds.length) vm.runInContext(`__resolve(${pendingFinds.shift()}, "[]")`, context, { timeout: 1000 });
      vm.runInContext(`__tick(${step}, ${t})`, context, { timeout: 1000 });
      await settle();
    }
  } catch (error) {
    failure ??= error instanceof Error ? `${error.name}: ${error.message}` : String(error);
  }
  return { ok: !failure, error: failure, nodes: alive.size, logs };
}

function normalize(v: number[]): number[] {
  const l = Math.hypot(v[0], v[1], v[2]) || 1;
  return [v[0] / l, v[1] / l, v[2] / l];
}
