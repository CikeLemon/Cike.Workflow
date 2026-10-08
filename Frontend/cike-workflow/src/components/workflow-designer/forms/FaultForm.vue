<template>
  <div class="space-y-2">
    <ExpressionEditor
      v-for="item in FIELDS"
      :key="item.key"
      :expression="expr(item.key)"
      :designer="designer"
      :label="item.label"
      literal-default=""
    >
      <template #default="{ value, commit }">
        <UiInput
          :model-value="value == null ? '' : String(value)"
          @change="(e: Event) => { const raw = (e.target as HTMLInputElement).value; commit(raw === '' ? null : raw) }"
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
import ExpressionEditor from "../ExpressionEditor.vue"

const props = defineProps<{ activity: unknown; designer: WorkflowDesignerState }>()

const FIELDS: Array<{ key: string; label: string }> = [
  { key: "faultCode", label: "故障码" },
  { key: "category", label: "类别" },
  { key: "faultType", label: "故障类型" },
  { key: "message", label: "消息" },
]

const activity = computed(() => props.activity as unknown as Record<string, { expression: ExpressionLike }>)

function expr(key: string): ExpressionLike {
  return activity.value[key].expression
}
</script>
