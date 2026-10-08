<template>
  <div class="relative flex shrink-0 flex-col border-t bg-background">
    <div
      v-if="open"
      class="absolute inset-x-0 top-0 h-1 cursor-row-resize touch-none hover:bg-primary/30"
      title="拖拽调整高度，双击复位"
      @pointerdown="onResizeStart"
      @dblclick="onResizeReset"
    />
    <button
      type="button"
      class="flex h-8 w-full items-center gap-2 px-3 text-xs hover:bg-muted/50"
      :aria-expanded="open"
      @click="emit('update:open', !open)"
    >
      <LoaderCircle v-if="validating" :size="14" class="shrink-0 animate-spin text-muted-foreground" />
      <ListChecks v-else :size="14" class="shrink-0 text-muted-foreground" />
      <span class="shrink-0 font-medium text-foreground">问题清单</span>
      <span v-if="count > 0" class="font-medium text-destructive">{{ count }} 个问题</span>
      <span v-else-if="validationError" class="font-medium text-warning">校验失败</span>
      <span v-else class="font-medium text-muted-foreground">校验通过</span>
      <span class="ml-auto shrink-0 text-muted-foreground">{{ open ? "收起" : "展开" }}</span>
      <component :is="open ? ChevronDown : ChevronUp" :size="14" class="shrink-0 text-muted-foreground" />
    </button>

    <div v-if="open" class="overflow-y-auto border-t" :style="{ height: `${panelHeight}px` }">
      <p v-if="validationError" class="px-3 py-2 text-xs text-warning">
        无法完成校验（{{ validationError }}）。这不是"画布没问题"，请检查后端连接后重试。
      </p>
      <div
        v-else-if="count === 0"
        class="flex items-center gap-2 px-3 py-3 text-xs text-muted-foreground"
      >
        <CircleCheck :size="14" class="shrink-0 text-success" />
        画布校验通过，没有发现问题。
      </div>
      <ul v-else>
        <li v-for="(problem, index) in problems" :key="`${problem.nodeId ?? problem.activityId ?? 'wf'}-${index}`">
          <button
            type="button"
            class="flex w-full items-start gap-2 px-3 py-1.5 text-left text-xs hover:bg-muted/50 disabled:cursor-default disabled:hover:bg-transparent"
            :disabled="!isLocatable(problem)"
            @click="onRowClick(problem)"
          >
            <CircleAlert :size="14" class="mt-0.5 shrink-0 text-destructive" />
            <span class="min-w-0 flex-1">
              <span class="text-foreground">{{ problem.message || "（无消息）" }}</span>
              <span v-if="problem.name" class="ml-2 text-muted-foreground">· {{ problem.name }}</span>
              <span v-if="!isLocatable(problem)" class="ml-2 text-muted-foreground">（工作流级，不可跳转）</span>
            </span>
          </button>
        </li>
      </ul>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from "vue"
import { ChevronDown, ChevronUp, CircleAlert, CircleCheck, ListChecks, LoaderCircle } from "@lucide/vue"
import { usePanelResize } from "@/composables/usePanelResize"
import type { ValidationProblem } from "@/composables/useWorkflowDesigner"

/**
 * Canvas-bottom "problem list" dock — the single outlet for canvas validation
 * problems (ADR 0002). Collapsible IDE-Problems style: the header always shows
 * the problem count / validating / pass state; the body lists each problem and
 * emits `reveal` for locatable rows so the designer can drill to the node.
 */
const props = defineProps<{
  problems: ValidationProblem[]
  validating: boolean
  validationError: string | null
  open: boolean
}>()

const emit = defineEmits<{
  "update:open": [open: boolean]
  reveal: [problem: ValidationProblem]
}>()

const count = computed(() => props.problems.length)

/** 展开态体部高度：顶缘拖拽调整、双击复位，跨会话持久化。 */
const { size: panelHeight, onPointerDown: onResizeStart, onDoubleClick: onResizeReset } = usePanelResize({
  axis: "height",
  invert: true,
  defaultSize: 200,
  min: 120,
  max: 480,
  maxViewportRatio: 0.5,
  storageKey: "cike.dock.size.problems",
})

/** A problem is locatable when it carries a NodeId chain or an activity id. */
function isLocatable(problem: ValidationProblem): boolean {
  return Boolean(problem.nodeId || problem.activityId)
}

function onRowClick(problem: ValidationProblem): void {
  if (isLocatable(problem)) emit("reveal", problem)
}
</script>
