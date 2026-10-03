<script setup lang="ts">
import { computed } from "vue"
import { Input as UiInput } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import type { WorkflowDesignerState } from "@/composables/useWorkflowDesigner"
import type { IActivity } from "@/core/abstracts/Activity"
import { makeEditPropertyCommand } from "@/core/designer/commands"
import { activityShortName } from "@/core/designer/registry"
import { getMergeMode, MERGE_MODES, setMergeMode } from "@/core/designer/form"
import { FORM_REGISTRY } from "./forms"
import GenericActivityForm from "./forms/GenericActivityForm.vue"
import OutputsSection from "./OutputsSection.vue"

/**
 * Node property content panel — renders the selected activity's form.
 * Extracted from the former PropertyPanel.vue; dock shell lives in RightToolDock.
 */
const props = defineProps<{
  activity: IActivity | null
  designer: WorkflowDesignerState
}>()

const typeShort = computed(() => (props.activity ? activityShortName(props.activity.type) : ""))

const dedicatedForm = computed(() => FORM_REGISTRY[typeShort.value] ?? null)

const descriptor = computed(() => {
  if (!props.activity) return null
  const map = props.designer.descriptorByType.value
  return map.get(props.activity.type) ?? map.get(`Cike.${typeShort.value}`) ?? null
})

const descriptors = computed(() => descriptor.value?.inputs ?? [])
const outputDescriptors = computed(() => descriptor.value?.outputs ?? [])

const inboundCount = computed(() => {
  if (!props.activity) return 0
  return props.designer.connectionTargets.value.get(props.activity.id) ?? 0
})

const currentMergeMode = computed(() => (props.activity ? getMergeMode(props.activity) : null))

function commitName(value: string): void {
  const activity = props.activity
  if (!activity) return
  const from = activity.name ?? null
  const to = value === "" ? null : value
  props.designer.executeCommand(
    makeEditPropertyCommand(activity as unknown as Record<string, unknown>, "name", from, to),
  )
}

function commitCode(value: string): void {
  const activity = props.activity
  if (!activity) return
  const from = activity.code ?? null
  const to = value.trim()
  if (to === "" || to === from) return
  props.designer.executeCommand(
    makeEditPropertyCommand(activity as unknown as Record<string, unknown>, "code", from, to),
  )
}

function commitMergeMode(mode: string): void {
  const activity = props.activity
  if (!activity) return
  const from = getMergeMode(activity)
  setMergeMode(activity as IActivity, mode)
  props.designer.executeCommand(
    makeEditPropertyCommand(activity.customProperties as Record<string, unknown>, "mergeMode", from, mode),
  )
}
</script>

<template>
  <div v-if="!activity" class="px-3 py-6 text-center text-xs text-muted-foreground">未选中节点</div>
  <div v-else class="space-y-4 px-3 py-3">
    <div class="space-y-1">
      <div class="text-sm font-medium text-foreground">{{ activity.name ?? activityShortName(activity.type) }}</div>
      <div class="font-mono text-xs text-muted-foreground">{{ activity.type }}</div>
    </div>

    <div class="space-y-1">
      <Label class="text-xs">标识（Code）</Label>
      <UiInput
        :model-value="activity.code ?? ''"
        :placeholder="activityShortName(activity.type)"
        @change="(event: Event) => commitCode((event.target as HTMLInputElement).value)"
      />
      <div class="text-[10px] text-muted-foreground">节点的稳定标识，用于流程引用</div>
    </div>

    <div class="space-y-1">
      <Label class="text-xs">名称</Label>
      <UiInput
        :model-value="activity.name ?? ''"
        :placeholder="activityShortName(activity.type)"
        @change="(event: Event) => commitName((event.target as HTMLInputElement).value)"
      />
    </div>

    <div v-if="inboundCount >= 2" class="space-y-1">
      <Label class="text-xs">合并模式</Label>
      <Select :model-value="currentMergeMode ?? ''" @update:model-value="(mode) => commitMergeMode(String(mode))">
        <SelectTrigger size="sm" class="w-full text-xs">
          <SelectValue class="block! min-w-0 truncate" placeholder="未设置（按 Stream）" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem v-for="mode in MERGE_MODES" :key="mode" :value="mode">{{ mode }}</SelectItem>
        </SelectContent>
      </Select>
      <div class="text-[10px] text-muted-foreground">多入边节点的汇聚语义</div>
    </div>

    <component
      :is="dedicatedForm"
      v-if="dedicatedForm"
      :activity="activity"
      :descriptors="descriptors"
      :designer="designer"
    />
    <div v-else class="space-y-2">
      <div class="text-xs font-medium text-muted-foreground">输入属性</div>
      <GenericActivityForm :activity="activity" :descriptors="descriptors" :designer="designer" />
    </div>

    <OutputsSection :activity="activity" :designer="designer" :descriptors="outputDescriptors" />
  </div>
</template>
