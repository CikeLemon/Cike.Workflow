<template>
  <div class="space-y-0 divide-y">
    <!-- Basic Info (read-only) -->
    <section class="px-3 py-3 space-y-2">
      <h3 class="text-xs font-medium text-foreground">基本信息</h3>
      <dl class="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1 text-xs">
        <dt class="text-muted-foreground">名称</dt>
        <dd class="truncate font-medium">{{ designer.definitionName.value }}</dd>
        <dt class="text-muted-foreground">定义 ID</dt>
        <dd class="truncate font-mono text-[11px]">{{ designer.definitionId.value }}</dd>
        <dt class="text-muted-foreground">版本</dt>
        <dd>v{{ designer.version.value }}</dd>
        <dt class="text-muted-foreground">类型</dt>
        <dd>{{ definitionTypeLabel }}</dd>
        <dt class="text-muted-foreground">状态</dt>
        <dd>
          <Badge v-if="designer.isPublished.value" variant="secondary" class="text-[10px]">已发布</Badge>
          <span v-else class="text-muted-foreground">草稿</span>
        </dd>
        <dt v-if="designer.usableAsActivity.value" class="text-muted-foreground">可作为活动</dt>
        <dd v-if="designer.usableAsActivity.value">是</dd>
        <template v-if="designer.description.value">
          <dt class="text-muted-foreground">描述</dt>
          <dd class="text-muted-foreground">{{ designer.description.value }}</dd>
        </template>
      </dl>
    </section>

    <!-- Variables -->
    <section class="px-3 py-2">
      <button type="button" class="flex w-full items-center gap-1 text-xs font-medium" @click="toggleSection('variables')">
        <component :is="sections.variables ? ChevronDown : ChevronRight" :size="12" />
        变量
        <span class="ml-auto text-muted-foreground">{{ variables.length }}</span>
        <Button v-if="!isReadonly" variant="ghost" size="icon" class="h-5 w-5" title="添加变量" @click.stop="addVariable">
          <Plus :size="12" />
        </Button>
      </button>
      <div v-if="sections.variables" class="mt-2 space-y-2">
        <div v-if="variables.length === 0" class="py-2 text-center text-[11px] text-muted-foreground">暂无变量</div>
        <div v-for="(variable, index) in variables" :key="variable.id ?? index" class="rounded border px-2 py-1.5 space-y-1.5">
          <div class="flex items-center gap-1.5">
            <div class="flex shrink-0 flex-col">
              <button type="button" class="text-muted-foreground/60 hover:text-foreground disabled:opacity-30" :disabled="isReadonly || index === 0" title="上移" @click="moveVariable(index, -1)">
                <ArrowUp :size="10" />
              </button>
              <button type="button" class="text-muted-foreground/60 hover:text-foreground disabled:opacity-30" :disabled="isReadonly || index === variables.length - 1" title="下移" @click="moveVariable(index, 1)">
                <ArrowDown :size="10" />
              </button>
            </div>
            <UiInput
              :model-value="variable.name ?? ''"
              class="h-8 flex-1 text-xs font-mono"
              placeholder="name"
              :disabled="isReadonly"
              @change="(e: Event) => commitVariableRename(index, (e.target as HTMLInputElement).value)"
            />
            <Button v-if="!isReadonly" variant="ghost" size="icon" class="h-5 w-5 shrink-0" title="删除" @click="removeVariable(index)">
              <Trash2 :size="11" />
            </Button>
          </div>
          <div class="flex items-center gap-2 pl-5">
            <Select
              :model-value="variable.typeName ?? 'String'"
              :disabled="isReadonly"
              @update:model-value="(v) => updateVariable(index, { typeName: String(v) })"
            >
              <SelectTrigger size="sm" class="w-28 text-[11px]">
                <SelectValue class="block! min-w-0 truncate" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem v-for="t in typeOptions" :key="t.typeName" :value="t.typeName!" class="text-[11px]">
                  {{ t.displayName ?? t.typeName }}
                </SelectItem>
                <SelectItem v-if="typeOptions.length === 0" value="String" class="text-[11px]">String</SelectItem>
              </SelectContent>
            </Select>
            <label class="flex items-center gap-1 text-[11px] text-muted-foreground">
              数组
              <Switch
                :model-value="variable.isArray ?? false"
                :disabled="isReadonly"
                class="scale-75"
                @update:model-value="(v: boolean) => updateVariable(index, { isArray: v })"
              />
            </label>
            <button
              type="button"
              class="ml-auto text-[10px] text-muted-foreground hover:text-foreground"
              @click="toggleAdvanced(`var-${index}`)"
            >
              {{ expandedAdvanced.has(`var-${index}`) ? "收起" : "高级" }}
            </button>
          </div>
          <div v-if="expandedAdvanced.has(`var-${index}`)" class="space-y-1 pl-5">
            <div class="space-y-0.5">
              <Label class="text-[10px]">默认值</Label>
              <UiInput
                :model-value="variable.defaultValue ?? ''"
                class="h-8 text-xs"
                placeholder="（空）"
                :disabled="isReadonly"
                @change="(e: Event) => updateVariable(index, { defaultValue: (e.target as HTMLInputElement).value || null })"
              />
            </div>
            <div class="space-y-0.5">
              <Label class="text-[10px]">存储驱动</Label>
              <Select
                :model-value="variable.storageDriverType ?? DEFAULT_STORAGE_DRIVER"
                :disabled="isReadonly"
                @update:model-value="(v) => updateVariable(index, { storageDriverType: String(v) })"
              >
                <SelectTrigger size="sm" class="w-full text-[11px]">
                  <SelectValue class="block! min-w-0 truncate" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem v-for="d in storageDriverOptions" :key="d.type" :value="d.type!" class="text-[11px]">
                    {{ d.displayName ?? d.type }}
                  </SelectItem>
                  <SelectItem v-if="storageDriverOptions.length === 0" :value="DEFAULT_STORAGE_DRIVER" class="text-[11px]">WorkflowInstance</SelectItem>
                </SelectContent>
              </Select>
            </div>
          </div>
        </div>
      </div>
    </section>

    <!-- Inputs -->
    <section class="px-3 py-2">
      <button type="button" class="flex w-full items-center gap-1 text-xs font-medium" @click="toggleSection('inputs')">
        <component :is="sections.inputs ? ChevronDown : ChevronRight" :size="12" />
        输入
        <span class="ml-auto text-muted-foreground">{{ inputs.length }}</span>
        <Button v-if="!isReadonly" variant="ghost" size="icon" class="h-5 w-5" title="添加输入" @click.stop="addInput">
          <Plus :size="12" />
        </Button>
      </button>
      <div v-if="sections.inputs" class="mt-2 space-y-2">
        <div v-if="inputs.length === 0" class="py-2 text-center text-[11px] text-muted-foreground">暂无输入</div>
        <div v-for="(input, index) in inputs" :key="index" class="rounded border px-2 py-1.5 space-y-1.5">
          <div class="flex items-center gap-1.5">
            <div class="flex shrink-0 flex-col">
              <button type="button" class="text-muted-foreground/60 hover:text-foreground disabled:opacity-30" :disabled="isReadonly || index === 0" title="上移" @click="moveInput(index, -1)">
                <ArrowUp :size="10" />
              </button>
              <button type="button" class="text-muted-foreground/60 hover:text-foreground disabled:opacity-30" :disabled="isReadonly || index === inputs.length - 1" title="下移" @click="moveInput(index, 1)">
                <ArrowDown :size="10" />
              </button>
            </div>
            <UiInput
              :model-value="input.name ?? ''"
              class="h-8 flex-1 text-xs font-mono"
              placeholder="name"
              :disabled="isReadonly"
              @change="(e: Event) => commitInputRename(index, (e.target as HTMLInputElement).value)"
            />
            <Select
              :model-value="input.type ?? 'String'"
              :disabled="isReadonly"
              @update:model-value="(v) => updateInput(index, { type: String(v) })"
            >
              <SelectTrigger size="sm" class="w-24 shrink-0 text-[11px]">
                <SelectValue class="block! min-w-0 truncate" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem v-for="t in typeOptions" :key="t.typeName" :value="t.typeName!" class="text-[11px]">
                  {{ t.displayName ?? t.typeName }}
                </SelectItem>
                <SelectItem v-if="typeOptions.length === 0" value="String" class="text-[11px]">String</SelectItem>
              </SelectContent>
            </Select>
            <Button v-if="!isReadonly" variant="ghost" size="icon" class="h-5 w-5 shrink-0" title="删除" @click="removeInput(index)">
              <Trash2 :size="11" />
            </Button>
          </div>
          <div class="flex items-center gap-2 pl-5">
            <UiInput
              :model-value="input.displayName ?? ''"
              class="h-8 flex-1 text-[11px]"
              placeholder="显示名"
              :disabled="isReadonly"
              @change="(e: Event) => updateInput(index, { displayName: (e.target as HTMLInputElement).value || undefined })"
            />
            <label class="flex items-center gap-1 text-[11px] text-muted-foreground">
              []
              <Switch
                :model-value="input.isArray ?? false"
                :disabled="isReadonly"
                class="scale-75"
                @update:model-value="(v: boolean) => updateInput(index, { isArray: v })"
              />
            </label>
          </div>
          <div class="pl-5">
            <button
              type="button"
              class="text-[10px] text-muted-foreground hover:text-foreground"
              @click="toggleAdvanced(`in-${index}`)"
            >
              {{ expandedAdvanced.has(`in-${index}`) ? "收起高级" : "高级设置" }}
            </button>
          </div>
          <div v-if="expandedAdvanced.has(`in-${index}`)" class="space-y-1 pl-5">
            <div class="space-y-0.5">
              <Label class="text-[10px]">说明</Label>
              <UiInput
                :model-value="input.description ?? ''"
                class="h-8 text-xs"
                :disabled="isReadonly"
                @change="(e: Event) => updateInput(index, { description: (e.target as HTMLInputElement).value || undefined })"
              />
            </div>
            <div v-if="input.defaultValue" class="space-y-0.5">
              <ExpressionEditor
                :expression="input.defaultValue as { type: string; value?: unknown }"
                :designer="designer"
                label="默认值"
                :allowed-types="INPUT_DEFAULT_ALLOWED_TYPES"
                literal-default=""
                dense
              >
                <template #default="{ value, commit }">
                  <UiInput
                    class="h-8 text-xs"
                    :model-value="value == null ? '' : String(value)"
                    :disabled="isReadonly"
                    @change="(e: Event) => { const raw = (e.target as HTMLInputElement).value; commit(raw === '' ? null : raw) }"
                  />
                </template>
              </ExpressionEditor>
            </div>
            <div class="space-y-0.5">
              <Label class="text-[10px]">存储驱动</Label>
              <Select
                :model-value="input.storageDriverType ?? DEFAULT_STORAGE_DRIVER"
                :disabled="isReadonly"
                @update:model-value="(v) => updateInput(index, { storageDriverType: String(v) })"
              >
                <SelectTrigger size="sm" class="w-full text-[11px]">
                  <SelectValue class="block! min-w-0 truncate" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem v-for="d in storageDriverOptions" :key="d.type" :value="d.type!" class="text-[11px]">
                    {{ d.displayName ?? d.type }}
                  </SelectItem>
                  <SelectItem v-if="storageDriverOptions.length === 0" :value="DEFAULT_STORAGE_DRIVER" class="text-[11px]">WorkflowInstance</SelectItem>
                </SelectContent>
              </Select>
            </div>
          </div>
        </div>
      </div>
    </section>

    <!-- Outputs -->
    <section class="px-3 py-2">
      <button type="button" class="flex w-full items-center gap-1 text-xs font-medium" @click="toggleSection('outputs')">
        <component :is="sections.outputs ? ChevronDown : ChevronRight" :size="12" />
        输出
        <span class="ml-auto text-muted-foreground">{{ outputs.length }}</span>
        <Button v-if="!isReadonly" variant="ghost" size="icon" class="h-5 w-5" title="添加输出" @click.stop="addOutput">
          <Plus :size="12" />
        </Button>
      </button>
      <div v-if="sections.outputs" class="mt-2 space-y-2">
        <div v-if="outputs.length === 0" class="py-2 text-center text-[11px] text-muted-foreground">暂无输出</div>
        <div v-for="(output, index) in outputs" :key="index" class="rounded border px-2 py-1.5 space-y-1.5">
          <div class="flex items-center gap-1.5">
            <div class="flex shrink-0 flex-col">
              <button type="button" class="text-muted-foreground/60 hover:text-foreground disabled:opacity-30" :disabled="isReadonly || index === 0" title="上移" @click="moveOutput(index, -1)">
                <ArrowUp :size="10" />
              </button>
              <button type="button" class="text-muted-foreground/60 hover:text-foreground disabled:opacity-30" :disabled="isReadonly || index === outputs.length - 1" title="下移" @click="moveOutput(index, 1)">
                <ArrowDown :size="10" />
              </button>
            </div>
            <UiInput
              :model-value="output.name ?? ''"
              class="h-8 flex-1 text-xs font-mono"
              placeholder="name"
              :disabled="isReadonly"
              @change="(e: Event) => updateOutput(index, { name: (e.target as HTMLInputElement).value })"
            />
            <Select
              :model-value="output.type ?? 'String'"
              :disabled="isReadonly"
              @update:model-value="(v) => updateOutput(index, { type: String(v) })"
            >
              <SelectTrigger size="sm" class="w-24 shrink-0 text-[11px]">
                <SelectValue class="block! min-w-0 truncate" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem v-for="t in typeOptions" :key="t.typeName" :value="t.typeName!" class="text-[11px]">
                  {{ t.displayName ?? t.typeName }}
                </SelectItem>
                <SelectItem v-if="typeOptions.length === 0" value="String" class="text-[11px]">String</SelectItem>
              </SelectContent>
            </Select>
            <Button v-if="!isReadonly" variant="ghost" size="icon" class="h-5 w-5 shrink-0" title="删除" @click="removeOutput(index)">
              <Trash2 :size="11" />
            </Button>
          </div>
          <div class="flex items-center gap-2 pl-5">
            <UiInput
              :model-value="output.displayName ?? ''"
              class="h-8 flex-1 text-[11px]"
              placeholder="显示名"
              :disabled="isReadonly"
              @change="(e: Event) => updateOutput(index, { displayName: (e.target as HTMLInputElement).value || undefined })"
            />
            <label class="flex items-center gap-1 text-[11px] text-muted-foreground">
              []
              <Switch
                :model-value="output.isArray ?? false"
                :disabled="isReadonly"
                class="scale-75"
                @update:model-value="(v: boolean) => updateOutput(index, { isArray: v })"
              />
            </label>
          </div>
          <div class="pl-5">
            <button
              type="button"
              class="text-[10px] text-muted-foreground hover:text-foreground"
              @click="toggleAdvanced(`out-${index}`)"
            >
              {{ expandedAdvanced.has(`out-${index}`) ? "收起高级" : "高级设置" }}
            </button>
          </div>
          <div v-if="expandedAdvanced.has(`out-${index}`)" class="space-y-1 pl-5">
            <div class="space-y-0.5">
              <Label class="text-[10px]">说明</Label>
              <UiInput
                :model-value="output.description ?? ''"
                class="h-8 text-xs"
                :disabled="isReadonly"
                @change="(e: Event) => updateOutput(index, { description: (e.target as HTMLInputElement).value || undefined })"
              />
            </div>
            <div v-if="output.defaultValue" class="space-y-0.5">
              <ExpressionEditor
                :expression="output.defaultValue as { type: string; value?: unknown }"
                :designer="designer"
                label="默认值"
                literal-default=""
                dense
              >
                <template #default="{ value, commit }">
                  <UiInput
                    class="h-8 text-xs"
                    :model-value="value == null ? '' : String(value)"
                    :disabled="isReadonly"
                    @change="(e: Event) => { const raw = (e.target as HTMLInputElement).value; commit(raw === '' ? null : raw) }"
                  />
                </template>
              </ExpressionEditor>
            </div>
          </div>
        </div>
      </div>
    </section>

    <!-- Outcomes -->
    <section class="px-3 py-2">
      <button type="button" class="flex w-full items-center gap-1 text-xs font-medium" @click="toggleSection('outcomes')">
        <component :is="sections.outcomes ? ChevronDown : ChevronRight" :size="12" />
        工作流结果
        <span class="ml-auto text-muted-foreground">{{ outcomes.length }}</span>
        <Button v-if="!isReadonly" variant="ghost" size="icon" class="h-5 w-5" title="添加结果" @click.stop="addOutcome">
          <Plus :size="12" />
        </Button>
      </button>
      <div v-if="sections.outcomes" class="mt-2 space-y-1.5">
        <div v-if="outcomes.length === 0" class="py-2 text-center text-[11px] text-muted-foreground">暂无结果</div>
        <div v-for="(outcome, index) in outcomes" :key="index" class="flex items-center gap-1.5">
          <div class="flex shrink-0 flex-col">
            <button type="button" class="text-muted-foreground/60 hover:text-foreground disabled:opacity-30" :disabled="isReadonly || index === 0" title="上移" @click="moveOutcome(index, -1)">
              <ArrowUp :size="10" />
            </button>
            <button type="button" class="text-muted-foreground/60 hover:text-foreground disabled:opacity-30" :disabled="isReadonly || index === outcomes.length - 1" title="下移" @click="moveOutcome(index, 1)">
              <ArrowDown :size="10" />
            </button>
          </div>
          <UiInput
            :model-value="outcome"
            class="h-8 flex-1 text-xs"
            placeholder="结果名称"
            :disabled="isReadonly"
            @change="(e: Event) => updateOutcome(index, (e.target as HTMLInputElement).value)"
          />
          <Button v-if="!isReadonly" variant="ghost" size="icon" class="h-5 w-5 shrink-0" title="删除" @click="removeOutcome(index)">
            <Trash2 :size="11" />
          </Button>
        </div>
      </div>
    </section>
  </div>
