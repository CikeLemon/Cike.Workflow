<template>
  <div class="space-y-1.5">
    <Label v-if="label" class="text-xs">{{ label }}</Label>

    <div class="flex items-start gap-2">
      <div class="min-w-0 flex-1">
        <ConditionGroupEditor
          v-if="spec.type === 'custom'"
          :group="(spec.value as ConditionGroup) ?? emptyGroup()"
          :designer="designer"
          :readonly="readonly"
          @change="onGroupChange"
        />

        <ExpressionEditor
          v-else
          :expression="spec"
          :designer="designer"
          :allowed-types="ESCAPE_TYPES"
          :literal-default="false"
          :readonly="readonly"
          hide-type-switcher
        >
          <template #default="{ value, commit, readonly: ro }">
            <div class="flex h-8 items-center gap-2">
              <Switch :model-value="value === true" :disabled="ro" @update:model-value="(v: boolean) => commit(v)" />
              <span class="text-xs text-muted-foreground">{{ value === true ? "True" : "False" }}</span>
            </div>
          </template>
        </ExpressionEditor>
      </div>

      <!-- Top-level type switcher: ExpressionEditor-styled icon button at the trailing end. -->
      <Select
        :model-value="spec.type"
        :disabled="readonly"
        @update:model-value="(t) => onTypeChange(t as ConditionSpec['type'])"
      >
        <SelectTriggerPrimitive
          :title="currentOption.label"
          class="text-muted-foreground hover:bg-accent hover:text-accent-foreground focus-visible:ring-ring/50 inline-flex size-8 shrink-0 items-center justify-center rounded-md outline-none transition-colors focus-visible:ring-3 disabled:pointer-events-none disabled:opacity-50 [&_svg]:size-4 [&_svg]:shrink-0"
        >
          <component :is="currentOption.icon" />
        </SelectTriggerPrimitive>
        <SelectContent>
          <SelectItem v-for="opt in TYPE_OPTIONS" :key="opt.type" :value="opt.type" class="text-xs">
            <span class="flex items-center gap-2">
              <component :is="opt.icon" class="size-4 shrink-0 text-muted-foreground" />
              {{ opt.label }}
            </span>
          </SelectItem>
        </SelectContent>
      </Select>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, type Component } from "vue"
import { SelectTrigger as SelectTriggerPrimitive } from "reka-ui"
import { Code, Droplet, ListTree, Type as TypeIcon } from "@lucide/vue"
import { Label } from "@/components/ui/label"
import { Switch } from "@/components/ui/switch"
import { Select, SelectContent, SelectItem } from "@/components/ui/select"
import type { WorkflowDesignerState } from "@/composables/useWorkflowDesigner"
import { compileGroup, type ConditionGroup, type ConditionSpec } from "@/core/designer/conditionCompile"
import { emptyGroup } from "@/core/designer/conditionModel"
import ConditionGroupEditor from "./ConditionGroupEditor.vue"
import ExpressionEditor from "./ExpressionEditor.vue"

/**
 * Top-level editor for one condition (If's condition / one Switch case).
 * The stored discriminator `type` decides the editor in a single read (ADR 0010):
 *   custom    → the visual builder (ConditionGroupEditor)
 *   otherwise → the spec itself is handed to the unified ExpressionEditor as an
 *               escape hatch, whitelisted to Literal / Javascript / Liquid, with
 *               the Literal slot rendering a true/false switch.
 * The top select is the custom gate (default custom); ExpressionEditor's icon
 * switcher moves among the three escape-hatch types. Structural edits (entering /
 * leaving custom) emit a controlled change; in-place value/type edits are owned by
 * ExpressionEditor. The compiled wire expression is a derived projection synced by
 * the owning form on revision (ADR 0010), not written here.
 */
const props = defineProps<{
  spec: ConditionSpec
  designer: WorkflowDesignerState
  readonly?: boolean
  label?: string
}>()
const emit = defineEmits<{ change: [ConditionSpec] }>()

// Icon-styled options mirroring ExpressionEditor's switcher (ADR 0007): custom
// gets a tree icon; the three escape types reuse the same lucide icons
// ExpressionEditor resolves (type / code / droplet) so both switchers agree.
const TYPE_OPTIONS: { type: ConditionSpec["type"]; label: string; icon: Component }[] = [
  { type: "custom", label: "可视化", icon: ListTree },
  { type: "Literal", label: "字面量", icon: TypeIcon },
  { type: "Javascript", label: "JavaScript", icon: Code },
  { type: "Liquid", label: "Liquid", icon: Droplet },
]

const currentOption = computed(() => TYPE_OPTIONS.find((o) => o.type === props.spec.type) ?? TYPE_OPTIONS[0])

const ESCAPE_TYPES = ["Literal", "Javascript", "Liquid"]

function onTypeChange(target: ConditionSpec["type"]): void {
  if (target === props.spec.type) return
  const current = props.spec
  let value: ConditionSpec["value"]
  if (target === "custom") {
    value = current.type === "custom" ? (current.value as ConditionGroup) : emptyGroup()
  } else if (target === "Literal") {
    value = current.type === "Literal" ? current.value === true : false
  } else if (target === "Javascript") {
    // Leaving the builder compiles the tree to code so semantics survive the switch.
    value = current.type === "custom" ? compileGroup(current.value as ConditionGroup) : typeof current.value === "string" ? current.value : ""
  } else {
    value = typeof current.value === "string" ? current.value : ""
  }
  emit("change", { type: target, value })
}

function onGroupChange(group: ConditionGroup): void {
  emit("change", { type: "custom", value: group })
}
</script>
