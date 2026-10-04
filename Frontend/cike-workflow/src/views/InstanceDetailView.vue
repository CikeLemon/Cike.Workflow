<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, shallowRef, watch } from "vue"
import { useRoute, useRouter } from "vue-router"
import { ArrowLeft, PanelRightClose, PanelRightOpen, Info, SquareStack } from "@lucide/vue"
import { Button } from "@/components/ui/button"
import { Badge } from "@/components/ui/badge"
import { usePanelResize } from "@/composables/usePanelResize"
import { useInstanceExecution } from "@/composables/useInstanceExecution"
import DesignerCanvas from "@/components/workflow-designer/DesignerCanvas.vue"
import DesignerBreadcrumb from "@/components/workflow-designer/DesignerBreadcrumb.vue"
import type { WorkflowStatus } from "@/api/generated"
import type { ActivityStatus } from "@/api/generated"
import { fromWireActivity, type WireActivity } from "@/core/designer/serialization"
import { Activity } from "@/core/abstracts/Activity"
import { Flowchart } from "@/core/activities/Flowchart"
import {
  projectCanvas,
  projectFlowchart,
  projectOrderedChain,
  canDrillInto,
  type CanvasProjection,
} from "@/core/designer/projection"
import { ensureDrillTarget, isChainContainer } from "@/core/designer/drill"
import { activityShortName } from "@/core/designer/registry"
import { ACTIVITY_STATUS_UI, formatDateTime, formatDuration } from "@/core/designer/execution"
import InstanceIoSection from "@/components/InstanceIoSection.vue"
import type { IActivity } from "@/core/abstracts/Activity"

const route = useRoute()
const router = useRouter()
const workspaceId = route.params.workspaceId as string
const instanceId = route.params.instanceId as string

// ---------------------------------------------------------------------------
// Execution state (HTTP snapshot + SignalR real-time)
// ---------------------------------------------------------------------------
const exec = useInstanceExecution(instanceId)

onMounted(() => { exec.load() })
onBeforeUnmount(() => { exec.dispose() })

// ---------------------------------------------------------------------------
// Activity tree + drill stack
// ---------------------------------------------------------------------------
interface DrillEntry {
  activity: Activity
  title: string
  chainChildren?: IActivity[]
}

const activityTree = shallowRef<Activity | null>(null)
const drillStack = shallowRef<DrillEntry[]>([])
const selectedActivityId = ref<string | null>(null)

// Parse the definition root into an Activity tree when loaded
watch(
  () => exec.definitionRoot.value,
  (raw) => {
    if (!raw) { activityTree.value = null; return }
    const parsed = fromWireActivity(raw as WireActivity)
    activityTree.value = parsed instanceof Activity ? parsed : null
    // Initialize drill stack with root
    if (parsed) {
      const rootActivity = parsed as Activity
      const title = rootActivity.name ?? activityShortName(rootActivity.type)
      drillStack.value = [{ activity: rootActivity, title }]
    }
  },
)

const currentEntry = computed(() => drillStack.value[drillStack.value.length - 1] ?? null)
const breadcrumb = computed(() => drillStack.value.map((e) => e.title))

function drillInto(activityId: string): void {
  const entry = currentEntry.value
  if (!entry) return
  const children = getChildren(entry)
  const target = children.find((c) => c.id === activityId)
  if (!target || !canDrillInto(target)) return
  if (!(target instanceof Activity)) return
  const drillTarget = ensureDrillTarget(target)
  if (!drillTarget) return
  const title = target.name ?? activityShortName(target.type)
  const newEntry: DrillEntry = isChainContainer(drillTarget)
    ? { activity: drillTarget, title, chainChildren: (drillTarget as unknown as { activities: IActivity[] }).activities }
    : { activity: drillTarget, title }
  drillStack.value = [...drillStack.value, newEntry]
  selectedActivityId.value = null
}

function popTo(index: number): void {
  if (index < 0 || index >= drillStack.value.length) return
  drillStack.value = drillStack.value.slice(0, index + 1)
  selectedActivityId.value = null
}

function getChildren(entry: DrillEntry): IActivity[] {
  if (entry.chainChildren) return entry.chainChildren
  const act = entry.activity as unknown as { activities?: IActivity[] }
  return Array.isArray(act.activities) ? act.activities : []
}

