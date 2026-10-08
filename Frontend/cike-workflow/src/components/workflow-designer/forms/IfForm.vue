<template>
  <ConditionEditor
    :spec="spec"
    :designer="designer"
    :readonly="designer.readonly.value"
    label="条件"
    @change="onChange"
  />
</template>

<script setup lang="ts">
import { computed, watch } from "vue"
import type { WorkflowDesignerState } from "@/composables/useWorkflowDesigner"
import type { IActivity } from "@/core/abstracts/Activity"
import { compileCondition, type ConditionSpec } from "@/core/designer/conditionCompile"
import { readIfCondition, type CustomExpressionContainer } from "@/core/designer/conditionModel"
import { makeEditPropertyCommand } from "@/core/designer/commands"
import { makeSetExpressionCommand } from "@/core/designer/expression"
import ConditionEditor from "../ConditionEditor.vue"

const props = defineProps<{ activity: unknown; designer: WorkflowDesignerState }>()

interface IfActivity extends IActivity {
  condition: { expression: { type: string; value?: unknown } }
}

const act = computed(() => props.activity as IfActivity)

// Reading revision re-runs this projection after any command / undo (the model
// lives in shallowRefs, so nested plain objects are not reactive on their own).
const spec = computed<ConditionSpec>(() => {
  void props.designer.revision.value
  return readIfCondition(act.value)
})

function containerOf(activity: IfActivity): CustomExpressionContainer | null {
  return (activity.customProperties as Record<string, unknown>)["customExpression"] as CustomExpressionContainer | null
}

/** Structural edits (entering/leaving custom, group rebuilds) write the editing truth only. */
function onChange(next: ConditionSpec): void {
  const activity = act.value
  const customProperties = activity.customProperties as Record<string, unknown>
  const fromContainer = customProperties["customExpression"] ?? null
  props.designer.executeCommand(
    makeEditPropertyCommand(customProperties, "customExpression", fromContainer, {
      ...(containerOf(activity) ?? {}),
      ifCondition: next,
    }),
  )
}

/**
 * The compiled wire expression is a pure function of the spec (ADR 0010), so it is a
 * derived projection: on every revision, if the container exists, push the compiled
 * value into If.condition.expression without an undo entry (apply, not execute) —
 * undoing a spec edit re-derives it automatically. Container-gated so a legacy
 * definition's seeded empty builder never overwrites its stored condition.
 */
watch(
  () => props.designer.revision.value,
  () => {
    const activity = act.value
    const container = containerOf(activity)
    if (!container?.ifCondition) return
    const compiled = compileCondition(container.ifCondition, props.designer.variables.value, props.designer.inputs.value)
    const target = activity.condition.expression
    if (target.type !== compiled.type || target.value !== compiled.value) {
      makeSetExpressionCommand(target, compiled).apply()
    }
  },
  { immediate: true },
)
</script>