</template>

<script setup lang="ts">
import { computed, ref } from "vue"
import { ArrowDown, ArrowUp, ChevronDown, ChevronRight, Plus, Trash2 } from "@lucide/vue"
import { Button } from "@/components/ui/button"
import { Input as UiInput } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Badge } from "@/components/ui/badge"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Switch } from "@/components/ui/switch"
import type { WorkflowDesignerState } from "@/composables/useWorkflowDesigner"
import type { Expression, InputDefinition, OutputDefinition, VariableDefinition } from "@/api/generated"
import ExpressionEditor from "./ExpressionEditor.vue"

/**
 * Workflow Configuration panel (ADR 0008): read-only basic info + CRUD sections
 * for variables, inputs, outputs, and outcomes. All mutations flow through the
 * designer command stack for undo/redo and validation.
 */
const props = defineProps<{ designer: WorkflowDesignerState }>()

const isReadonly = computed(() => props.designer.readonly.value)

// Collapsible section state
const sections = ref({
  variables: true,
  inputs: true,
  outputs: true,
  outcomes: true,
})

function toggleSection(key: keyof typeof sections.value): void {
  sections.value[key] = !sections.value[key]
}

// --- Variables ---
const variables = computed(() => props.designer.variables.value)

function addVariable(): void {
  if (isReadonly.value) return
  const list = [...variables.value, { id: crypto.randomUUID(), name: "", typeName: "String", isArray: false }]
  props.designer.setVariables(list)
}