// ---------------------------------------------------------------------------
// Projection with status overlay
// ---------------------------------------------------------------------------
const projection = computed<CanvasProjection>(() => {
  const entry = currentEntry.value
  if (!entry) return { nodes: [], edges: [] }
  const base = entry.chainChildren
    ? projectOrderedChain(entry.chainChildren)
    : entry.activity instanceof Flowchart
      ? projectFlowchart(entry.activity)
      : "activities" in entry.activity
        ? projectCanvas(entry.activity as never)
        : { nodes: [], edges: [] }

  const statusMap = exec.statusMap.value
  // On the read-only instance canvas every node must show a status, so the set
  // is closed: nodes with no execution record yet get an explicit "未执行"
  // badge (accurate whether the instance is still running or already terminal).
  return {
    edges: base.edges,
    nodes: base.nodes.map((node) => ({
      ...node,
      data: {
        ...node.data,
        status: statusMap.get(node.data.activityId) ?? null,
        notRun: !statusMap.has(node.data.activityId),
      },
    })),
  }
})

const entryKey = computed(() => `inst-${drillStack.value.length}`)

// ---------------------------------------------------------------------------
// Canvas event handlers
// ---------------------------------------------------------------------------
function onNodeClick(activityId: string): void {
  selectedActivityId.value = selectedActivityId.value === activityId ? null : activityId
  exec.selectedActivityId.value = selectedActivityId.value
}

function onNodeDblclick(activityId: string): void {
  drillInto(activityId)
}

// ---------------------------------------------------------------------------
// Right panel state
// ---------------------------------------------------------------------------
type PanelTab = "overview" | "detail"
const activeTab = ref<PanelTab>("overview")
const panelOpen = ref(true)

watch(selectedActivityId, (id, prev) => {
  if (id && id !== prev) { activeTab.value = "detail"; panelOpen.value = true }
  else if (!id && prev) { activeTab.value = "overview" }
})

const { size: panelWidth, onPointerDown: onResizeStart, onDoubleClick: onResizeReset } = usePanelResize({
  axis: "width",
  invert: true,
  defaultSize: 320,
  min: 260,
  max: 520,
  storageKey: "cike.dock.size.instance",
})

// ---------------------------------------------------------------------------
// Header status
// ---------------------------------------------------------------------------
const instanceStatusUi: Record<WorkflowStatus, { label: string; class: string }> = {
  0: { label: "等待中", class: "bg-muted text-muted-foreground" },
  1: { label: "执行中", class: "bg-info/15 text-info" },
  2: { label: "已挂起", class: "bg-warning/15 text-warning" },
  3: { label: "已完成", class: "bg-success/15 text-success" },
  4: { label: "已取消", class: "bg-muted text-muted-foreground" },
  5: { label: "已故障", class: "bg-destructive/15 text-destructive" },
  6: { label: "已中断", class: "bg-warning/15 text-warning" },
}

const statusBadge = computed(() => {
  const s = exec.instanceStatus.value
  return s != null ? instanceStatusUi[s] : null
})

// Header timeline: start / end / elapsed, derived from the instance snapshot.
const startTimeText = computed(() => formatDateTime(exec.instance.value?.createdAt))
const endTimeText = computed(() => formatDateTime(exec.instance.value?.finishedAt))
const durationText = computed(() =>
  formatDuration(exec.instance.value?.createdAt, exec.instance.value?.finishedAt),
)

function goBack(): void {
  router.push({ name: "instances", params: { workspaceId } })
}

// ---------------------------------------------------------------------------
// Node execution detail helpers
// ---------------------------------------------------------------------------
const selectedRecord = computed(() => exec.selectedRecord.value)

function jsonDisplay(obj: unknown): string {
  if (obj == null) return "—"
  try { return JSON.stringify(obj, null, 2) } catch { return String(obj) }
}

const recordStatusUi = computed(() => {
  const s = selectedRecord.value?.status as ActivityStatus | undefined
  return s != null ? ACTIVITY_STATUS_UI[s] : null
})

// ---------------------------------------------------------------------------
// Workflow I/O (schema-driven, see InstanceIoSection)
// ---------------------------------------------------------------------------
const stateAvailable = computed(() => exec.instance.value?.workflowState != null)
const inputValues = computed(() => exec.instance.value?.workflowState?.input)
const outputValues = computed(() => exec.instance.value?.workflowState?.output)
</script>

