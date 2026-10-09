/**
 * 条件 builder 树 → Javascript 表达式的编译器（ADR 0010）。
 *
 * 编辑真源是存于 customProperties.customExpression 的 builder 树；本模块把它单向编译为
 * 后端可执行的 Expression（编译目标钉死 Javascript）。纯函数、无副作用，便于 vitest 覆盖。
 */

export type ConditionDataType = "string" | "number" | "boolean" | "datetime"

/**
 * builder 操作数类型（ADR 0010）。操作数槽复用 ExpressionEditor、白名单 Literal/Variable/Input；
 * Javascript 仅为存量数据保留（原样内联），Liquid 不可作操作数（Jint 无渲染器）仅作整条件逃生舱。
 * WorkflowInput 是 Input 的旧别名，同路编译。
 */
export type ConditionOperandType = "Literal" | "Variable" | "Input" | "WorkflowInput" | "Javascript"

export type ConditionOperator =
  | "="
  | "!="
  | ">"
  | ">="
  | "<"
  | "<="
  | "contains"
  | "notContains"
  | "startsWith"
  | "endsWith"
  | "empty"
  | "notEmpty"

export interface ConditionOperand {
  type: ConditionOperandType
  value?: unknown
  dataType?: ConditionDataType
}

export interface ConditionComparison {
  left: ConditionOperand
  operator: ConditionOperator
  right: ConditionOperand
}

export interface ConditionGroup {
  conditionType: "and" | "or"
  conditions: ConditionComparison[]
  combineCondition?: ConditionGroup
}

/** 单个条件（If 的 ifCondition / Switch 的 caseConditions[i]）的编辑态。 */
export interface ConditionSpec {
  type: "custom" | "Literal" | "Javascript" | "Liquid"
  value: ConditionGroup | boolean | string
}

/** 编译产物：与后端 Expression { type, value } 同构。 */
export interface CompiledExpression {
  type: string
  value: unknown
}

/** 各数据类型可用的运算符全集（UI 与校验共用）。 */
export const OPERATORS_BY_DATATYPE: Record<ConditionDataType, ConditionOperator[]> = {
  number: ["=", "!=", ">", ">=", "<", "<=", "empty", "notEmpty"],
  datetime: ["=", "!=", ">", ">=", "<", "<=", "empty", "notEmpty"],
  string: ["=", "!=", "contains", "notContains", "startsWith", "endsWith", "empty", "notEmpty"],
  boolean: ["=", "!="],
}

/** 各 dataType 的字面量零值（切到 Literal / 换 dataType 时重置 value 用）。 */
export function defaultLiteralValue(dataType: ConditionDataType): unknown {
  switch (dataType) {
    case "number":
      return 0
    case "boolean":
      return false
    default:
      return ""
  }
}

// Backend variable/argument CLR type names (VariableDefinition.typeName /
// ArgumentDefinition.type), matched case-insensitively.
const CLR_NUMBER_TYPES = new Set([
  "byte",
  "sbyte",
  "int16",
  "int32",
  "int64",
  "uint16",
  "uint32",
  "uint64",
  "single",
  "double",
  "decimal",
])
const CLR_DATETIME_TYPES = new Set(["datetime", "datetimeoffset", "dateonly", "timeonly"])

/**
 * 后端 CLR 类型名 → builder dataType；未知 / 缺省返回 undefined，
 * 让调用方保留自己的回退链。
 */
export function conditionDataTypeOfClrType(typeName?: string | null): ConditionDataType | undefined {
  const name = (typeName ?? "").trim().toLowerCase()
  if (CLR_NUMBER_TYPES.has(name)) return "number"
  if (name === "boolean") return "boolean"
  if (CLR_DATETIME_TYPES.has(name)) return "datetime"
  if (name === "string") return "string"
  return undefined
}

/**
 * 名字引用操作数携带的 dataType：Variable / Input / WorkflowInput 按名字解析到
 * 工作流变量 / 输入定义；其余（Literal、Javascript、未知名字、未映射类型）返回 undefined。
 */
export function referenceDataTypeOf(
  operand: ConditionOperand | undefined,
  variables: ReadonlyArray<{ name?: string; typeName?: string }>,
  inputs: ReadonlyArray<{ name?: string; type?: string }>,
): ConditionDataType | undefined {
  if (!operand) return undefined
  const name = typeof operand.value === "string" ? operand.value : ""
  if (!name) return undefined
  if (operand.type === "Variable") return conditionDataTypeOfClrType(variables.find((v) => v.name === name)?.typeName)
  if (operand.type === "Input" || operand.type === "WorkflowInput")
    return conditionDataTypeOfClrType(inputs.find((i) => i.name === name)?.type)
  return undefined
}

/** 把操作数的 dataType 换为 dataType，value 按目标类型强制（已兼容则保留）。 */
export function withConditionDataType(operand: ConditionOperand, dataType: ConditionDataType): ConditionOperand {
  return { ...operand, dataType, value: coerceValueForDataType(operand.value, dataType) }
}