function removeVariable(index: number): void {
  if (isReadonly.value) return
  const list = variables.value.filter((_, i) => i !== index)
  props.designer.setVariables(list)
}

function updateVariable(index: number, patch: Partial<VariableDefinition>): void {
  if (isReadonly.value) return
  const list = variables.value.map((v, i) => (i === index ? { ...v, ...patch } : v))
  props.designer.setVariables(list)
}

function commitVariableRename(index: number, newName: string): void {
  if (isReadonly.value) return
  const old = variables.value[index]
  if (!old || old.name === newName) return
  if (newName && old.name) {
    props.designer.renameReference("Variable", old.name, newName)
  } else {
    updateVariable(index, { name: newName })
  }
}

// --- Inputs ---
const inputs = computed(() => props.designer.inputs.value)

function addInput(): void {
  if (isReadonly.value) return
  const list: InputDefinition[] = [...inputs.value, { name: "", type: "String", isArray: false }]
  props.designer.setInputs(list)
}

function removeInput(index: number): void {
  if (isReadonly.value) return
  const list = inputs.value.filter((_, i) => i !== index)
  props.designer.setInputs(list)
}

function updateInput(index: number, patch: Partial<InputDefinition>): void {
  if (isReadonly.value) return
  const list = inputs.value.map((item, i) => (i === index ? { ...item, ...patch } : item))
  props.designer.setInputs(list)
}