<template>
  <div class="flex h-full min-h-0 flex-col">
    <!-- Header toolbar -->
    <header class="flex shrink-0 items-center gap-3 border-b px-4 py-2">
      <Button variant="ghost" size="icon" title="返回" @click="goBack">
        <ArrowLeft :size="16" />
      </Button>
      <Badge v-if="statusBadge" :class="statusBadge.class">{{ statusBadge.label }}</Badge>
      <Badge v-if="exec.instance.value?.isDebug" variant="outline" class="px-1.5 py-0 text-[10px] font-normal text-muted-foreground">调试</Badge>
      <span class="truncate text-sm font-medium">{{ exec.instance.value?.name || `实例 ${instanceId}` }}</span>
      <div class="flex items-center gap-4 text-xs text-muted-foreground">
        <RouterLink
          v-if="exec.instance.value?.definitionVersionId"
          :to="`/workspaces/${workspaceId}/definitions/${exec.instance.value.definitionVersionId}`"
          class="hover:text-primary"
        >
          {{ exec.instance.value?.definitionName || "查看定义" }}
        </RouterLink>
        <span v-if="exec.instance.value?.version">版本 <span class="font-mono">v{{ exec.instance.value.version }}</span></span>
        <span v-if="startTimeText">开始 <span class="font-mono">{{ startTimeText }}</span></span>
        <span v-if="endTimeText">结束 <span class="font-mono">{{ endTimeText }}</span></span>
        <span v-if="durationText">耗时 <span class="font-mono">{{ durationText }}</span></span>
        <span v-if="exec.instance.value?.correlationId">关联 ID <span class="font-mono">{{ exec.instance.value.correlationId }}</span></span>
      </div>
    </header>

    <!-- Main content -->
    <div class="flex min-h-0 flex-1">
      <!-- Canvas area -->
      <div class="flex min-w-0 flex-1 flex-col">
        <DesignerBreadcrumb
          v-if="drillStack.length > 1"
          :entries="breadcrumb"
          @select="popTo"
        />
        <div class="relative min-h-0 flex-1">
          <div v-if="exec.loading.value" class="flex h-full items-center justify-center text-sm text-muted-foreground">
            加载中…
          </div>
          <div v-else-if="exec.loadError.value" class="flex h-full items-center justify-center text-sm text-destructive">
            {{ exec.loadError.value }}
          </div>
          <DesignerCanvas
            v-else
            :projection="projection"
            :interactive="false"
            :selected-id="selectedActivityId"
            :entry-key="entryKey"
            :entry-activity="currentEntry?.activity ?? null"
            @node-click="onNodeClick"
            @node-dblclick="onNodeDblclick"
          />
        </div>
      </div>

      <!-- Right dock panel -->
      <div class="flex shrink-0">
        <aside v-if="panelOpen" class="relative flex min-h-0 flex-col border-l" :style="{ width: `${panelWidth}px` }">
          <div class="flex shrink-0 items-center border-b px-3 py-2 text-xs font-medium text-muted-foreground">
            {{ activeTab === "overview" ? "实例概览" : "节点执行详情" }}
            <button
              type="button"
              class="ml-auto rounded p-0.5 hover:bg-muted hover:text-foreground"
              title="收起面板"
              @click="panelOpen = false"
            >
              <PanelRightClose :size="14" />
            </button>
          </div>

          <div class="min-h-0 flex-1 overflow-y-auto">
            <!-- Instance Overview -->
            <div v-if="activeTab === 'overview'" class="space-y-4 p-3">
              <section>
                <h4 class="mb-1 text-xs font-semibold text-muted-foreground">状态</h4>
                <Badge v-if="statusBadge" :class="statusBadge.class">{{ statusBadge.label }}</Badge>
              </section>
              <InstanceIoSection
                title="工作流输入"
                :defs="exec.inputDefs.value"
                :values="inputValues"
                :state-available="stateAvailable"
              />
              <InstanceIoSection
                title="工作流输出"
                :defs="exec.outputDefs.value"
                :values="outputValues"
                :state-available="stateAvailable"
              />
              <section class="space-y-1 text-xs text-muted-foreground">
                <h4 class="font-semibold">元信息</h4>
                <p v-if="exec.instance.value?.correlationId">关联 ID: <span class="font-mono">{{ exec.instance.value.correlationId }}</span></p>
                <p v-if="startTimeText">开始时间: {{ startTimeText }}</p>
                <p v-if="endTimeText">结束时间: {{ endTimeText }}</p>
                <p v-if="durationText">耗时: {{ durationText }}</p>
                <p v-if="exec.instance.value?.version">定义版本: v{{ exec.instance.value.version }}</p>
                <p v-if="exec.instance.value?.isDebug">来源: 调试运行</p>
              </section>
            </div>

            <!-- Node Execution Detail -->
            <div v-else class="space-y-4 p-3">
              <template v-if="selectedRecord">
                <section class="flex items-center gap-2">
                  <span class="text-sm font-medium">{{ selectedRecord.activityName || selectedRecord.activityId }}</span>
                  <Badge v-if="recordStatusUi" :class="recordStatusUi.class">{{ recordStatusUi.label }}</Badge>
                </section>
                <section v-if="selectedRecord.activityType">
                  <h4 class="mb-1 text-xs font-semibold text-muted-foreground">类型</h4>
                  <p class="text-xs font-mono">{{ selectedRecord.activityType }}</p>
                </section>
                <section v-if="selectedRecord.activityState">
                  <h4 class="mb-1 text-xs font-semibold text-muted-foreground">活动状态</h4>
                  <pre class="max-h-48 overflow-auto rounded bg-muted/50 p-2 text-xs font-mono">{{ jsonDisplay(selectedRecord.activityState) }}</pre>
                </section>
                <section v-if="selectedRecord.outputs">
                  <h4 class="mb-1 text-xs font-semibold text-muted-foreground">输出</h4>
                  <pre class="max-h-48 overflow-auto rounded bg-muted/50 p-2 text-xs font-mono">{{ jsonDisplay(selectedRecord.outputs) }}</pre>
                </section>
                <section v-if="selectedRecord.exception">
                  <h4 class="mb-1 text-xs font-semibold text-destructive">异常</h4>
                  <pre class="max-h-32 overflow-auto rounded bg-destructive/5 p-2 text-xs font-mono text-destructive">{{ jsonDisplay(selectedRecord.exception) }}</pre>
                </section>
              </template>
              <p v-else class="text-xs text-muted-foreground">点击画布节点查看执行详情</p>

              <!-- Logs section (backend stub - shows empty state) -->
              <section class="border-t pt-3">
                <h4 class="mb-1 text-xs font-semibold text-muted-foreground">执行日志</h4>
                <p class="text-xs text-muted-foreground italic">暂无日志</p>
              </section>
            </div>
          </div>

          <div
            class="absolute bottom-0 left-0 top-0 w-1 cursor-col-resize touch-none hover:bg-primary/30"
            title="拖拽调整宽度，双击复位"
            @pointerdown="onResizeStart"
            @dblclick="onResizeReset"
          />
        </aside>

        <!-- Icon rail -->
        <nav class="flex w-9 shrink-0 flex-col items-center border-l bg-background py-1 gap-0.5">
          <button
            v-if="!panelOpen"
            type="button"
            class="rounded p-1.5 text-muted-foreground hover:bg-muted hover:text-foreground"
            title="展开面板"
            @click="panelOpen = true"
          >
            <PanelRightOpen :size="15" />
          </button>
          <div v-if="!panelOpen" class="my-1 h-px w-5 bg-border" />
          <button
            type="button"
            class="rounded p-1.5 transition-colors"
            :class="activeTab === 'overview' && panelOpen
              ? 'bg-accent text-accent-foreground'
              : 'text-muted-foreground hover:bg-muted hover:text-foreground'"
            title="实例概览"
            @click="activeTab = 'overview'; panelOpen = true"
          >
            <Info :size="16" />
          </button>
          <button
            type="button"
            class="rounded p-1.5 transition-colors"
            :class="activeTab === 'detail' && panelOpen
              ? 'bg-accent text-accent-foreground'
              : 'text-muted-foreground hover:bg-muted hover:text-foreground'"
            title="节点执行详情"
            @click="activeTab = 'detail'; panelOpen = true"
          >
            <SquareStack :size="16" />
          </button>
        </nav>
      </div>
    </div>
  </div>
</template>
