import type { IActivity } from "../abstracts/Activity";
import { resolveActivityClass } from "./registry";

/**
 * Descriptor-driven generic form logic: resolve which activity fields are
 * editable inputs and how a descriptor maps onto the (plain-object) model.
 */

export interface InputFieldDescriptorInput {
  name?: string;
  clrName?: string;
  displayName?: string | null;
  description?: string | null;
  isReadOnly?: boolean | null;
  defaultValue?: unknown;
}

export interface ResolvedInputField {
  /** Model field on the activity instance (camelCase). */
  field: string;
  label: string;
  description: string | null;
  readOnly: boolean;
  /** Backend-provided default, used as the Literal reset value (ADR 0005). */
  defaultValue?: unknown;
  /** The Input-shaped value: { memoryBlockReference, expression: { type, value } }. */
  input: { expression: { type: string; value?: unknown } } | null;
}

function isInputShaped(value: unknown): value is { expression: { type: string; value?: unknown } } {
  return (
    typeof value === "object" &&
    value !== null &&
    "expression" in (value as object) &&
    typeof (value as { expression?: unknown }).expression === "object" &&
    (value as { expression?: unknown }).expression !== null
  );
}

function lowerFirst(name: string): string {
  return name ? name[0].toLowerCase() + name.slice(1) : name;
}

export function resolveInputFields(activity: IActivity, descriptors: InputFieldDescriptorInput[]): ResolvedInputField[] {
  const resolved: ResolvedInputField[] = [];
  for (const descriptor of descriptors) {
    const candidates = [descriptor.clrName, descriptor.name].filter((name): name is string => !!name);
    let field = candidates.map(lowerFirst).find((name) => name in activity) ?? null;
    if (!field) continue;
    const value = (activity as unknown as Record<string, unknown>)[field];
    resolved.push({
      field,
      label: descriptor.displayName ?? descriptor.name ?? field,
      description: descriptor.description ?? null,
      readOnly: descriptor.isReadOnly === true,
      defaultValue: descriptor.defaultValue,
      input: isInputShaped(value) ? value : null,
    });
  }
  return resolved;
}

export interface OutputFieldDescriptorInput {
  name?: string;
  clrName?: string;
  displayName?: string | null;
  description?: string | null;
  isBrowsable?: boolean | null;
}

/** An Output-shaped value: a memory block reference, no expression (CONTEXT.md「输出」). */
export interface OutputBinding {
  memoryBlockReference: { id: string };
}

export interface ResolvedOutputField {
  /** Model field on the activity instance (camelCase). */
  field: string;
  label: string;
  description: string | null;
  /** The current binding, or null when the output is unbound. */
  output: OutputBinding | null;
}

function isOutputShaped(value: unknown): value is OutputBinding {
  if (typeof value !== "object" || value === null) return false;
  const ref = (value as { memoryBlockReference?: unknown }).memoryBlockReference;
  return typeof ref === "object" && ref !== null && typeof (ref as { id?: unknown }).id === "string";
}

/**
 * Descriptor-driven output resolution, mirroring resolveInputFields: map each
 * browsable output descriptor onto the activity field it names and read the
 * current binding. Descriptors flagged non-browsable, or with no matching field,
 * are dropped — so the panel hides the outputs section when nothing resolves.
 */
export function resolveOutputFields(
  activity: IActivity,
  descriptors: OutputFieldDescriptorInput[],
): ResolvedOutputField[] {
  // Resolve field presence against the activity's *declared* shape, not its live
  // own properties: unbinding writes null through makeEditPropertyCommand, which
  // deletes the own property (commands.ts drops null-valued keys). A deleted
  // output must still resolve — as unbound — so its row stays visible and
  // re-bindable (spec stories 7–8). A pristine instance of the mirrored class
  // exposes exactly the declared output fields; unknown types fall back to the
  // live object (their outputs ride the raw JSON bag, not declared fields).
  const Ctor = resolveActivityClass(activity.type);
  const shape: Record<string, unknown> = Ctor
    ? (new Ctor() as unknown as Record<string, unknown>)
    : (activity as unknown as Record<string, unknown>);
  const resolved: ResolvedOutputField[] = [];
  for (const descriptor of descriptors) {
    if (descriptor.isBrowsable === false) continue;
    const candidates = [descriptor.clrName, descriptor.name].filter((name): name is string => !!name);
    const field = candidates.map(lowerFirst).find((name) => name in shape) ?? null;
    if (!field) continue;
    const value = (activity as unknown as Record<string, unknown>)[field];
    resolved.push({
      field,
      label: descriptor.displayName ?? descriptor.name ?? field,
      description: descriptor.description ?? null,
      output: isOutputShaped(value) ? value : null,
    });
  }
  return resolved;
}

/**
 * Builds the value written to an activity's output field when binding it to a
 * variable: a memory block reference keyed by the variable's stable id, so a
 * rename never breaks the binding (CONTEXT.md「输出绑定」). A null id unbinds.
 */
export function makeOutputBinding(variableId: string | null): OutputBinding | null {
  return variableId == null ? null : { memoryBlockReference: { id: variableId } };
}

/** Resolves a variable by its stable id; undefined when unbound or dangling. */
export function findVariableById<T extends { id?: string }>(
  variables: T[],
  id: string | null | undefined,
): T | undefined {
  if (id == null) return undefined;
  return variables.find((variable) => variable.id === id);
}

export const MERGE_MODES = ["Stream", "Merge", "Converge", "Cascade", "Race"] as const;

export function getMergeMode(activity: IActivity): string | null {
  const value = activity.customProperties?.["mergeMode"];
  return typeof value === "string" && value ? value : null;
}

export function setMergeMode(activity: IActivity, mode: string | null): void {
  const customProperties = (activity.customProperties ??= {}) as Record<string, unknown>;
  if (!mode) delete customProperties["mergeMode"];
  else customProperties["mergeMode"] = mode;
}

/** Options-level workflow variables (mirrors backend WorkflowVariableDefinition). */
export interface WorkflowVariableInput {
  name?: string;
  typeName?: string;
  isArray?: boolean;
}

/** Lightweight, non-blocking validation matching the backend publish rules. */
export function variableNameIssues(variables: WorkflowVariableInput[]): string[] {
  const issues: string[] = [];
  variables.forEach((variable, index) => {
    if (!variable.name) issues.push(`第 ${index + 1} 个变量名称为空`);
  });
  const seen = new Map<string, number>();
  for (const variable of variables) {
    if (!variable.name) continue;
    seen.set(variable.name, (seen.get(variable.name) ?? 0) + 1);
  }
  for (const [name, count] of seen) {
    if (count > 1) issues.push(`变量名称 [${name}] 重复`);
  }
  return issues;
}

/**
 * Coerce a text-edited literal back into the shape of the previous value so
 * numbers/booleans/objects survive a roundtrip through an input element.
 * Returns undefined when an object payload fails to parse (caller no-ops).
 */
export function coerceLiteralValue(from: unknown, raw: string): unknown {
  if (typeof from === "number") return raw === "" ? null : Number(raw);
  if (typeof from === "boolean") return raw === "true";
  if (from != null && typeof from === "object") {
    try {
      return raw ? JSON.parse(raw) : null;
    } catch {
      return undefined;
    }
  }
  return raw === "" ? null : raw;
}
