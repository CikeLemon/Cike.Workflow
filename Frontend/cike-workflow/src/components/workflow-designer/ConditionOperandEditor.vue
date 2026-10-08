<template>
  <ExpressionEditor
    :expression="operand"
    :designer="designer"
    :allowed-types="OPERAND_TYPES"
    :literal-default="defaultLiteralValue(dataType)"
    :readonly="readonly"
  >
    <template #default="{ value, commit, readonly: ro }">
      <div class="flex min-w-0 items-center gap-1">
        <Select
          :model-value="dataType"
          :disabled="ro || lockedDataType != null"
          @update:model-value="(d) => setDataType(d as ConditionDataType)"
        >
          <SelectTrigger size="sm" class="h-8 w-28 shrink-0 text-xs">
            <SelectValue class="block! min-w-0 truncate" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="string" class="text-xs">字符串</SelectItem>
            <SelectItem value="number" class="text-xs">数字</SelectItem>
            <SelectItem value="boolean" class="text-xs">布尔</SelectItem>
            <SelectItem value="datetime" class="text-xs">日期时间</SelectItem>
          </SelectContent>
        </Select>

        <div v-if="dataType === 'boolean'" class="flex h-8 items-center gap-2">
          <Switch :model-value="value === true" :disabled="ro" @update:model-value="(v: boolean) => commit(v)" />
          <span class="text-xs text-muted-foreground">{{ value === true ? "True" : "False" }}</span>
        </div>
        <UiInput
          v-else-if="dataType === 'number'"
          class="h-8 min-w-0 flex-1 text-xs"
          type="number"
          :model-value="value == null ? '' : String(value)"
          :disabled="ro"
          @change="(e: Event) => commit(Number((e.target as HTMLInputElement).value))"
        />
        <UiInput
          v-else-if="dataType === 'datetime'"
          class="h-8 min-w-0 flex-1 text-xs"
          type="datetime-local"
          :model-value="value == null ? '' : String(value)"
          :disabled="ro"
          @change="(e: Event) => commit((e.target as HTMLInputElement).value)"
        />
        <UiInput
          v-else
          class="h-8 min-w-0 flex-1 text-xs"
          :model-value="value == null ? '' : String(value)"
          :disabled="ro"
          @change="(e: Event) => commit((e.target as HTMLInputElement).value)"
        />
      </div>
    </template>
  </ExpressionEditor>
</template>

<script setup lang="ts">
import { computed } from "vue"
import { Input as UiInput } from "@/components/ui/input"
import { Switch } from "@/components/ui/switch"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import type { WorkflowDesignerState } from "@/composables/useWorkflowDesigner"
import {
  defaultLiteralValue,
  withConditionDataType,
  type ConditionDataType,
  type ConditionOperand,
} from "@/core/designer/conditionCompile"
import ExpressionEditor from "./ExpressionEditor.vue"

/**
 * One operand (left or right of a comparison). Reuses the unified ExpressionEditor
 * (ADR 0010) with the operand whitelist Literal / Variable / Input; the Literal value
 * widget is dataType-driven and supplied through ExpressionEditor's Literal slot.
 * value/type edits are owned by ExpressionEditor (it writes through the command
 * stack); dataType is operand-local metadata ExpressionEditor doesn't know, so a
 * dataType change emits a controlled change up to the group editor.
 * lockedDataType: the group editor pins the Literal's type to a sibling
 * reference's definition type; the select then only displays it.
 */
const props = defineProps<{
  operand: ConditionOperand
  designer: WorkflowDesignerState
  readonly?: boolean
  lockedDataType?: ConditionDataType
}>()
const emit = defineEmits<{ change: [ConditionOperand] }>()

const OPERAND_TYPES = ["Literal", "Variable", "Input"]

const dataType = computed<ConditionDataType>(() => props.lockedDataType ?? props.operand.dataType ?? "string")

function setDataType(dt: ConditionDataType): void {
  emit("change", withConditionDataType(props.operand, dt))
}
</script>
