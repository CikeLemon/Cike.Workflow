<template>
  <ExpressionEditor :expression="condition" :designer="designer" label="循环条件" :literal-default="false">
    <template #default="{ value, commit, readonly }">
      <div class="flex h-8 items-center gap-2">
        <Switch :model-value="value === true" :disabled="readonly" @update:model-value="(v: boolean) => commit(v)" />
        <span class="text-xs text-muted-foreground">{{ value === true ? "True" : "False" }}</span>
      </div>
    </template>
  </ExpressionEditor>
</template>

<script setup lang="ts">
import { computed } from "vue"
import { Switch } from "@/components/ui/switch"
import type { WorkflowDesignerState } from "@/composables/useWorkflowDesigner"
import type { ExpressionLike } from "@/core/designer/expression"
import ExpressionEditor from "../ExpressionEditor.vue"

const props = defineProps<{ activity: unknown; designer: WorkflowDesignerState }>()

const condition = computed<ExpressionLike>(
  () => (props.activity as unknown as { condition: { expression: ExpressionLike } }).condition.expression,
)
</script>
