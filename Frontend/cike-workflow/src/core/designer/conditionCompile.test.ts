import { describe, expect, it } from "vitest"
import {
  compileCondition,
  compileGroup,
  conditionDataTypeOfClrType,
  normalizeGroup,
  OPERATORS_BY_DATATYPE,
  referenceDataTypeOf,
  withConditionDataType,
  type ConditionComparison,
  type ConditionGroup,
  type ConditionOperand,
} from "./conditionCompile"

function js(value: string): ConditionOperand {
  return { type: "Javascript", value }
}

function lit(value: unknown, dataType: ConditionOperand["dataType"]): ConditionOperand {
  return { type: "Literal", value, dataType }
}

function variable(name: string): ConditionOperand {
  return { type: "Variable", value: name }
}

function input(name: string): ConditionOperand {
  return { type: "Input", value: name }
}

function cmp(left: ConditionOperand, operator: ConditionComparison["operator"], right: ConditionOperand): ConditionComparison {
  return { left, operator, right }
}

describe("compileGroup", () => {
  it("compiles a flat and-group of comparisons", () => {
    const group: ConditionGroup = {
      conditionType: "and",
      conditions: [
        cmp(js("getVariable('age')"), ">", lit(18, "number")),
        cmp(js("getVariable('name')"), "=", lit("tom", "string")),
      ],
    }
    expect(compileGroup(group)).toBe(
      `(getVariable('age') > 18) && (getVariable('name') === "tom")`,
    )
  })

  it("joins conditions and combineCondition with the group operator, parenthesizing the subgroup", () => {
    const group: ConditionGroup = {
      conditionType: "and",
      conditions: [cmp(js("a"), ">", lit(1, "number"))],
      combineCondition: {
        conditionType: "or",
        conditions: [
          cmp(js("b"), "=", lit(true, "boolean")),
          cmp(js("c"), "<", lit(2, "number")),
        ],
      },
    }
    expect(compileGroup(group)).toBe(`(a > 1) && ((b === true) || (c < 2))`)
  })

  it("degrades an empty group to the identity true", () => {
    expect(compileGroup({ conditionType: "and", conditions: [] })).toBe("true")
  })

  it("compiles string helpers and unary empty", () => {
    const group: ConditionGroup = {
      conditionType: "or",
      conditions: [
        cmp(js("getVariable('code')"), "contains", lit("VIP", "string")),
        cmp(js("getVariable('note')"), "empty", lit("", "string")),
      ],
    }
    expect(compileGroup(group)).toBe(
      `(String(getVariable('code')).includes("VIP")) || (getVariable('note') == null || getVariable('note') === "")`,
    )
  })

  it("compiles datetime literals to epoch milliseconds", () => {
    const group: ConditionGroup = {
      conditionType: "and",
      conditions: [cmp(js("getVariable('at')"), ">=", lit("2026-01-01T00:00:00Z", "datetime"))],
    }
    expect(compileGroup(group)).toContain(`new Date("2026-01-01T00:00:00Z").getTime()`)
  })

  it("degrades an empty Javascript operand to undefined so output stays valid JS", () => {
    const group: ConditionGroup = {
      conditionType: "and",
      conditions: [cmp(js(""), "=", lit("", "string"))],
    }
    expect(compileGroup(group)).toBe(`(undefined === "")`)
  })

  it("compiles Variable operands to the getVariable accessor", () => {
    const group: ConditionGroup = {
      conditionType: "and",
      conditions: [cmp(variable("age"), ">", lit(18, "number"))],
    }
    expect(compileGroup(group)).toBe(`(getVariable("age") > 18)`)
  })

  it("compiles Input and legacy WorkflowInput operands to the getInput accessor", () => {
    const group: ConditionGroup = {
      conditionType: "and",
      conditions: [
        cmp(input("score"), ">=", lit(90, "number")),
        cmp({ type: "WorkflowInput", value: "legacy" }, "=", lit("x", "string")),
      ],
    }
    expect(compileGroup(group)).toBe(`(getInput("score") >= 90) && (getInput("legacy") === "x")`)
  })

  it("compiles a stale string literal against a number reference as a number", () => {
    const group: ConditionGroup = {
      conditionType: "and",
      conditions: [cmp(input("score"), ">", lit("90", "string"))],
    }
    expect(compileGroup(group, [], [{ name: "score", type: "Decimal" }])).toBe(`(getInput("score") > 90)`)
  })
})

describe("compileCondition", () => {
  it("compiles a custom tree into a Javascript expression", () => {
    const compiled = compileCondition({
      type: "custom",
      value: { conditionType: "or", conditions: [cmp(js("x"), "=", lit(1, "number"))] },
    })
    expect(compiled).toEqual({ type: "Javascript", value: "(x === 1)" })
  })

  it("passes escape-hatch types through unchanged", () => {
    expect(compileCondition({ type: "Literal", value: true })).toEqual({ type: "Literal", value: true })
    expect(compileCondition({ type: "Liquid", value: "{{ ok }}" })).toEqual({ type: "Liquid", value: "{{ ok }}" })
    expect(compileCondition({ type: "Javascript", value: "a > 1" })).toEqual({ type: "Javascript", value: "a > 1" })
  })
})

