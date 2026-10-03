<script setup lang="ts">
import { computed } from "vue"
import { X } from "@lucide/vue"
import { Label } from "@/components/ui/label"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import type { WorkflowDesignerState } from "@/composables/useWorkflowDesigner"
import type { IActivity } from "@/core/abstracts/Activity"
import type { OutputDescriptor } from "@/api/generated"
import { makeEditPropertyCommand } from "@/core/designer/commands"
import {
  findVariableById,
  makeOutputBinding,
  resolveOutputFields,
  type ResolvedOutputField,
} from "@/core/designer/form"

/**
 * Node output-binding section (CONTEXT.md「输出绑定」): lists the activity's
 * browsable outputs, each with a variable picker. Binding aligns the output's
 * memory block reference id to the variable's stable id (rename-safe); the clear
 * (X) button resets it back to null. Every write flows through the designer
 * command stack, so it participates in undo/redo and debounced validation.
 */
const props = defineProps<{
  activity: IActivity
  designer: WorkflowDesignerState
  descriptors: OutputDescriptor[]
}>()

const isReadonly = computed(() => props.designer.readonly.value)

// Reading `revision` re-runs resolution on any model mutation (bind/unbind,
// undo/redo), since the domain model lives in shallowRefs whose nested plain
// objects are not reactive on their own (same pattern as ExpressionEditor).
const fields = computed<ResolvedOutputField[]>(() => {
  void props.designer.revision.value
  return resolveOutputFields(props.activity, props.descriptors)
})

/** Variables with a stable id, projected to the { id, name } the picker needs. */
const bindableVariables = computed(() =>
  props.designer.variables.value.flatMap((variable) =>
    variable.id ? [{ id: variable.id, name: variable.name ?? "" }] : [],
  ),
)

/** Current Select value: the bound variable id, or "" so the trigger renders the
 *  「未绑定」placeholder (spec stories 8/11). reka-ui only shows the placeholder
 *  for an empty model-value — same pattern as the merge-mode Select. A dangling
 *  id also falls through to the placeholder, since it matches no item. */
function selectValue(field: ResolvedOutputField): string {
  return field.output?.memoryBlockReference.id ?? ""
}

/** A stored id matching no current variable (deleted, or a stale draft). */
function isDangling(field: ResolvedOutputField): boolean {
  const id = field.output?.memoryBlockReference.id
  return id != null && findVariableById(props.designer.variables.value, id) === undefined
}

/** Binds the output to the picked variable (the picker lists variables only). */
function onBind(field: ResolvedOutputField, variableId: string): void {
  if (isReadonly.value) return
  const from = field.output
  if (from?.memoryBlockReference.id === variableId) return
  props.designer.executeCommand(
    makeEditPropertyCommand(
      props.activity as unknown as Record<string, unknown>,
      field.field,
      from,
      makeOutputBinding(variableId),
    ),
  )
}

/** Clears the binding back to null. form.ts keeps a deleted output listed as
 *  unbound, so the row stays visible and can be re-bound (spec stories 7–8). */
function onClear(field: ResolvedOutputField): void {
  if (isReadonly.value) return
  const from = field.output
  if (!from) return
  props.designer.executeCommand(
    makeEditPropertyCommand(
      props.activity as unknown as Record<string, unknown>,
      field.field,
      from,
      null,
    ),
  )
}
</script>

<template>
  <div v-if="fields.length" class="space-y-2">
    <div class="space-y-0.5">
      <div class="text-xs font-medium text-muted-foreground">输出属性</div>
      <div class="text-[10px] text-muted-foreground">绑定到变量后，产出值写入该变量</div>
    </div>
    <div class="space-y-3">
      <div v-for="field in fields" :key="field.field" class="space-y-1">
        <Label class="text-xs" :title="field.description ?? undefined">{{ field.label }}</Label>
        <div class="relative">
          <Select
            :model-value="selectValue(field)"
            :disabled="isReadonly"
            @update:model-value="(value) => onBind(field, String(value))"
          >
            <SelectTrigger size="sm" class="w-full text-xs">
              <SelectValue
                class="block! min-w-0 truncate"
                :class="field.output ? 'mr-6' : ''"
                :placeholder="bindableVariables.length ? '未绑定' : '（暂无变量）'"
              />
            </SelectTrigger>
            <SelectContent>
              <SelectItem v-for="variable in bindableVariables" :key="variable.id" :value="variable.id" class="text-xs">
                {{ variable.name || "（未命名）" }}
              </SelectItem>
            </SelectContent>
          </Select>
          <button
            v-if="field.output"
            type="button"
            class="absolute top-1/2 right-8 flex size-6 -translate-y-1/2 items-center justify-center rounded-sm text-muted-foreground transition-colors hover:text-foreground disabled:pointer-events-none disabled:opacity-50"
            :disabled="isReadonly"
            title="清除绑定"
            @click.stop="onClear(field)"
          >
            <X class="size-3.5" />
          </button>
        </div>
        <div v-if="isDangling(field)" class="text-[10px] text-muted-foreground">原引用的变量已不存在</div>
      </div>
    </div>
  </div>
</template>
