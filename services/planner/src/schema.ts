/**
 * The plan wire format, as a JSON Schema the model's output is constrained to.
 *
 * This mirrors SpecJson.cs in the Unity package: the device parses exactly this shape. Structured outputs need
 * every object closed (`additionalProperties: false`) and do not support numeric or length constraints, so
 * ranges are described in prose and enforced on the device (SpecJson.Sanitize).
 */

export const ELEMENT_TYPES = [
  "label", "pin", "card", "gizmo", "flow", "highlight", "chart", "checklist", "avatar", "panel",
] as const;

export const ANCHOR_MODES = [
  "center", "top", "above", "callout", "floor-near", "surface", "wall", "user",
] as const;

export const STYLES = ["accent", "neutral", "warning", "success"] as const;

type Schema = Record<string, unknown>;

const vec3: Schema = {
  type: "object",
  properties: { x: { type: "number" }, y: { type: "number" }, z: { type: "number" } },
  required: ["x", "y", "z"],
  additionalProperties: false,
};

const anchor: Schema = {
  type: "object",
  description: "Where the element goes, relative to an entity in the room.",
  properties: {
    entity: { type: "string", description: "An entity id from the room model. Use an empty string only with at = \"user\"." },
    at: { type: "string", enum: [...ANCHOR_MODES] },
    offset: { ...vec3, description: "Extra offset in metres along the entity's local axes. Usually all zeros." },
  },
  required: ["entity", "at", "offset"],
  additionalProperties: false,
};

/** Fields every element carries. */
const common = {
  id: { type: "string", description: "Unique within the build, e.g. \"pin-1\"." },
  anchor,
  step: { type: "integer", description: "0 = always visible. n = only while the build is on step n." },
  style: { type: "string", enum: [...STYLES] },
  scale: { type: "number", description: "1 is the designed size. Between 0.5 and 2." },
};
const commonRequired = ["id", "type", "anchor", "step", "style", "scale"];

function element(type: string, description: string, properties: Schema, required: string[]): Schema {
  return {
    type: "object",
    description,
    properties: { type: { type: "string", const: type }, ...common, ...properties },
    required: [...commonRequired, ...required],
    additionalProperties: false,
  };
}

const elements: Schema[] = [
  element("label", "A pill of text tethered to an entity. neutral = outlined, accent = solid, warning = orange.",
    { text: { type: "string" } }, ["text"]),
  element("pin", "A numbered marker above an entity. Use number for steps, or number 0 with text \"!\" for alerts.",
    { number: { type: "integer" }, text: { type: "string" } }, ["number", "text"]),
  element("card", "A numbered instruction card. Anchor with \"callout\" so it sits beside the part.",
    { number: { type: "integer" }, title: { type: "string" }, body: { type: "string" } }, ["number", "title", "body"]),
  element("gizmo", "A motion cue on a part. Anchor with \"top\"; it centres itself on the part's action point.",
    { action: { type: "string", enum: ["turn-cw", "turn-ccw"] } }, ["action"]),
  element("flow", "Animated chevrons along a path, with listed entities lit. Anchor to the equipment with \"center\".",
    {
      points: { type: "array", items: vec3, description: "Centre-line in the equipment's frame (origin on the floor under the equipment, metres), upstream to downstream." },
      radius: { type: "number", description: "Pipe radius in metres." },
      entities: { type: "array", items: { type: "string" }, description: "Entity ids to light up." },
      text: { type: "string", description: "Short caption such as a flow rate, or empty." },
    }, ["points", "radius", "entities", "text"]),
  element("highlight", "A pulsing tinted volume over an entity. Anchor with \"center\".",
    { title: { type: "string" }, body: { type: "string" } }, ["title", "body"]),
  element("chart", "A 3D bar chart with a trend line. Anchor with \"floor-near\" (stands on the floor beside the entity) or \"surface\".",
    {
      title: { type: "string" },
      unit: { type: "string" },
      values: { type: "array", items: { type: "number" } },
      labels: { type: "array", items: { type: "string" } },
    }, ["title", "unit", "values", "labels"]),
  element("checklist", "A tickable list. Anchor with \"above\" a table or \"wall\".",
    {
      title: { type: "string" },
      items: {
        type: "array",
        items: {
          type: "object",
          properties: { text: { type: "string" }, done: { type: "boolean" } },
          required: ["text", "done"],
          additionalProperties: false,
        },
      },
      people: { type: "array", items: { type: "string" }, description: "Initials of people on the task." },
      more: { type: "integer", description: "How many more people are not shown." },
    }, ["title", "items", "people", "more"]),
  element("avatar", "A collaborator's presence marker standing on the floor. Anchor with \"floor-near\".",
    { name: { type: "string" }, text: { type: "string", description: "Initial shown on the marker." } }, ["name", "text"]),
  element("panel", "A free-form text panel: a title and a short paragraph. Anchor with \"callout\", \"above\", \"wall\" or \"user\".",
    { title: { type: "string" }, body: { type: "string" } }, ["title", "body"]),
];

