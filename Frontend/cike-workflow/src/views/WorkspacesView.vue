<template>
  <div ref="rootEl" class="space-y-6">
    <!-- Header -->
    <div class="flex items-center justify-between">
      <div>
        <h1 class="text-2xl font-semibold tracking-tight">空间管理</h1>
        <p class="mt-1 text-sm text-muted-foreground">
          工作空间用于划分工作流的管理范围
        </p>
      </div>
      <Button @click="openCreate">
        <Plus />
        新建空间
      </Button>
    </div>

    <!-- Search -->
    <div class="relative max-w-sm">
      <Search :size="16" class="absolute left-3 top-1/2 -translate-y-1/2 text-muted-foreground" />
      <Input
        v-model="keyword"
        placeholder="搜索空间..."
        class="pl-9"
        @keyup.enter="handleSearch"
      />
    </div>

    <!-- Workspace Cards -->
    <div v-if="loading" class="flex items-center justify-center py-16">
      <div class="flex items-center gap-2 text-sm text-muted-foreground">
        <div class="h-4 w-4 animate-spin rounded-full border-2 border-primary border-t-transparent" />
        加载中...
      </div>
    </div>

    <div v-else-if="workspaces.length" class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
      <div
        v-for="ws in workspaces"
        :key="ws.id"
        class="group relative flex flex-col rounded-lg border bg-card transition-colors hover:border-primary/50 hover:bg-accent/30"
      >
        <!-- 操作按钮 -->
        <div class="absolute top-2.5 right-2.5 z-10 flex gap-1 opacity-0 transition-opacity group-hover:opacity-100">
          <Button variant="ghost" size="icon-xs" title="编辑" @click.prevent="openEdit(ws)">
            <Pencil />
          </Button>
          <Button variant="ghost" size="icon-xs" title="删除" class="text-destructive hover:text-destructive" @click.prevent="openDelete(ws)">
            <Trash2 />
          </Button>
        </div>

        <RouterLink :to="`/workspaces/${ws.id}`" class="flex flex-1 flex-col p-5">
          <!-- 图标 + 名称行 -->
          <div class="flex items-start gap-3">
            <div
              class="flex h-10 w-10 shrink-0 items-center justify-center rounded-md"
              :class="getChartColor(ws.name || '').icon"
            >
              <Boxes :size="20" />
            </div>
            <div class="min-w-0 flex-1">
              <h3 class="truncate font-medium text-card-foreground">{{ ws.name }}</h3>
              <span
                class="mt-0.5 inline-block rounded px-1.5 py-0.5 font-mono text-xs"
                :class="getChartColor(ws.name || '').badge"
              >
                {{ ws.code }}
              </span>
            </div>
          </div>

          <!-- 描述（flex-1 占满剩余空间，使 footer 贴底对齐） -->
          <div class="mt-3 flex-1">
            <p class="line-clamp-2 text-sm text-muted-foreground">
              {{ ws.description || "暂无描述" }}
            </p>
          </div>

          <!-- 底部元数据分隔线（固定 mt-4，保证与描述间距一致） -->
          <div class="mt-4 flex items-center border-t pt-3 text-xs text-muted-foreground">
            <span>更新于 {{ formatDate(ws.updatedAt) }}</span>
          </div>
        </RouterLink>
      </div>
    </div>

    <!-- 加载失败 -->
    <div v-else-if="loadError" class="flex flex-col items-center justify-center rounded-lg border border-dashed py-16">
      <h3 class="font-medium">加载失败</h3>
      <p class="mt-1 text-sm text-muted-foreground">
        无法获取空间列表，请检查后端服务后重试
      </p>
      <Button class="mt-4" variant="outline" @click="reload">
        重试
      </Button>
    </div>

    <!-- Empty State -->
    <div v-else class="flex flex-col items-center justify-center rounded-lg border border-dashed py-16">
      <Boxes :size="40" class="text-muted-foreground/50" />
      <h3 class="mt-4 font-medium">暂无工作空间</h3>
      <p class="mt-1 text-sm text-muted-foreground">
        创建一个工作空间来开始管理你的工作流
      </p>
      <Button class="mt-4" @click="openCreate">
        <Plus />
        新建空间
      </Button>
    </div>

    <!-- 滚动加载状态提示 -->
    <div v-if="workspaces.length" class="pt-2">
      <div v-if="loadingMore" class="flex items-center justify-center gap-2 py-2 text-sm text-muted-foreground">
        <div class="h-4 w-4 animate-spin rounded-full border-2 border-primary border-t-transparent" />
        加载中...
      </div>
      <p v-else-if="!hasMore" class="py-2 text-center text-xs text-muted-foreground">
        已加载全部 {{ total }} 个空间
      </p>
    </div>

    <!-- 新建 / 编辑对话框 -->
    <WorkspaceFormDialog
      v-model:open="formDialogOpen"
      :workspace="editingWorkspace"
      @saved="onSaved"
    />

    <!-- 删除确认对话框 -->
    <AlertDialog v-model:open="deleteDialogOpen">
      <AlertDialogContent class="sm:max-w-md">
        <AlertDialogHeader>
          <AlertDialogTitle>确认删除</AlertDialogTitle>
          <AlertDialogDescription>
            确定要删除空间「{{ deletingWorkspace?.name }}」吗？此操作不可撤销。
          </AlertDialogDescription>
        </AlertDialogHeader>
        <p v-if="deleteError" class="text-sm text-destructive">
          {{ deleteError }}
        </p>
        <AlertDialogFooter>
          <Button variant="outline" @click="deleteDialogOpen = false">
            取消
          </Button>
          <Button variant="destructive" :disabled="deleting" @click="confirmDelete">
            {{ deleting ? '删除中...' : '删除' }}
          </Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from "vue"