/**
 * 把比较行里 Literal 操作数的 dataType 钉到引用定义解析出的类型（递归子组）。
 * 纯函数派生：不改动存库的 builder 树，仅供显示与编译取一致语义。
 */
export function normalizeGroup(
  group: ConditionGroup,
  variables: ReadonlyArray<{ name?: string; typeName?: string }> = [],
  inputs: ReadonlyArray<{ name?: string; type?: string }> = [],
): ConditionGroup {
  const conditions = (group.conditions ?? []).map((cmp) => {
    const ref = referenceDataTypeOf(cmp.left, variables, inputs) ?? referenceDataTypeOf(cmp.right, variables, inputs)
    if (!ref) return cmp
    const next: ConditionComparison = { ...cmp }
    if (next.left?.type === "Literal" && next.left.dataType !== ref) next.left = withConditionDataType(next.left, ref)
    if (next.right?.type === "Literal" && next.right.dataType !== ref) next.right = withConditionDataType(next.right, ref)
    return next
  })
  return {
    ...group,
    conditions,
    combineCondition: group.combineCondition ? normalizeGroup(group.combineCondition, variables, inputs) : undefined,
  }
}

function coerceValueForDataType(value: unknown, dataType: ConditionDataType): unknown {
  switch (dataType) {
    case "number":
      if (typeof value === "number" && Number.isFinite(value)) return value
      if (typeof value === "string" && value.trim() !== "" && Number.isFinite(Number(value))) return Number(value)
      return 0
    case "boolean":
      return typeof value === "boolean" ? value : false
    case "datetime":
      return typeof value === "string" ? value : ""
    case "string":
    default:
      return value == null ? "" : String(value)
  }
}

function compileOperand(operand: ConditionOperand): string {
  if (operand.type === "Javascript") {
    const code = String(operand.value ?? "");
    // An unfinished (empty) script operand would otherwise emit nothing and
    // produce syntactically invalid JS like `( === "")`; degrade to undefined.
    return code.trim() === "" ? "undefined" : code;
  }
  // Name-referencing operands compile to the two Jint accessors the backend registers.
  if (operand.type === "Variable") return `getVariable(${JSON.stringify(String(operand.value ?? ""))})`
  if (operand.type === "Input" || operand.type === "WorkflowInput")
    return `getInput(${JSON.stringify(String(operand.value ?? ""))})`
  switch (operand.dataType) {
    case "number":
      return String(operand.value)
    case "boolean":
      return operand.value === true ? "true" : "false"
    case "datetime":
      return `new Date(${JSON.stringify(String(operand.value ?? ""))}).getTime()`
    case "string":
    default:
      return JSON.stringify(operand.value == null ? "" : String(operand.value))
  }
}

function compileComparison(comparison: ConditionComparison): string {
  const left = compileOperand(comparison.left)
  const right = compileOperand(comparison.right)
  switch (comparison.operator) {
    case "=":
      return `(${left} === ${right})`
    case "!=":
      return `(${left} !== ${right})`
    case ">":
      return `(${left} > ${right})`
    case ">=":
      return `(${left} >= ${right})`
    case "<":
      return `(${left} < ${right})`
    case "<=":
      return `(${left} <= ${right})`
    case "contains":
      return `(String(${left}).includes(${right}))`
    case "notContains":
      return `(!String(${left}).includes(${right}))`
    case "startsWith":
      return `(String(${left}).startsWith(${right}))`
    case "endsWith":
      return `(String(${left}).endsWith(${right}))`
    case "empty":
      return `(${left} == null || ${left} === "")`
    case "notEmpty":
      return `(!(${left} == null || ${left} === ""))`
    default:
      return `(${left} === ${right})`
  }
}

/** 组 = conditions[] 用 conditionType 接合，再与（加括号的）combineCondition 用同一操作符接合。 */
export function compileGroup(
  group: ConditionGroup,
  variables: ReadonlyArray<{ name?: string; typeName?: string }> = [],
  inputs: ReadonlyArray<{ name?: string; type?: string }> = [],
): string {
  const normalized = normalizeGroup(group, variables, inputs)
  const op = normalized.conditionType === "or" ? "||" : "&&"
  const parts = (normalized.conditions ?? []).map(compileComparison)
  if (normalized.combineCondition) parts.push(`(${compileGroup(normalized.combineCondition, variables, inputs)})`)
  if (parts.length === 0) return "true"
  return parts.join(` ${op} `)
}

/** 整个条件（含逃生舱类型）→ 后端 Expression。 */
export function compileCondition(
  spec: ConditionSpec,
  variables: ReadonlyArray<{ name?: string; typeName?: string }> = [],
  inputs: ReadonlyArray<{ name?: string; type?: string }> = [],
): CompiledExpression {
  switch (spec.type) {
    case "custom":
      return { type: "Javascript", value: compileGroup(spec.value as ConditionGroup, variables, inputs) }
    case "Literal":
      return { type: "Literal", value: spec.value === true }
    case "Liquid":
      return { type: "Liquid", value: String(spec.value ?? "") }
    case "Javascript":
    default:
      return { type: "Javascript", value: String(spec.value ?? "") }
  }
}