describe("conditionDataTypeOfClrType", () => {
  it("maps backend serialization aliases to builder data types", () => {
    expect(conditionDataTypeOfClrType("Int32")).toBe("number")
    expect(conditionDataTypeOfClrType("Decimal")).toBe("number")
    expect(conditionDataTypeOfClrType("double")).toBe("number")
    expect(conditionDataTypeOfClrType("Boolean")).toBe("boolean")
    expect(conditionDataTypeOfClrType("DateTime")).toBe("datetime")
    expect(conditionDataTypeOfClrType("DateTimeOffset")).toBe("datetime")
    expect(conditionDataTypeOfClrType("String")).toBe("string")
  })

  it("returns undefined for unknown or absent names", () => {
    expect(conditionDataTypeOfClrType("Object")).toBeUndefined()
    expect(conditionDataTypeOfClrType("Int32[]")).toBeUndefined()
    expect(conditionDataTypeOfClrType(undefined)).toBeUndefined()
    expect(conditionDataTypeOfClrType("")).toBeUndefined()
  })
})

describe("referenceDataTypeOf", () => {
  const variables = [{ name: "count", typeName: "Int32" }]
  const inputs = [
    { name: "userId", type: "String" },
    { name: "score", type: "Decimal" },
  ]

  it("resolves Variable/Input names against the workflow definitions", () => {
    expect(referenceDataTypeOf({ type: "Variable", value: "count" }, variables, inputs)).toBe("number")
    expect(referenceDataTypeOf({ type: "Input", value: "score" }, variables, inputs)).toBe("number")
    expect(referenceDataTypeOf({ type: "WorkflowInput", value: "userId" }, variables, inputs)).toBe("string")
  })

  it("yields undefined for literals, unknown names and unmapped types", () => {
    expect(referenceDataTypeOf({ type: "Literal", value: 1, dataType: "number" }, variables, inputs)).toBeUndefined()
    expect(referenceDataTypeOf({ type: "Input", value: "nope" }, variables, inputs)).toBeUndefined()
    expect(referenceDataTypeOf({ type: "Variable", value: "obj" }, [{ name: "obj", typeName: "Object" }], inputs)).toBeUndefined()
  })
})

describe("withConditionDataType", () => {
  it("keeps compatible values and coerces incompatible ones", () => {
    expect(withConditionDataType({ type: "Literal", value: "5", dataType: "string" }, "number")).toEqual({
      type: "Literal",
      value: 5,
      dataType: "number",
    })
    expect(withConditionDataType({ type: "Literal", value: "abc", dataType: "string" }, "number")).toEqual({
      type: "Literal",
      value: 0,
      dataType: "number",
    })
    expect(withConditionDataType({ type: "Literal", value: 5, dataType: "number" }, "string")).toEqual({
      type: "Literal",
      value: "5",
      dataType: "string",
    })
    expect(withConditionDataType({ type: "Literal", value: "", dataType: "string" }, "boolean")).toEqual({
      type: "Literal",
      value: false,
      dataType: "boolean",
    })
  })
})

describe("normalizeGroup", () => {
  const variables = [{ name: "count", typeName: "Int32" }]
  const inputs = [{ name: "score", type: "Decimal" }]

  it("pins literal dataType to the referenced definition type", () => {
    const group: ConditionGroup = {
      conditionType: "and",
      conditions: [cmp(input("score"), "=", lit("", "string"))],
    }
    const normalized = normalizeGroup(group, variables, inputs)
    expect(normalized.conditions[0].right).toEqual({ type: "Literal", value: 0, dataType: "number" })
  })

  it("recurses into combineCondition and leaves reference-free rows untouched", () => {
    const group: ConditionGroup = {
      conditionType: "and",
      conditions: [cmp(lit("a", "string"), "=", lit("b", "string"))],
      combineCondition: { conditionType: "or", conditions: [cmp(variable("count"), ">", lit("", "string"))] },
    }
    const normalized = normalizeGroup(group, variables, inputs)
    expect(normalized.conditions[0]).toEqual(group.conditions[0])
    expect(normalized.combineCondition?.conditions[0].right).toEqual({ type: "Literal", value: 0, dataType: "number" })
  })
})

describe("OPERATORS_BY_DATATYPE", () => {
  it("gives number and datetime ordering operators plus emptiness checks", () => {
    expect(OPERATORS_BY_DATATYPE.number).toEqual(["=", "!=", ">", ">=", "<", "<=", "empty", "notEmpty"])
    expect(OPERATORS_BY_DATATYPE.datetime).toEqual(["=", "!=", ">", ">=", "<", "<=", "empty", "notEmpty"])
  })
})