import { useInfiniteScroll } from "@vueuse/core"
import { RouterLink } from "vue-router"
import { Plus, Search, Boxes, Pencil, Trash2 } from "@lucide/vue"
import { getApiV1WorkspacesPagedList, deleteApiV1Workspaces } from "@/api"
import type { WorkspaceItemDto } from "@/api"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import {
  AlertDialog,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog"
import WorkspaceFormDialog from "@/components/WorkspaceFormDialog.vue"
import { extractApiErrorMessage } from "@/lib/apiError"

// --- 图标配色：根据 workspace name 哈希选取 chart 色系（均为 style.css 定义的主题变量） ---
const CHART_COLORS = [
  { icon: "bg-chart-1/10 text-chart-1", badge: "bg-chart-1/10 text-chart-1" },
  { icon: "bg-chart-2/10 text-chart-2", badge: "bg-chart-2/10 text-chart-2" },
  { icon: "bg-chart-3/10 text-chart-3", badge: "bg-chart-3/10 text-chart-3" },
  { icon: "bg-chart-4/10 text-chart-4", badge: "bg-chart-4/10 text-chart-4" },
  { icon: "bg-chart-5/10 text-chart-5", badge: "bg-chart-5/10 text-chart-5" },
]

function getChartColor(name: string) {
  let hash = 0
  for (let i = 0; i < name.length; i++) {
    hash = name.charCodeAt(i) + ((hash << 5) - hash)
  }
  return CHART_COLORS[Math.abs(hash) % CHART_COLORS.length]
}

// --- 日期格式化 ---
function formatDate(dateStr: string | undefined): string {
  if (!dateStr) return "—"
  const d = new Date(dateStr)
  return d.toLocaleDateString("zh-CN", { year: "numeric", month: "short", day: "numeric" })
}

// --- 列表数据（滚动加载） ---
const workspaces = ref<WorkspaceItemDto[]>([])
const loading = ref(false)
const loadingMore = ref(false)
const loadError = ref(false)
const total = ref(0)
const keyword = ref("")
const page = ref(1)
const pageSize = ref(12)
const hasMore = computed(() => workspaces.value.length < total.value)

// Monotonic sequence guard: only the latest in-flight fetch may write state, so a
// search reload racing an infinite-scroll append (or overlapping searches) can't be
// clobbered by a stale response, and the stale call leaves loading flags to the newer one.
let wsFetchSeq = 0

async function fetchWorkspaces(append = false) {
  const seq = ++wsFetchSeq
  if (append) loadingMore.value = true
  else loading.value = true
  loadError.value = false
  try {
    const { data, error } = await getApiV1WorkspacesPagedList({
      query: {
        keyword: keyword.value || undefined,
        Page: page.value,
        PageSize: pageSize.value,
      },
    })
    if (seq !== wsFetchSeq) return // stale — a newer fetch owns state & loading flags
    if (error) {
      loadError.value = true
      return
    }
    const items = data?.items ?? []
    workspaces.value = append ? [...workspaces.value, ...items] : items
    total.value = Number(data?.total ?? 0)
  } finally {
    if (seq === wsFetchSeq) {
      loading.value = false
      loadingMore.value = false
    }
  }
}

// 重新加载（回到第一页，用于搜索 / 增删改后刷新）
function reload() {
  page.value = 1
  fetchWorkspaces(false)
}

function handleSearch() {
  reload()
}

// 滚动加载：复用 @vueuse/core 的 useInfiniteScroll（内置“内容未填满容器时继续加载”）
// 滚动容器是布局的 main（带 overflow-y-auto），在 onMounted 时解析
const rootEl = ref<HTMLElement | null>(null)
const scrollContainer = ref<HTMLElement | null>(null)

function getScrollParent(el: HTMLElement | null): HTMLElement | null {
  let node = el?.parentElement ?? null
  while (node) {
    const overflowY = getComputedStyle(node).overflowY
    if (overflowY === "auto" || overflowY === "scroll") return node
    node = node.parentElement
  }
  return null
}

useInfiniteScroll(
  scrollContainer,
  async () => {
    page.value += 1
    await fetchWorkspaces(true)
  },
  {
    distance: 200,
    // 节流滚动处理，避免触摸板/鼠标滚轮高频事件造成的卡顿
    throttle: 100,
    canLoadMore: () => hasMore.value && !loading.value && !loadingMore.value,
  },
)

// --- 新建 / 编辑 ---
const formDialogOpen = ref(false)
const editingWorkspace = ref<WorkspaceItemDto | null>(null)

function openCreate() {
  editingWorkspace.value = null
  formDialogOpen.value = true
}

function openEdit(ws: WorkspaceItemDto) {
  editingWorkspace.value = ws
  formDialogOpen.value = true
}

function onSaved() {
  reload()
}

// --- 删除 ---
const deleteDialogOpen = ref(false)
const deletingWorkspace = ref<WorkspaceItemDto | null>(null)
const deleting = ref(false)
const deleteError = ref("")

function openDelete(ws: WorkspaceItemDto) {
  deletingWorkspace.value = ws
  deleteError.value = ""
  deleteDialogOpen.value = true
}

async function confirmDelete() {
  if (!deletingWorkspace.value) return
  deleting.value = true
  deleteError.value = ""
  try {
    const { error } = await deleteApiV1Workspaces({
      query: { workspaceId: deletingWorkspace.value.id! },
    })
    if (error) {
      deleteError.value = extractApiErrorMessage(error, "删除失败，请稍后重试")
      return
    }
    deleteDialogOpen.value = false
    deletingWorkspace.value = null
    reload()
  } finally {
    deleting.value = false
  }
}

onMounted(() => {
  scrollContainer.value = getScrollParent(rootEl.value)
  fetchWorkspaces(false)
})
</script>