function commitInputRename(index: number, newName: string): void {
  if (isReadonly.value) return
  const old = inputs.value[index]
  if (!old || old.name === newName) return
  if (newName && old.name) {
    props.designer.renameReference("Input", old.name, newName)
  } else {
    updateInput(index, { name: newName })
  }
}

// --- Outputs ---
const outputs = computed(() => props.designer.outputs.value)

function addOutput(): void {
  if (isReadonly.value) return
  const list: OutputDefinition[] = [...outputs.value, { name: "", type: "String", isArray: false }]
  props.designer.setOutputs(list)
}

function removeOutput(index: number): void {
  if (isReadonly.value) return
  const list = outputs.value.filter((_, i) => i !== index)
  props.designer.setOutputs(list)
}

function updateOutput(index: number, patch: Partial<OutputDefinition>): void {
  if (isReadonly.value) return
  const list = outputs.value.map((item, i) => (i === index ? { ...item, ...patch } : item))
  props.designer.setOutputs(list)
}

// --- Outcomes ---
const outcomes = computed(() => props.designer.outcomes.value)

function addOutcome(): void {
  if (isReadonly.value) return
  props.designer.setOutcomes([...outcomes.value, ""])
}

function removeOutcome(index: number): void {
  if (isReadonly.value) return
  props.designer.setOutcomes(outcomes.value.filter((_, i) => i !== index))
}

