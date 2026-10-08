<template>
  <div class="space-y-2">
    <ExpressionEditor :expression="expr('start')" :designer="designer" label="起始值" :literal-default="0">
      <template #default="{ value, commit }">
        <UiInput type="number" :model-value="num(value)" @change="(e: Event) => commit(toNumber((e.target as HTMLInputElement).value))" />
      </template>
    </ExpressionEditor>
    <ExpressionEditor :expression="expr('end')" :designer="designer" label="结束值" :literal-default="0">
      <template #default="{ value, commit }">
        <UiInput type="number" :model-value="num(value)" @change="(e: Event) => commit(toNumber((e.target as HTMLInputElement).value))" />
      </template>
    </ExpressionEditor>
    <ExpressionEditor :expression="expr('step')" :designer="designer" label="步长" :literal-default="1">
      <template #default="{ value, commit }">
        <UiInput type="number" :model-value="num(value)" @change="(e: Event) => commit(toNumber((e.target as HTMLInputElement).value))" />
      </template>
    </ExpressionEditor>
    <ExpressionEditor :expression="expr('outerBoundInclusive')" :designer="designer" label="含右边界" :literal-default="false">
      <template #default="{ value, commit, readonly }">
        <div class="flex h-8 items-center">
          <Switch :model-value="value === true" :disabled="readonly" @update:model-value="(v: boolean) => commit(v)" />
        </div>
      </template>
    </ExpressionEditor>
  </div>
</template>

<script setup lang="ts">
import { computed } from "vue"
import { Input as UiInput } from "@/components/ui/input"
import { Switch } from "@/components/ui/switch"
import type { WorkflowDesignerState } from "@/composables/useWorkflowDesigner"
import type { ExpressionLike } from "@/core/designer/expression"
import ExpressionEditor from "../ExpressionEditor.vue"

const props = defineProps<{ activity: unknown; designer: WorkflowDesignerState }>()

const fields = computed(() => props.activity as unknown as Record<string, { expression: ExpressionLike }>)

function expr(key: string): ExpressionLike {
  return fields.value[key].expression
}

function num(value: unknown): string {
  return value == null ? "" : String(value)
}

function toNumber(raw: string): unknown {
  return raw === "" ? null : Number(raw)
}
</script>