const build: Schema = {
  type: "object",
  properties: {
    id: { type: "string", description: "Stable id. Re-using an existing build's id replaces that build." },
    kind: { type: "string", enum: ["labels", "walkthrough", "flow", "faults", "dashboard", "checklist", "session", "note", "custom"] },
    title: { type: "string", description: "Two or three words, shown as \"Built <title>\"." },
    stepCount: { type: "integer", description: "Number of steps, or 0 when the build is not sequenced." },
    currentStep: { type: "integer", description: "1 for a new sequenced build, 0 otherwise." },
    elements: { type: "array", items: { anyOf: elements } },
  },
  required: ["id", "kind", "title", "stepCount", "currentStep", "elements"],
  additionalProperties: false,
};

const ops: Schema[] = [
  {
    type: "object",
    description: "Add a build, or replace the one with the same id.",
    properties: { op: { type: "string", const: "add" }, build },
    required: ["op", "build"],
    additionalProperties: false,
  },
  {
    type: "object",
    properties: { op: { type: "string", const: "remove" }, buildId: { type: "string" } },
    required: ["op", "buildId"],
    additionalProperties: false,
  },
  {
    type: "object",
    description: "Move a sequenced build to a step (1-based).",
    properties: { op: { type: "string", const: "step" }, buildId: { type: "string" }, step: { type: "integer" } },
    required: ["op", "buildId", "step"],
    additionalProperties: false,
  },
  {
    type: "object",
    description: "Remove every build.",
    properties: { op: { type: "string", const: "clear" } },
    required: ["op"],
    additionalProperties: false,
  },
];

export const PLAN_SCHEMA: Schema = {
  type: "object",
  properties: {
    reply: { type: "string", description: "One short sentence for the user, as it would be spoken." },
    chip: { type: "string", description: "The title of what was built or changed, or an empty string if nothing was." },
    ops: { type: "array", items: { anyOf: ops } },
  },
  required: ["reply", "chip", "ops"],
  additionalProperties: false,
};

// ---- TypeScript views of the same shapes ---------------------------------------------------

export interface Vec3 { x: number; y: number; z: number }

export interface PlanElement {
  id: string;
  type: (typeof ELEMENT_TYPES)[number];
  anchor: { entity: string; at: (typeof ANCHOR_MODES)[number]; offset: Vec3 };
  step: number;
  style: (typeof STYLES)[number];
  scale: number;
  [field: string]: unknown;
}

export interface PlanBuild {
  id: string;
  kind: string;
  title: string;
  stepCount: number;
  currentStep: number;
  elements: PlanElement[];
}

export type PlanOp =
  | { op: "add"; build: PlanBuild }
  | { op: "remove"; buildId: string }
  | { op: "step"; buildId: string; step: number }
  | { op: "clear" };

export interface PlanResult {
  reply: string;
  chip: string;
  ops: PlanOp[];
  planner?: string;
  latencyMs?: number;
  error?: string;
}

/** What the device sends to POST /v1/plan. See RemotePlanner.BuildRequestJson in the Unity package. */
export interface PlanRequest {
  prompt: string;
  site?: { id?: string; name?: string; equipment?: string };
  room: { floorY?: number; user?: unknown; entities: Array<Record<string, unknown>> };
  knowledge?: Record<string, unknown>;
  builds?: Array<Record<string, unknown>>;
  focus?: string;
}
