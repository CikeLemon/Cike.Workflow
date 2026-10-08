<template>
  <div class="space-y-2">
    <ExpressionEditor :expression="expr('script')" :designer="designer" label="脚本" :allowed-types="SCRIPT_ALLOWED_TYPES" literal-default="">
      <template #default="{ value, commit, readonly }">
        <CodeLiteralEditor
          language="javascript"
          :value="value"
          :readonly="readonly"
          title="脚本"
          @blur="(raw) => { const to = coerceLiteralValue(value, raw); if (to !== undefined) commit(to) }"
        />
      </template>
    </ExpressionEditor>
    <ExpressionEditor :expression="expr('possibleOutcomes')" :designer="designer" label="可能出端口（逗号分隔）" :literal-default="[]">
      <template #default="{ value, commit }">
        <UiInput
          :model-value="outcomesText(value)"
          @change="(e: Event) => commit(parseOutcomes((e.target as HTMLInputElement).value))"
        />
      </template>
    </ExpressionEditor>
  </div>
</template>

<script setup lang="ts">
import { computed } from "vue"
import { Input as UiInput } from "@/components/ui/input"
import type { WorkflowDesignerState } from "@/composables/useWorkflowDesigner"
import type { ExpressionLike } from "@/core/designer/expression"
import { coerceLiteralValue } from "@/core/designer/form"
import CodeLiteralEditor from "./CodeLiteralEditor.vue"
import ExpressionEditor from "../ExpressionEditor.vue"

const props = defineProps<{ activity: unknown; designer: WorkflowDesignerState }>()

// The script body is already edited as a Literal code block; wrapping it in a
// Liquid/JavaScript expression is meaningless here, so the type switcher only
// offers concrete values and name references.
const SCRIPT_ALLOWED_TYPES = ["Literal", "Variable", "Input"]

const activity = computed(() => props.activity as unknown as Record<string, { expression: ExpressionLike }>)

function expr(key: string): ExpressionLike {
  return activity.value[key].expression
}

function outcomesText(value: unknown): string {
  return Array.isArray(value) ? value.join(", ") : ""
}

function parseOutcomes(raw: string): string[] {
  return raw.split(",").map((entry) => entry.trim()).filter(Boolean)
}
</script>
