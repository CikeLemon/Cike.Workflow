<template>
  <div class="space-y-1">
    <div class="flex gap-2">
      <!-- Left rail column: centred junction toggle + vertical rail above/below. -->
      <div class="flex shrink-0 flex-col items-center">
        <div class="bg-border w-px flex-1" />
        <button
          type="button"
          :disabled="readonly"
          :title="group.conditionType === 'and' ? '且（点击切换为或）' : '或（点击切换为且）'"
          class="text-muted-foreground hover:bg-accent hover:text-accent-foreground inline-flex h-7 shrink-0 items-center gap-0.5 rounded-md border border-dashed bg-transparent px-1.5 text-xs transition-colors disabled:pointer-events-none disabled:opacity-50"
          @click="toggleJunction"
        >
          {{ group.conditionType === "and" ? "且" : "或" }}
          <ArrowUpDown :size="12" class="shrink-0" />
        </button>
        <div class="bg-border w-px flex-1" />
      </div>

      <div class="min-w-0 flex-1 space-y-1.5">
        <!-- Flat comparison rows (conditions[]). -->
        <div v-for="(cmp, index) in conditions" :key="index" class="flex items-start gap-1">
          <div class="min-w-0 flex-1 space-y-1">
            <ConditionOperandEditor
              :operand="cmp.left"
              :designer="designer"
              :readonly="readonly"
              :locked-data-type="rowRefTypes[index]"
              @change="(o) => onLeftChange(index, cmp, o)"
            />
            <div class="flex items-center gap-1">
              <Select
                :model-value="cmp.operator"
                :disabled="readonly"
                @update:model-value="(op) => onOperatorChange(index, cmp, op as ConditionOperator)"
              >
                <SelectTriggerPrimitive
                  :title="OPERATOR_LABELS[cmp.operator]"
                  class="text-muted-foreground hover:bg-accent hover:text-accent-foreground focus-visible:ring-ring/50 inline-flex size-8 shrink-0 items-center justify-center rounded-md border border-input bg-transparent outline-none transition-colors focus-visible:ring-3 disabled:pointer-events-none disabled:opacity-50 [&_svg]:size-4 [&_svg]:shrink-0"
                >
                  <component :is="OPERATOR_ICONS[cmp.operator]" />
                </SelectTriggerPrimitive>
                <SelectContent>
                  <SelectItem v-for="op in rowOperators[index]" :key="op" :value="op" class="text-xs">
                    <span class="flex items-center gap-2">
                      <component :is="OPERATOR_ICONS[op]" class="size-4 shrink-0 text-muted-foreground" />
                      {{ OPERATOR_LABELS[op] }}
                    </span>
                  </SelectItem>
                </SelectContent>
              </Select>
              <ConditionOperandEditor
                v-if="!isUnary(cmp.operator)"
                class="min-w-0 flex-1"
                :operand="cmp.right"
                :designer="designer"
                :readonly="readonly"
                :locked-data-type="rowRefTypes[index]"
                @change="(o) => onRightChange(index, cmp, o)"
              />
            </div>
          </div>
          <Button
            variant="ghost"
            size="icon"
            class="h-8 w-8 shrink-0"
            :disabled="readonly"
            @click="removeRule(index)"
          >
            <Trash2 :size="13" />
          </Button>
        </div>

        <!-- Nested subgroup (combineCondition) — recursive, indented, dashed. -->
        <div v-if="group.combineCondition" class="border-border rounded-md border border-dashed p-2">
          <div class="mb-1 flex justify-end">
            <Button variant="ghost" size="icon" class="h-6 w-6" :disabled="readonly" @click="removeCombine">
              <Trash2 :size="12" />
            </Button>
          </div>
          <ConditionGroupEditor
            :group="group.combineCondition"
            :designer="designer"
            :readonly="readonly"
            nested
            @change="(g) => patch({ combineCondition: g })"
          />
        </div>
      </div>
    </div>

    <!-- Action row aligned to the junction's left edge (image-2). -->
    <div class="flex items-center gap-1">
      <Button variant="outline" size="sm" class="h-7 text-xs" :disabled="readonly" @click="addRule">
        <Plus :size="12" class="mr-1" />添加规则
      </Button>
      <Button
        v-if="!group.combineCondition"
        variant="outline"
        size="sm"
        class="h-7 text-xs"
        :disabled="readonly"
        @click="addCombine"
      >
        <Plus :size="12" class="mr-1" />组合条件
      </Button>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from "vue"
import { SelectTrigger as SelectTriggerPrimitive } from "reka-ui"
import { Button } from "@/components/ui/button"
import { Select, SelectContent, SelectItem } from "@/components/ui/select"
import {
  AlignLeft,
  AlignRight,
  ArrowUpDown,
  ChevronLeft,
  ChevronRight,
  ChevronsLeft,
  ChevronsRight,
  CircleDashed,
  CircleDot,
  Equal,
  EqualNot,
  Plus,
  Search,
  SearchX,
  Trash2,
  type LucideIcon,
} from "@lucide/vue"
import type { WorkflowDesignerState } from "@/composables/useWorkflowDesigner"
import {
  OPERATORS_BY_DATATYPE,
  referenceDataTypeOf,
  type ConditionComparison,
  type ConditionDataType,
  type ConditionGroup,
  type ConditionOperator,
  type ConditionOperand,
} from "@/core/designer/conditionCompile"
import { emptyGroup } from "@/core/designer/conditionModel"
import ConditionOperandEditor from "./ConditionOperandEditor.vue"

