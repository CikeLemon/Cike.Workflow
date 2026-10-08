<template>
  <div class="space-y-3">
    <ExpressionEditor v-if="modeExpr" :expression="modeExpr" :designer="designer" label="匹配模式" :literal-default="0">
      <template #default="{ value, commit: commitMode }">
        <Select :model-value="String(value ?? 0)" @update:model-value="(v) => commitMode(Number(String(v)))">
          <SelectTrigger size="sm" class="w-full text-xs">
            <SelectValue class="block! min-w-0 truncate" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="0">MatchFirst（首个匹配）</SelectItem>
            <SelectItem value="1">MatchAny（任一匹配）</SelectItem>
          </SelectContent>
        </Select>
      </template>
    </ExpressionEditor>

    <div class="space-y-2">
      <Label class="text-xs">分支（Case）</Label>
      <div v-for="(entry, index) in caseConditions" :key="index" class="border-border space-y-2 rounded-md border p-2">
        <div class="flex items-center gap-1.5">
          <UiInput
            class="h-8 flex-1 text-xs"
            :model-value="entry.label"
            placeholder="分支标签（出端口）"
            :disabled="designer.readonly.value"
            @change="(event: Event) => updateLabel(index, (event.target as HTMLInputElement).value)"
          />
          <Button
            variant="ghost"
            size="icon"
            class="h-8 w-8 shrink-0"
            :disabled="designer.readonly.value"
            @click="removeCase(index)"
          >
            <Trash2 :size="13" />
          </Button>
        </div>
        <ConditionEditor
          :spec="entry"
          :designer="designer"
          :readonly="designer.readonly.value"
          @change="(spec) => updateSpec(index, spec)"
        />
      </div>
      <Button variant="outline" size="sm" class="w-full" :disabled="designer.readonly.value" @click="addCase">
        <Plus :size="13" class="mr-1" />添加分支
      </Button>
      <div class="text-[10px] text-muted-foreground">每个分支标签都会成为该节点的一个出端口</div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, watch } from "vue"
import { Input as UiInput } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Button } from "@/components/ui/button"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Plus, Trash2 } from "@lucide/vue"
import type { WorkflowDesignerState } from "@/composables/useWorkflowDesigner"
import type { IActivity } from "@/core/abstracts/Activity"
import { compileCondition, type ConditionSpec } from "@/core/designer/conditionCompile"
import { emptySpec, readCaseConditions, type CaseCondition, type CustomExpressionContainer } from "@/core/designer/conditionModel"
import { makeBatchCommand, makeEditPropertyCommand } from "@/core/designer/commands"
import { makeSetExpressionCommand, type ExpressionLike } from "@/core/designer/expression"
import ExpressionEditor from "../ExpressionEditor.vue"
import ConditionEditor from "../ConditionEditor.vue"

const props = defineProps<{ activity: unknown; designer: WorkflowDesignerState }>()

interface SwitchActivity extends IActivity {
  mode?: { expression: ExpressionLike }
  cases?: Array<{ label: string; value: ExpressionLike }>
}

const act = computed(() => props.activity as SwitchActivity)

// `mode` is an Input (expression-backed) → edited through ExpressionEditor.
const modeExpr = computed<ExpressionLike | null>(() => act.value.mode?.expression ?? null)

// Reading revision re-runs this projection after any command / undo (shallowRef model).
const caseConditions = computed<CaseCondition[]>(() => {
  void props.designer.revision.value
  return readCaseConditions(act.value)
})

function containerOf(activity: SwitchActivity): CustomExpressionContainer | null {
  return (activity.customProperties as Record<string, unknown>)["customExpression"] as CustomExpressionContainer | null
}

function writeContainer(next: CustomExpressionContainer): void {
  const activity = act.value
  const customProperties = activity.customProperties as Record<string, unknown>
  const fromContainer = customProperties["customExpression"] ?? null
  props.designer.executeCommand(makeEditPropertyCommand(customProperties, "customExpression", fromContainer, next))
}

/**
 * Structural edits (add/remove case, rename label) must also reshape the wire cases
 * array, whose labels project to out-ports. Value edits inside a case (operand /
 * escape-hatch, owned by ExpressionEditor) only touch the container; the derived
 * sync below keeps wire values fresh.
 */
function commit(nextCases: CaseCondition[]): void {
  const activity = act.value
  const customProperties = activity.customProperties as Record<string, unknown>
  const fromContainer = customProperties["customExpression"] ?? null
  const wireCases = nextCases.map((entry) => ({ label: entry.label, value: compileCondition(entry) }))
  props.designer.executeCommand(
    makeBatchCommand("修改分支", [
      makeEditPropertyCommand(activity as unknown as Record<string, unknown>, "cases", activity.cases ?? null, wireCases),
      makeEditPropertyCommand(customProperties, "customExpression", fromContainer, {
        ...(containerOf(activity) ?? {}),
        caseConditions: nextCases,
      }),
    ]),
  )
}

/** Spec-only edit (discriminator switch / group rebuild): container write, values re-derived. */
function updateSpec(index: number, spec: ConditionSpec): void {
  writeContainer({
    ...(containerOf(act.value) ?? {}),
    caseConditions: caseConditions.value.map((entry, i) => (i === index ? { ...entry, ...spec } : entry)),
  })
}

function updateLabel(index: number, label: string): void {
  commit(caseConditions.value.map((entry, i) => (i === index ? { ...entry, label } : entry)))
}

function addCase(): void {
  commit([...caseConditions.value, { label: "", ...emptySpec() }])
}

function removeCase(index: number): void {
  commit(caseConditions.value.filter((_, i) => i !== index))
}

/**
 * Derived sync (ADR 0010): each wire case value is a pure function of its spec; on
 * every revision push compiled values into activity.cases[i].value without undo
 * entries (apply, not execute). Container-gated so legacy seeds never overwrite.
 */
watch(
  () => props.designer.revision.value,
  () => {
    const activity = act.value
    const container = containerOf(activity)
    if (!container?.caseConditions) return
    container.caseConditions.forEach((entry, index) => {
      const wire = activity.cases?.[index]
      if (!wire?.value) return
      const compiled = compileCondition(entry)
      if (wire.value.type !== compiled.type || wire.value.value !== compiled.value) {
        makeSetExpressionCommand(wire.value, compiled).apply()
      }
    })
  },
  { immediate: true },
)
</script>
