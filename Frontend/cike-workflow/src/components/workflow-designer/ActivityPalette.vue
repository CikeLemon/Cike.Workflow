<template>
  <aside v-if="open" class="relative flex shrink-0 flex-col border-r" :style="{ width: `${panelWidth}px` }">
    <div class="flex shrink-0 items-center border-b bg-background px-3 py-2 text-xs font-medium text-muted-foreground">
      活动
      <button
        type="button"
        class="ml-auto rounded p-0.5 hover:bg-muted hover:text-foreground"
        title="收起活动面板"
        @click="open = false"
      >
        <PanelLeftClose :size="14" />
      </button>
    </div>
    <div class="min-h-0 flex-1 overflow-y-auto">
      <div v-if="groups.length === 0" class="px-3 py-6 text-center text-xs text-muted-foreground">活动清单加载中…</div>
      <div v-for="group in groups" :key="group.category" class="border-b last:border-b-0">
        <div class="px-3 pb-1 pt-3 text-[10px] font-medium uppercase tracking-wide text-muted-foreground">
          {{ group.category }}
        </div>
        <button
          v-for="item in group.items"
          :key="item.typeName"
          class="flex w-full items-center gap-2 px-3 py-1.5 text-left transition-colors"
          :class="item.canAdd ? 'cursor-grab hover:bg-accent' : 'cursor-not-allowed opacity-50'"
          :draggable="item.canAdd"
          :title="item.description ?? item.typeName"
          @click="item.canAdd && emit('add', item.typeName)"
          @dragstart="item.canAdd && onDragStart(item.typeName, $event)"
        >
          <component :is="resolveActivityIcon(item.icon)" :size="15" class="shrink-0 text-muted-foreground" />
          <div class="min-w-0">
            <div class="truncate text-xs font-medium text-foreground">{{ item.displayName }}</div>
            <div v-if="!item.canAdd" class="text-[10px] text-muted-foreground">前端未镜像，不可添加</div>
          </div>
        </button>
      </div>
    </div>
    <div
      class="absolute bottom-0 right-0 top-0 w-1 cursor-col-resize touch-none hover:bg-primary/30"
      title="拖拽调整宽度，双击复位"
      @pointerdown="onResizeStart"
      @dblclick="onResizeReset"
    />
  </aside>
  <aside v-else class="flex w-9 shrink-0 flex-col items-center border-r bg-background py-1">
    <button
      type="button"
      class="rounded p-1 text-muted-foreground hover:bg-muted hover:text-foreground"
      title="展开活动面板"
      @click="open = true"
    >
      <PanelLeftOpen :size="15" />
    </button>
  </aside>
</template>

<script setup lang="ts">
import { ref } from "vue"
import { PanelLeftClose, PanelLeftOpen } from "@lucide/vue"
import { usePanelResize } from "@/composables/usePanelResize"
import type { PaletteGroup } from "@/core/designer/palette"
import { resolveActivityIcon } from "./nodes/icons"

defineProps<{ groups: PaletteGroup[] }>()

/** Collapsible like an IDE side panel: collapsed leaves a thin rail to reopen. */
const open = ref(true)

/** 展开态宽度：右缘拖拽调整、双击复位，跨会话持久化。 */
const { size: panelWidth, onPointerDown: onResizeStart, onDoubleClick: onResizeReset } = usePanelResize({
  axis: "width",
  defaultSize: 224,
  min: 160,
  max: 420,
  storageKey: "cike.dock.size.palette",
})

const emit = defineEmits<{
  add: [typeName: string]
  dragstart: [payload: { typeName: string; event: DragEvent }]
}>()

function onDragStart(typeName: string, event: DragEvent): void {
  event.dataTransfer?.setData("cike-activity-type", typeName)
  event.dataTransfer?.setData("text/plain", typeName)
  emit("dragstart", { typeName, event })
}
</script>
