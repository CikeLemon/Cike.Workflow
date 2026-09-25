<script setup lang="ts">
import { computed } from "vue"
import type { Node } from "@antv/x6"
import { ChevronRight, CircleAlert } from "@lucide/vue"
import type { DesignerNodeData } from "@/core/designer/projection"
import { ACTIVITY_STATUS_UI } from "@/core/designer/execution"
import { resolveActivityIcon } from "./icons"

const props = defineProps<{ node: Node }>()

const data = computed(() => props.node.getData() as DesignerNodeData & { selected?: boolean })

const icon = computed(() => resolveActivityIcon(data.value.icon))

const statusUi = computed(() => (data.value.status != null ? ACTIVITY_STATUS_UI[data.value.status] : null))

/** Runtime status border: Running = pulse, Faulted = red. Lower priority than selected/error. */
const statusBorderClass = computed(() => {
  if (data.value.selected || data.value.hasError) return ""
  switch (data.value.status) {
    case 1: return "border-info animate-pulse"
    case 4: return "border-destructive ring-1 ring-destructive/35"
    default: return ""
  }
})
</script>

<template>
  <div class="relative h-full w-full">
    <!-- Drill affordance: an in-bounds "under-card" peeking at the bottom edge,
         reading as "a canvas level is stacked below". Kept inside the node box
         (not an offset pseudo-layer) so X6's fixed-size foreignObject never clips
         it. Lives on the depth/background channel, orthogonal to the border
         channel used by generic/selected/error, so all states coexist. -->
    <div
      v-if="data.canDrill"
      class="pointer-events-none absolute inset-x-[3px] bottom-0 top-[5px] rounded-md border border-border/70 bg-muted/70"
    />
    <div
      class="absolute inset-x-0 top-0 flex items-center gap-2 rounded-md border bg-card px-3 text-card-foreground shadow-sm transition-shadow"
      :class="[
        data.canDrill ? 'bottom-[5px]' : 'bottom-0',
        data.isGeneric ? 'border-dashed' : '',
        data.selected
          ? 'border-primary ring-2 ring-primary/40'
          : data.hasError
            ? 'border-destructive ring-1 ring-destructive/35'
            : statusBorderClass,
      ]"
    >
      <component :is="icon" :size="14" class="shrink-0 text-muted-foreground" />
      <span class="min-w-0 truncate text-xs font-medium text-foreground">{{ data.name }}</span>
      <!-- Right slot: status/error/type badge first, then the persistent drill
           chevron at the very edge, so badges and the affordance never collide. -->
      <span class="ml-auto flex shrink-0 items-center gap-1">
        <CircleAlert
          v-if="data.hasError"
          :size="14"
          class="text-destructive"
          aria-label="校验问题"
        />
        <span
          v-else-if="statusUi"
          class="rounded px-1.5 py-0.5 text-[10px] font-medium"
          :class="statusUi.class"
        >{{ statusUi.label }}</span>
        <span v-else-if="data.isGeneric" class="text-[10px] text-muted-foreground">{{ data.typeShort }}</span>
        <span v-if="data.canDrill" class="flex cursor-pointer items-center" title="双击进入">
          <ChevronRight :size="14" class="text-muted-foreground" aria-label="可下钻" />
        </span>
      </span>
    </div>
  </div>
</template>
