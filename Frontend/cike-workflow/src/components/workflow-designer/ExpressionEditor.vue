<template>
  <div :class="dense ? 'space-y-0.5' : 'space-y-1'">
    <Label v-if="label" :class="dense ? 'text-[10px]' : 'text-xs'" :title="description ?? undefined">{{ label }}</Label>

    <div class="flex items-start gap-2">
      <div class="min-w-0 flex-1">
        <!-- Literal: the caller-provided value editor (only the node knows T). -->
        <slot v-if="isLiteral(currentType)" :value="currentValue" :commit="commitValue" :readonly="isReadonly" />

        <!-- JavaScript / Liquid: Monaco, committed on blur. -->
        <MonacoEditor
          v-else-if="isMonacoType"
          v-model="draft"
          :language="languageFor(currentType)"
          :readonly="isReadonly"
          @blur="commitDraft"
        />

        <!-- Variable / WorkflowInput: reference a definition by name. -->
        <Select
          v-else-if="isNameType"
          :model-value="nameValue"
          :disabled="isReadonly"
          @update:model-value="(n) => commitValue(String(n))"
        >
          <SelectTrigger size="sm" class="w-full text-xs">
            <SelectValue class="block! min-w-0 truncate" :placeholder="nameOptions.length ? '选择…' : '（暂无可选项）'" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem v-for="name in nameOptions" :key="name" :value="name" class="text-xs">{{ name }}</SelectItem>
          </SelectContent>
        </Select>

        <!-- Any other string-valued type: plain text fallback. -->
        <UiInput
          v-else
          class="font-mono text-xs"
          :model-value="draft"
          :disabled="isReadonly"
          @input="(e: Event) => (draft = (e.target as HTMLInputElement).value)"
          @blur="commitDraft"
        />
      </div>

      <!-- Type switcher: icon-only trigger opening a single-select picker (ADR 0007). -->
      <Select
        v-if="!hideTypeSwitcher"
        :model-value="currentType"
        :disabled="isReadonly"
        @update:model-value="(t) => onTypeChange(String(t))"
      >
        <SelectTriggerPrimitive
          :title="currentTypeName"
          class="text-muted-foreground hover:bg-accent hover:text-accent-foreground focus-visible:ring-ring/50 inline-flex size-8 shrink-0 items-center justify-center rounded-md outline-none transition-colors focus-visible:ring-3 disabled:pointer-events-none disabled:opacity-50 [&_svg]:size-4 [&_svg]:shrink-0"
        >
          <component :is="currentIcon" />
        </SelectTriggerPrimitive>
        <SelectContent>
          <SelectItem v-for="opt in typeOptions" :key="opt.type" :value="opt.type" class="text-xs">
            <span class="flex items-center gap-2">
              <component :is="resolveExpressionIcon(opt.icon)" class="size-4 shrink-0 text-muted-foreground" />
              {{ opt.displayName }}
            </span>
          </SelectItem>
        </SelectContent>
      </Select>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, ref, watch } from "vue"
import { SelectTrigger as SelectTriggerPrimitive } from "reka-ui"
import { Input as UiInput } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import type { WorkflowDesignerState } from "@/composables/useWorkflowDesigner"
import { makeEditPropertyCommand } from "@/core/designer/commands"
import { LITERAL_TYPE, isLiteral, makeSwitchExpressionTypeCommand, type ExpressionLike } from "@/core/designer/expression"
import { resolveExpressionIcon } from "./expressionIcons"
import MonacoEditor from "./MonacoEditor.vue"

/**
 * Unified editor for one Input's Expression (ADR 0004). It owns the type
 * selector (backend ExpressionDescriptors-driven), the four non-Literal editors,
 * and every model write through the designer command stack. The caller supplies
 * only the Literal value editor via the default slot, because only the node
 * knows the Input's target type.
 */
const props = defineProps<{
  expression: ExpressionLike
  designer: WorkflowDesignerState
  label?: string
  description?: string | null
  /** Concrete zero/default value used when switching *to* Literal (ADR 0005). */
  literalDefault?: unknown
  readonly?: boolean
  /** Whitelist of selectable types; the current type always stays visible
   *  even when excluded, so a stored value never becomes unselectable. */
  allowedTypes?: string[]
  /** Suppress the built-in icon type switcher, for callers that own the
   *  top-level type choice themselves (e.g. ConditionEditor's escape hatch). */
  hideTypeSwitcher?: boolean
  /** Compact label/spacing variant for dense panels (WorkflowConfigPanel);
   *  the value editor and type switcher keep the standard h-8 row height. */
  dense?: boolean
}>()

const isReadonly = computed(() => props.readonly ?? props.designer.readonly.value)