function updateOutcome(index: number, value: string): void {
  if (isReadonly.value) return
  const list = outcomes.value.map((item, i) => (i === index ? value : item))
  props.designer.setOutcomes(list)
}

// --- Type selector options ---
const typeOptions = computed(() => props.designer.variableTypes.value)

// --- Storage driver selector options ---
/** Effective driver when a definition has none set explicitly: the backend default. */
const DEFAULT_STORAGE_DRIVER = "WorkflowInstance"
const storageDriverOptions = computed(() => props.designer.storageDrivers.value)

// --- Default expression editing (ADR 0009) ---
/** Input defaults may only be Literal/Liquid/JavaScript; outputs allow all types. */
const INPUT_DEFAULT_ALLOWED_TYPES = ["Literal", "Liquid", "Javascript"]

/**
 * Materializes an argument's defaultValue into a live expression object so the
 * ExpressionEditor can edit it in place through the command stack — the same
 * pattern node forms use. listRef and savedOptions share the item reference,
 * so edits land in the save payload without an extra list commit.
 */
function ensureDefaultValue(
  listRef: { value: Array<{ defaultValue?: Expression }> },
  index: number,
  label: string,
): void {
  if (isReadonly.value) return
  const item = listRef.value[index]
  if (!item || item.defaultValue) return
  props.designer.executeCommand({
    label,
    apply: () => {
      item.defaultValue = { type: "Literal", value: null }
    },
    undo: () => {
      delete item.defaultValue
    },
    redo: () => {
      item.defaultValue = { type: "Literal", value: null }
    },
  })
}

