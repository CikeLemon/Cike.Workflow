import { describe, expect, it } from "vitest";
import { For } from "../activities/For";
import { If } from "../activities/If";
import { SendHttpRequest } from "../activities/SendHttpRequest";
import { Output } from "../models/Output";
import {
  findVariableById,
  getMergeMode,
  makeOutputBinding,
  resolveInputFields,
  resolveOutputFields,
  setMergeMode,
  variableNameIssues,
} from "./form";

describe("form", () => {
  it("ResolveInputFields_MapsClrNameToLowerFirst", () => {
    const activity = new If();
    const fields = resolveInputFields(activity, [
      { clrName: "Condition", displayName: "条件", name: "Condition" },
      { clrName: "NonExistent" },
    ]);
    expect(fields.length).toBe(1);
    expect(fields[0].field).toBe("condition");
    expect(fields[0].label).toBe("条件");
    expect(fields[0].input).not.toBeNull();
    expect(fields[0].input!.expression.type).toBe("Literal");
  });

  it("MergeMode_SetGetRemove", () => {
    const activity = new If();
    expect(getMergeMode(activity)).toBeNull();
    setMergeMode(activity, "Merge");
    expect(getMergeMode(activity)).toBe("Merge");
    expect(activity.customProperties["mergeMode"]).toBe("Merge");
    setMergeMode(activity, null);
    expect(getMergeMode(activity)).toBeNull();
  });
});

describe("variableNameIssues", () => {
  it("FlagsEmptyAndDuplicateNames", () => {
    const issues = variableNameIssues([
      { name: "amount", typeName: "String" },
      { name: "", typeName: "String" },
      { name: "amount", typeName: "Int32" },
    ]);
    expect(issues.length).toBe(2);
    expect(issues[0]).toContain("空");
    expect(issues[1]).toContain("amount");
  });
});

describe("resolveOutputFields", () => {
  it("MapsClrNameToLowerFirstAndReadsCurrentBinding", () => {
    const activity = new SendHttpRequest();
    activity.statusCode = new Output<number>("var-1");
    const fields = resolveOutputFields(activity, [
      { clrName: "StatusCode", name: "StatusCode", displayName: "状态码", description: "HTTP 状态码" },
      { clrName: "ParsedContent", name: "ParsedContent" },
      { clrName: "NonExistent" },
    ]);
    // NonExistent has no matching field on the activity, so it is dropped.
    expect(fields.map((f) => f.field)).toEqual(["statusCode", "parsedContent"]);
    expect(fields[0].label).toBe("状态码");
    expect(fields[0].description).toBe("HTTP 状态码");
    // A bound output exposes its memory block reference id (the variable id).
    expect(fields[0].output).toEqual({ memoryBlockReference: { id: "var-1" } });
    // An unbound output defaults to null; label falls back to the raw name.
    expect(fields[1].output).toBeNull();
    expect(fields[1].label).toBe("ParsedContent");
  });

  it("FiltersNonBrowsableOutputs", () => {
    const fields = resolveOutputFields(new For(), [
      { clrName: "CurrentValue", name: "CurrentValue", isBrowsable: false },
    ]);
    expect(fields).toEqual([]);
  });

  it("ResolvesNothingWhenActivityHasNoMatchingOutputField", () => {
    // If declares no output fields; a lone descriptor resolves to an empty list,
    // so the panel hides the outputs section entirely.
    expect(resolveOutputFields(new If(), [{ clrName: "CurrentValue" }])).toEqual([]);
  });

  it("KeepsUnboundFieldResolvableAfterItsOwnPropertyIsDeleted", () => {
    // Unbinding writes null through makeEditPropertyCommand, which deletes the
    // own property (commands.ts drops null-valued keys). The field must still
    // resolve — as unbound — so its row stays visible and re-bindable instead of
    // vanishing (spec stories 7–8: an unbound output keeps its default state).
    const activity = new SendHttpRequest();
    activity.statusCode = new Output<number>("var-1");
    const descriptors = [{ clrName: "StatusCode", name: "StatusCode", displayName: "状态码" }];
    expect(resolveOutputFields(activity, descriptors)[0].output).toEqual({
      memoryBlockReference: { id: "var-1" },
    });
    delete (activity as unknown as Record<string, unknown>).statusCode;
    const afterUnbind = resolveOutputFields(activity, descriptors);
    expect(afterUnbind.map((f) => f.field)).toEqual(["statusCode"]);
    expect(afterUnbind[0].output).toBeNull();
  });
});

describe("makeOutputBinding", () => {
  it("BuildsMemoryBlockReferenceKeyedByVariableId", () => {
    expect(makeOutputBinding("var-1")).toEqual({ memoryBlockReference: { id: "var-1" } });
  });

  it("UnbindsToNull", () => {
    expect(makeOutputBinding(null)).toBeNull();
  });
});

describe("findVariableById", () => {
  const variables = [
    { id: "v1", name: "alpha" },
    { id: "v2", name: "beta" },
  ];

  it("ResolvesVariableById", () => {
    expect(findVariableById(variables, "v2")?.name).toBe("beta");
  });

  it("ReturnsUndefinedForDanglingOrEmptyId", () => {
    expect(findVariableById(variables, "ghost")).toBeUndefined();
    expect(findVariableById(variables, null)).toBeUndefined();
    expect(findVariableById(variables, undefined)).toBeUndefined();
  });
});