// Reading `revision` makes the editor re-render on any model mutation (including
// undo/redo and type switches), since the domain model lives in shallowRefs and
// nested plain objects are not reactive on their own.
const currentType = computed(() => {
  void props.designer.revision.value
  return props.expression.type || LITERAL_TYPE
})

const currentValue = computed(() => {
  void props.designer.revision.value
  return props.expression.value
})

// Fallback display names for the five known types, used when the backend
// descriptor list has not loaded or omits a type the model already references.
const KNOWN_TYPE_NAMES: Record<string, string> = {
  Literal: "字面量",
  Javascript: "JavaScript 表达式",
  Liquid: "Liquid 表达式",
  Variable: "变量",
  // The backend registers the workflow-input expression under Type "Input"
  // (DisplayName "输入参数"); "WorkflowInput" is kept as a legacy alias.
  Input: "输入参数",
  WorkflowInput: "输入参数",
}

// Backend-registered types; a Literal-only fallback covers the pre-fetch moment.
// Each option carries the backend icon name (lucide) resolved for the trigger
// and menu items.
const typeOptions = computed(() => {
  const list = props.designer.expressionDescriptors.value
  const allowed = props.allowedTypes
  const options =
    list.length === 0
      ? [{ type: LITERAL_TYPE, displayName: KNOWN_TYPE_NAMES[LITERAL_TYPE], icon: "type" as string | null }]
      : list
          .filter((d): d is { type: string; displayName?: string; icon?: string | null } => !!d.type)
          .filter((d) => !allowed || allowed.includes(d.type as string))
          .map((d) => ({
            type: d.type as string,
            displayName: d.displayName ?? KNOWN_TYPE_NAMES[d.type as string] ?? d.type,
            icon: d.icon ?? null,
          }))
  // A filtered list may drop Literal while the value is still concrete:
  // keep the Literal editor usable as the fallback for the default slot.
  if (allowed && !allowed.includes(LITERAL_TYPE) && !options.some((o) => o.type === LITERAL_TYPE)) {
    options.unshift({ type: LITERAL_TYPE, displayName: KNOWN_TYPE_NAMES[LITERAL_TYPE], icon: "type" })
  }
  // Always keep the current type visible/selectable even if the backend list
  // does not include it (e.g. a saved Variable input), otherwise the trigger
  // shows no icon and the type cannot be re-selected.
  const current = currentType.value
  if (current && !options.some((o) => o.type === current)) {
    options.push({ type: current, displayName: KNOWN_TYPE_NAMES[current] ?? current, icon: null })
  }
  return options
})

// Current type's descriptor projection, for the trigger icon and its title.
const currentOption = computed(() => typeOptions.value.find((o) => o.type === currentType.value))
const currentIcon = computed(() => resolveExpressionIcon(currentOption.value?.icon))
const currentTypeName = computed(() => currentOption.value?.displayName ?? currentType.value)

const isMonacoType = computed(() => currentType.value === "Javascript" || currentType.value === "Liquid")
// Name-referencing types resolve to a dropdown of definition names. The backend
// registers the workflow-input type as "Input"; "WorkflowInput" is a legacy alias.
const isNameType = computed(() =>
  currentType.value === "Variable" || currentType.value === "Input" || currentType.value === "WorkflowInput",
)

function languageFor(type: string): string {
  if (type === "Javascript") return "javascript"
  if (type === "Liquid") return "liquid"
  return "plaintext"
}

const variableNames = computed(() =>
  props.designer.variables.value.map((v) => v.name ?? "").filter((n) => n.length > 0),
)
const inputNames = computed(() =>
  props.designer.inputs.value.map((i) => i.name ?? "").filter((n) => n.length > 0),
)
const nameOptions = computed(() => (currentType.value === "Variable" ? variableNames.value : inputNames.value))
const nameValue = computed(() => (typeof currentValue.value === "string" ? currentValue.value : ""))

// Local draft for text editors: commit on blur so undo history is one step per
// edit session, not one per keystroke.
const draft = ref("")
watch(
  [currentValue, currentType],
  () => {
    draft.value = typeof currentValue.value === "string" ? currentValue.value : ""
  },
  { immediate: true },
)

function commitValue(to: unknown): void {
  if (isReadonly.value) return
  const from = props.expression.value
  if (from === to) return
  props.designer.executeCommand(
    makeEditPropertyCommand(props.expression as unknown as Record<string, unknown>, "value", from, to),
  )
}

function commitDraft(): void {
  commitValue(draft.value)
}

function onTypeChange(type: string): void {
  if (type === currentType.value) return
  props.designer.executeCommand(
    makeSwitchExpressionTypeCommand(props.expression, type, props.literalDefault),
  )
}
</script>