/**
 * One condition group (recursive). Layout contract (ADR 0010, image-2 reference):
 * a left rail column holds the junction as a single-char + swap-icon toggle
 * button (且/或), vertically centred with a vertical rail above and below it;
 * the comparison rows and the indented dashed subgroup sit to the rail's right;
 * the [添加规则][组合条件] buttons sit at the bottom aligned to the junction's
 * left edge. Controlled: emits a new group; owns no command logic.
 */
defineOptions({ name: "ConditionGroupEditor" })

const props = defineProps<{ group: ConditionGroup; designer: WorkflowDesignerState; readonly?: boolean; nested?: boolean }>()
const emit = defineEmits<{ change: [ConditionGroup] }>()

const OPERATOR_LABELS: Record<ConditionOperator, string> = {
  "=": "等于",
  "!=": "不等于",
  ">": "大于",
  ">=": "大于等于",
  "<": "小于",
  "<=": "小于等于",
  contains: "包含",
  notContains: "不包含",
  startsWith: "开头是",
  endsWith: "结尾是",
  empty: "为空",
  notEmpty: "不为空",
}

function isUnary(op: ConditionOperator): boolean {
  return op === "empty" || op === "notEmpty"
}

// Icon per operator so the trigger is a compact 32px button (frees row width);
// the dropdown still spells each operator out as icon + label. Strict vs
// "or equal" reads as single vs double chevron.
const OPERATOR_ICONS: Record<ConditionOperator, LucideIcon> = {
  "=": Equal,
  "!=": EqualNot,
  ">": ChevronRight,
  ">=": ChevronsRight,
  "<": ChevronLeft,
  "<=": ChevronsLeft,
  contains: Search,
  notContains: SearchX,
  startsWith: AlignLeft,
  endsWith: AlignRight,
  empty: CircleDashed,
  notEmpty: CircleDot,
}

/** The comparison's effective dataType: referenced definition type (left then
 *  right) → right literal → left literal → string (ADR 0010). */
function dataTypeOf(cmp: ConditionComparison): ConditionDataType {
  return (
    referenceTypeOf(cmp.left) ??
    referenceTypeOf(cmp.right) ??
    (cmp.right?.type === "Literal" ? cmp.right.dataType : undefined) ??
    (cmp.left?.type === "Literal" ? cmp.left.dataType : undefined) ??
    "string"
  )
}

function referenceTypeOf(operand: ConditionOperand | undefined): ConditionDataType | undefined {
  return referenceDataTypeOf(operand, props.designer.variables.value, props.designer.inputs.value)
}

function operatorsFor(cmp: ConditionComparison): ConditionOperator[] {
  return OPERATORS_BY_DATATYPE[dataTypeOf(cmp)]
}

function patch(p: Partial<ConditionGroup>): void {
  emit("change", { ...props.group, ...p })
}

function updateComparison(index: number, next: ConditionComparison): void {
  patch({ conditions: props.group.conditions.map((c, i) => (i === index ? next : c)) })
}

function onLeftChange(index: number, cmp: ConditionComparison, left: ConditionOperand): void {
  const next: ConditionComparison = { ...cmp, left }
  if (!operatorsFor(next).includes(next.operator)) next.operator = "="
  updateComparison(index, next)
}

function onRightChange(index: number, cmp: ConditionComparison, right: ConditionOperand): void {
  const next: ConditionComparison = { ...cmp, right }
  if (!operatorsFor(next).includes(next.operator)) next.operator = "="
  updateComparison(index, next)
}

function onOperatorChange(index: number, cmp: ConditionComparison, operator: ConditionOperator): void {
  updateComparison(index, { ...cmp, operator })
}

function addRule(): void {
  const blank: ConditionComparison = {
    left: { type: "Literal", value: "", dataType: "string" },
    operator: "=",
    right: { type: "Literal", value: "", dataType: "string" },
  }
  patch({ conditions: [...props.group.conditions, blank] })
}

function removeRule(index: number): void {
  patch({ conditions: props.group.conditions.filter((_, i) => i !== index) })
}

function addCombine(): void {
  patch({ combineCondition: emptyGroup() })
}

function removeCombine(): void {
  patch({ combineCondition: undefined })
}

// The builder tree is plain (non-reactive) objects and ExpressionEditor owns
// name/type edits through its own commands, so every model mutation only
// surfaces through the designer revision. Each derived list reads revision
// DIRECTLY: a shared computed returning the same array reference would not
// invalidate its dependents (Vue compares computed values by identity), which
// would leave operator sets stale after name/type edits (ADR 0010).
const conditions = computed(() => {
  void props.designer.revision.value
  return [...(props.group.conditions ?? [])]
})

/** Per-row referenced definition type; pins Literal operands when present. */
const rowRefTypes = computed(() => {
  void props.designer.revision.value
  return (props.group.conditions ?? []).map((cmp) => referenceTypeOf(cmp.left) ?? referenceTypeOf(cmp.right))
})

const rowOperators = computed(() => {
  void props.designer.revision.value
  return (props.group.conditions ?? []).map((cmp) => operatorsFor(cmp))
})

function toggleJunction(): void {
  patch({ conditionType: props.group.conditionType === "and" ? "or" : "and" })
}
</script>