// --- Reordering ---
function moveItem<T>(list: T[], index: number, direction: -1 | 1): T[] {
  const target = index + direction
  if (target < 0 || target >= list.length) return list
  const next = [...list]
  ;[next[index], next[target]] = [next[target]!, next[index]!]
  return next
}

function moveVariable(index: number, direction: -1 | 1): void {
  if (isReadonly.value) return
  props.designer.setVariables(moveItem(variables.value, index, direction))
}

function moveInput(index: number, direction: -1 | 1): void {
  if (isReadonly.value) return
  props.designer.setInputs(moveItem(inputs.value, index, direction))
}

function moveOutput(index: number, direction: -1 | 1): void {
  if (isReadonly.value) return
  props.designer.setOutputs(moveItem(outputs.value, index, direction))
}

function moveOutcome(index: number, direction: -1 | 1): void {
  if (isReadonly.value) return
  props.designer.setOutcomes(moveItem(outcomes.value, index, direction))
}

// --- Advanced field expansion per item ---
const expandedAdvanced = ref<Set<string>>(new Set())

function toggleAdvanced(id: string): void {
  const next = new Set(expandedAdvanced.value)
  if (next.has(id)) {
    next.delete(id)
  } else {
    // Materialize the default expression when an argument editor first opens,
    // so the ExpressionEditor always has a live object to bind to.
    if (id.startsWith("in-")) ensureDefaultValue(inputs, Number(id.slice(3)), "初始化输入默认值")
    else if (id.startsWith("out-")) ensureDefaultValue(outputs, Number(id.slice(4)), "初始化输出默认值")
    next.add(id)
  }
  expandedAdvanced.value = next
}

// Definition type display
const DEFINITION_TYPE_LABELS: Record<number, string> = { 1: "工作流", 2: "Agent 工作流", 3: "审批" }
const definitionTypeLabel = computed(() => {
  const t = props.designer.definitionType.value
  return t ? (DEFINITION_TYPE_LABELS[t] ?? String(t)) : "—"
})
</script>
