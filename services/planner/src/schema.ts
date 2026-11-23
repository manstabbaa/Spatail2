/**
 * Shapes exchanged with devices. They mirror BuildRequest and BuildResult in
 * packages/com.spatail.core/Runtime/Core/Building/BuildTypes.cs.
 */

/** What the model's answer is constrained to. Every object is closed, as structured outputs require. */
export const BUILD_SCHEMA: Record<string, unknown> = {
  type: "object",
  properties: {
    reply: { type: "string", description: "One short sentence for the user, as it would be spoken." },
    title: { type: "string", description: "Two or three words naming what was built, or an empty string if nothing was." },
    id: { type: "string", description: "Id for the experience. Reuse a running experience's id to replace it; otherwise a short new kebab-case id. Empty when there is no program." },
    code: { type: "string", description: "The program: the body of an async function using the Spatail API. Empty when nothing should run." },
    remove: { type: "array", items: { type: "string" }, description: "Ids of running experiences to stop." },
    clear: { type: "boolean", description: "True to stop every running experience." },
  },
  required: ["reply", "title", "id", "code", "remove", "clear"],
  additionalProperties: false,
};

export const LOCATE_SCHEMA: Record<string, unknown> = {
  type: "object",
  properties: {
    points: {
      type: "array",
      description: "Where the described thing is in the image, best match first. Empty if it is not visible.",
      items: {
        type: "object",
        properties: {
          u: { type: "number", description: "0 at the left edge, 1 at the right edge." },
          v: { type: "number", description: "0 at the top edge, 1 at the bottom edge." },
          label: { type: "string", description: "What this point is, in a few words." },
        },
        required: ["u", "v", "label"],
        additionalProperties: false,
      },
    },
  },
  required: ["points"],
  additionalProperties: false,
};

export interface BuildRequest {
  prompt: string;
  /** Base64 JPEG of what the device's camera saw when the request was made. Absent when the device has none. */
  image?: string;
  view?: { width?: number; height?: number; depth?: { columns: number; rows: number; metres: number[] } };
  user?: { position: number[]; forward: number[] };
  floorY?: number;
  surfaces?: Array<Record<string, unknown>>;
  props?: Array<Record<string, unknown>>;
  running?: Array<{ id: string; title: string; prompt?: string; code?: string }>;
  repair?: { code: string; error: string };
}

export interface BuildResult {
  reply: string;
  title: string;
  id: string;
  code: string;
  remove: string[];
  clear: boolean;
  builder?: string;
  latencyMs?: number;
  error?: string;
}

export interface LocateRequest {
  query: string;
  image: string;
}

export interface LocateResult {
  points: Array<{ u: number; v: number; label: string }>;
  error?: string;
}
