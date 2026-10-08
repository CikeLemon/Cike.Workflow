<template>
  <div class="flex shrink-0">
    <!-- Content panel -->
    <aside v-if="open" class="relative flex min-h-0 flex-col border-l" :style="{ width: `${panelWidth}px` }">
      <div class="flex shrink-0 items-center border-b px-3 py-2 text-xs font-medium text-muted-foreground">
        {{ activeTab === "config" ? "工作流配置" : "节点属性" }}
        <button
          type="button"
          class="ml-auto rounded p-0.5 hover:bg-muted hover:text-foreground"
          title="收起面板"
          @click="open = false"
        >
          <PanelRightClose :size="14" />
        </button>
      </div>
      <div class="min-h-0 flex-1 overflow-y-auto">
        <WorkflowConfigPanel v-if="activeTab === 'config'" :designer="designer" />
        <NodePropertyPanel v-else :activity="activity" :designer="designer" />
      </div>
      <div
        class="absolute bottom-0 left-0 top-0 w-1 cursor-col-resize touch-none hover:bg-primary/30"
        title="拖拽调整宽度，双击复位"
        @pointerdown="onResizeStart"
        @dblclick="onResizeReset"
      />
    </aside>

    <!-- Icon rail: always visible -->
    <nav class="flex w-9 shrink-0 flex-col items-center border-l bg-background py-1 gap-0.5">
      <button
        v-if="!open"
        type="button"
        class="rounded p-1.5 text-muted-foreground hover:bg-muted hover:text-foreground"
        title="展开面板"
        @click="open = true"
      >
        <PanelRightOpen :size="15" />
      </button>
      <div v-if="!open" class="my-1 h-px w-5 bg-border" />
      <button
        type="button"
        class="rounded p-1.5 transition-colors"
        :class="activeTab === 'config' && open
          ? 'bg-accent text-accent-foreground'
          : 'text-muted-foreground hover:bg-muted hover:text-foreground'"
        title="工作流配置"
        @click="activeTab = 'config'; open = true"
      >
        <Settings2 :size="16" />
      </button>
      <button
        type="button"
        class="rounded p-1.5 transition-colors"
        :class="activeTab === 'properties' && open
          ? 'bg-accent text-accent-foreground'
          : 'text-muted-foreground hover:bg-muted hover:text-foreground'"
        title="节点属性"
        @click="activeTab = 'properties'; open = true"
      >
        <SquareStack :size="16" />
      </button>
    </nav>
  </div>
</template>

<script setup lang="ts">
import { computed, ref, watch } from "vue"
import { PanelRightClose, PanelRightOpen, Settings2, SquareStack } from "@lucide/vue"
import { usePanelResize } from "@/composables/usePanelResize"
import type { WorkflowDesignerState } from "@/composables/useWorkflowDesigner"
import WorkflowConfigPanel from "./WorkflowConfigPanel.vue"
import NodePropertyPanel from "./NodePropertyPanel.vue"

/**
 * Right tool dock (ADR 0008): narrow icon rail + single content panel.
 * Switches between "Workflow Config" and "Node Properties" based on selection.
 * Reuses existing pin/resize/persistence mechanisms.
 */
const props = defineProps<{ designer: WorkflowDesignerState }>()

type DockTab = "config" | "properties"

const activeTab = ref<DockTab>("config")
const open = ref(true)

/** Auto-switch: selecting a node activates properties; deselecting returns to config. */
watch(
  () => props.designer.selectedActivityId.value,
  (id, prev) => {
    if (id && id !== prev) {
      activeTab.value = "properties"
      // Auto-expand on selection (unless user manually collapsed).
      open.value = true
    } else if (!id && prev) {
      activeTab.value = "config"
      // Preserve current open/collapsed state on deselect (ADR 0008).
    }
  },
)

const { size: panelWidth, onPointerDown: onResizeStart, onDoubleClick: onResizeReset } = usePanelResize({
  axis: "width",
  invert: true,
  defaultSize: 320,
  min: 260,
  max: 520,
  storageKey: "cike.dock.size.property",
})

const activity = computed(() => props.designer.selectedActivity.value)
</script>
