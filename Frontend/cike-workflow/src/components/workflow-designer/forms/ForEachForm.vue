<template>
  <ExpressionEditor :expression="items" :designer="designer" label="迭代集合" :literal-default="[]">
    <template #default="{ value, commit, readonly }">
      <div class="space-y-1">
        <CodeLiteralEditor
          :value="value"
          :readonly="readonly"
          @blur="(raw) => { const to = coerceLiteralValue(value, raw); if (to !== undefined) commit(to) }"
        />
        <div class="text-[10px] text-muted-foreground">JSON 数组，如 [1, 2, 3] 或表达式返回的集合</div>
      </div>
    </template>
  </ExpressionEditor>
</template>

<script setup lang="ts">
import { computed } from "vue"
import type { WorkflowDesignerState } from "@/composables/useWorkflowDesigner"
import type { ExpressionLike } from "@/core/designer/expression"
import { coerceLiteralValue } from "@/core/designer/form"
import CodeLiteralEditor from "./CodeLiteralEditor.vue"
import ExpressionEditor from "../ExpressionEditor.vue"

const props = defineProps<{ activity: unknown; designer: WorkflowDesignerState }>()

const items = computed<ExpressionLike>(
  () => (props.activity as unknown as { items: { expression: ExpressionLike } }).items.expression,
)
</script>
